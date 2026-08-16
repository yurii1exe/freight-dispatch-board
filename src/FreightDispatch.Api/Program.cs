using System.Text.Json.Serialization;
using EdiX12.Core;
using FreightDispatch.Api;
using FreightDispatch.Core;
using FreightDispatch.Core.Edi;
using FreightDispatch.Core.Model;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
});

// One board, one control number sequence, for the lifetime of the process. See the
// remarks on LoadBoard: this is a demonstration of the loop, not a durable TMS.
builder.Services.AddSingleton(_ => new LoadBoard());
builder.Services.AddSingleton<SampleLibrary>();

// The Angular dev server runs on its own origin. In a published build the API serves the
// compiled client from wwwroot and the same-origin case applies, so this policy only ever
// matters while developing.
const string DevCors = "angular-dev-server";
builder.Services.AddCors(options => options.AddPolicy(DevCors, policy => policy
    .WithOrigins("http://localhost:4200", "https://localhost:4200")
    .AllowAnyHeader()
    .AllowAnyMethod()));

WebApplication app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseCors(DevCors);
}

app.UseDefaultFiles();
app.UseStaticFiles();

// A board that starts empty demonstrates nothing. Seeding also runs the 204 reader over
// every bundled sample at startup, so a broken walk shows up as an empty grid rather than
// as a surprise the first time someone pastes a file.
BoardSeeder.Seed(
    app.Services.GetRequiredService<LoadBoard>(),
    app.Services.GetRequiredService<SampleLibrary>(),
    DateTime.Now);

// ---------------------------------------------------------------------------
// Board
// ---------------------------------------------------------------------------

app.MapGet("/api/loads", (LoadBoard board) =>
        board.Loads.Select(Contracts.ToSummary).ToList())
    .WithName("ListLoads")
    .WithSummary("Every load on the board, newest tender first.");

app.MapGet("/api/loads/{id:guid}", Results<Ok<LoadDetail>, NotFound> (Guid id, LoadBoard board) =>
    {
        Load? load = board.Find(id);
        return load is null ? TypedResults.NotFound() : TypedResults.Ok(Contracts.ToDetail(load));
    })
    .WithName("GetLoad")
    .WithSummary("One load with its stops, references, notes, status history and the 204 it came from.");

app.MapDelete("/api/loads/{id:guid}", Results<NoContent, NotFound> (Guid id, LoadBoard board) =>
        board.Remove(id) ? TypedResults.NoContent() : TypedResults.NotFound())
    .WithName("RemoveLoad")
    .WithSummary("Takes a load off the board. Not a 204 cancellation — this is the demo's clear button.");

// ---------------------------------------------------------------------------
// Tender in
// ---------------------------------------------------------------------------

app.MapPost("/api/loads/tender", async Task<Results<Ok<TenderResult>, BadRequest<ProblemDetails>>> (
        HttpRequest request,
        LoadBoard board) =>
    {
        string edi = await ReadTenderBody(request);

        if (string.IsNullOrWhiteSpace(edi))
        {
            return TypedResults.BadRequest(Problem(
                "Empty tender",
                "The request body contained no X12 text. Post the raw interchange as text/plain, or {\"edi\": \"ISA*...\"} as JSON."));
        }

        try
        {
            IReadOnlyList<Load> loads = board.Tender(edi);
            X12Delimiters delimiters = X12Tokenizer.ReadDelimiters(edi);
            int segmentCount = X12Tokenizer.Tokenize(edi).Count;

            return TypedResults.Ok(new TenderResult(
                loads.Select(Contracts.ToSummary).ToList(),
                loads[0].TenderDiagnostics,
                segmentCount,
                delimiters.ToString()));
        }
        catch (X12ParseException ex)
        {
            // A structural failure means the file could not be read at all. Say where.
            return TypedResults.BadRequest(Problem(
                "Unreadable interchange",
                ex.Message,
                ex.SegmentPosition));
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.BadRequest(Problem("Not a load tender", ex.Message));
        }
    })
    .Accepts<TenderRequest>("text/plain", "application/json")
    .WithName("Tender")
    .WithSummary("Ingests a 204 and puts every load tender in it on the board.");

// ---------------------------------------------------------------------------
// Status out
// ---------------------------------------------------------------------------

app.MapPost("/api/loads/{id:guid}/status",
        Results<Ok<List<StatusEventDto>>, NotFound, BadRequest<ProblemDetails>> (
            Guid id,
            AdvanceRequest body,
            LoadBoard board) =>
        {
            if (board.Find(id) is null)
            {
                return TypedResults.NotFound();
            }

            if (!Enum.TryParse(body.Status, ignoreCase: true, out LoadStatus status))
            {
                return TypedResults.BadRequest(Problem(
                    "Unknown status",
                    $"'{body.Status}' is not a board status. Valid values: " +
                    string.Join(", ", StatusCatalog.All.Select(s => s.ToString())) + "."));
            }

            try
            {
                IReadOnlyList<StatusEvent> emitted = board.Advance(
                    id,
                    status,
                    body.OccurredAt,
                    string.IsNullOrWhiteSpace(body.ReasonCode) ? "NS" : body.ReasonCode!,
                    body.City,
                    body.State,
                    body.Note);

                return TypedResults.Ok(emitted.Select(Contracts.ToEvent).ToList());
            }
            catch (InvalidOperationException ex)
            {
                return TypedResults.BadRequest(Problem("Invalid transition", ex.Message));
            }
        })
    .WithName("AdvanceStatus")
    .WithSummary("Moves a load to its next status and generates the 214s that report it. Leaving an intermediate stop produces two.");

app.MapGet("/api/loads/{id:guid}/events/{eventId:guid}/edi",
        Results<ContentHttpResult, NotFound> (Guid id, Guid eventId, LoadBoard board) =>
        {
            StatusEvent? statusEvent = board.Find(id)?.Events.FirstOrDefault(e => e.Id == eventId);

            return statusEvent is null
                ? TypedResults.NotFound()
                : TypedResults.Text(statusEvent.Edi214, "application/edi-x12");
        })
    .WithName("GetEventEdi")
    .WithSummary("The generated 214 for one status event, as it would go on the wire.");

app.MapGet("/api/loads/{id:guid}/edi/204",
        Results<ContentHttpResult, NotFound> (Guid id, LoadBoard board) =>
        {
            Load? load = board.Find(id);
            return load is null
                ? TypedResults.NotFound()
                : TypedResults.Text(load.SourceEdi, "application/edi-x12");
        })
    .WithName("GetTenderEdi")
    .WithSummary("The 204 this load arrived on, byte for byte as received.");

// ---------------------------------------------------------------------------
// Reference data and samples
// ---------------------------------------------------------------------------

app.MapGet("/api/statuses", () => StatusCatalog.All
        .Select(status =>
        {
            // The rail shows the pickup leg, which is the two-stop case and what the README
            // documents. On a multi-drop load the same states emit the delivery codes —
            // X1 for arrival, D1 for completion, CD for departure — and the detail panel
            // shows the code that was actually sent on each event.
            bool pickupLeg = status
                is LoadStatus.AtShipper
                or LoadStatus.Loaded
                or LoadStatus.InTransit;

            string code = status == LoadStatus.Tendered
                ? string.Empty
                : StatusCatalog.StatusCodeFor(status, pickupLeg);

            return new StatusOption(
                status.ToString(),
                StatusCatalog.DescribeStatus(status),
                (int)status,
                code,
                code.Length == 0 ? string.Empty : StatusCatalog.DescribeStatusCode(code));
        })
        .ToList())
    .WithName("ListStatuses")
    .WithSummary("The board states and the X12 element 1650 code each one emits on the pickup leg.");

app.MapGet("/api/samples", (SampleLibrary samples) => samples.All)
    .WithName("ListSamples")
    .WithSummary("The bundled sample 204 tenders. All invented from the public specification.");

app.MapPost("/api/samples/{name}/tender",
        Results<Ok<TenderResult>, NotFound, BadRequest<ProblemDetails>> (
            string name,
            SampleLibrary samples,
            LoadBoard board) =>
        {
            SampleTender? sample = samples.Find(name);
            if (sample is null)
            {
                return TypedResults.NotFound();
            }

            try
            {
                IReadOnlyList<Load> loads = board.Tender(sample.Edi);
                X12Delimiters delimiters = X12Tokenizer.ReadDelimiters(sample.Edi);

                return TypedResults.Ok(new TenderResult(
                    loads.Select(Contracts.ToSummary).ToList(),
                    loads[0].TenderDiagnostics,
                    X12Tokenizer.Tokenize(sample.Edi).Count,
                    delimiters.ToString()));
            }
            catch (X12ParseException ex)
            {
                return TypedResults.BadRequest(Problem("Unreadable interchange", ex.Message, ex.SegmentPosition));
            }
        })
    .WithName("TenderSample")
    .WithSummary("Ingests one of the bundled samples.");

app.MapPost("/api/board/reset", (LoadBoard board, SampleLibrary samples) =>
    {
        BoardSeeder.Seed(board, samples, DateTime.Now);
        return board.Loads.Select(Contracts.ToSummary).ToList();
    })
    .WithName("ResetBoard")
    .WithSummary("Clears the board and reseeds it with the bundled samples and the invented lanes.");

app.MapDelete("/api/board", (LoadBoard board) =>
    {
        board.Clear();
        return TypedResults.NoContent();
    })
    .WithName("ClearBoard")
    .WithSummary("Empties the board without reseeding.");

app.MapGet("/api/health", () => new { status = "ok" }).ExcludeFromDescription();

// The Angular client is a single page; anything that is not an API route and not a file
// on disk is one of its routes.
app.MapFallbackToFile("index.html");

app.Run();

// Reads the tender out of the request, accepting either the raw interchange as text/plain
// or a JSON object with an "edi" property. Both exist because both happen: a partner's AS2
// or SFTP drop is a file, a browser paste-box is JSON. Accepting only one means the other
// end writes a shim.
static async Task<string> ReadTenderBody(HttpRequest request)
{
    if (request.HasJsonContentType())
    {
        TenderRequest? body = await request.ReadFromJsonAsync<TenderRequest>();
        return body?.Edi ?? string.Empty;
    }

    using var reader = new StreamReader(request.Body);
    return await reader.ReadToEndAsync();
}

// Builds a problem response that names the offending segment when there is one.
static ProblemDetails Problem(string title, string detail, int? segmentPosition = null)
{
    var problem = new ProblemDetails
    {
        Title = title,
        Detail = detail,
        Status = StatusCodes.Status400BadRequest,
    };

    if (segmentPosition is not null)
    {
        problem.Extensions["segmentPosition"] = segmentPosition;
    }

    return problem;
}
