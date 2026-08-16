using System.Collections.Concurrent;
using EdiX12.Core;
using FreightDispatch.Core.Edi;
using FreightDispatch.Core.Model;
using FreightDispatch.Core.Transport;

namespace FreightDispatch.Core;

/// <summary>
/// The board: the loads currently on it and the operations a dispatcher performs on them.
/// </summary>
/// <remarks>
/// <para>State is in memory. This is a demonstration of the tender-to-status loop, not a
/// TMS; a real board needs the loads, the status events and the control number sequence in
/// one durable transaction, because those three going out of step is how a partner ends up
/// with two interchanges numbered 000000417 and no way to tell which one was the
/// delivery.</para>
/// <para>Everything the board does is additive. A status event is a statement about
/// something that happened, and the correction for a wrong one is another 214, never an
/// edit — which is why <see cref="Load.Events"/> only ever grows.</para>
/// <para>The board owns the whole lifecycle, in EDI terms, and generates every document in
/// it. Nothing else in the application writes X12:</para>
/// <code>
/// 204 in   →  Receive   →  997 out       within minutes, before anyone has looked at it
///             board row
///          →  Advance   →  214 out       one per status event
///          →  Delivered →  210 out       the invoice, which is where the loop closes
/// </code>
/// </remarks>
public sealed class LoadBoard
{
    private static readonly AsyncLocal<bool> _suppressed = new();

    private readonly ConcurrentDictionary<Guid, Load> _loads = new();
    private readonly ConcurrentQueue<FunctionalAcknowledgment> _acknowledgments = new();
    private readonly ControlNumbers _controlNumbers;
    private readonly Edi214Writer _writer;
    private readonly Edi997Writer _acknowledgmentWriter;
    private readonly Edi210Writer _invoiceWriter;
    private readonly Func<DateTime> _clock;

    /// <summary>Creates a board.</summary>
    /// <param name="controlNumbers">
    /// The outbound control number sequence. Shared across every load and every transaction
    /// set, because ISA13 is unique per interchange and not per shipment or per document
    /// type. A separate counter per document type is a tempting mistake and produces two
    /// interchanges with the same control number on the same day.
    /// </param>
    /// <param name="clock">
    /// Supplies "now" in local time, for the ISA and GS timestamps. Injected so tests can
    /// produce a byte-identical file twice.
    /// </param>
    /// <param name="rates">The rate card the 210 prices against. Defaults to <see cref="InvoiceRates.Demo"/>.</param>
    public LoadBoard(ControlNumbers? controlNumbers = null, Func<DateTime>? clock = null, InvoiceRates? rates = null)
    {
        _controlNumbers = controlNumbers ?? new ControlNumbers(4001);
        _clock = clock ?? (() => DateTime.Now);
        _writer = new Edi214Writer(_controlNumbers);
        _acknowledgmentWriter = new Edi997Writer(_controlNumbers);
        _invoiceWriter = new Edi210Writer(_controlNumbers, rates);
    }

    /// <summary>
    /// Raised for every interchange the board generates — 997, 214 and 210 alike.
    /// </summary>
    /// <remarks>
    /// This is the board's only outward-facing seam and it is deliberately not an
    /// <c>ITransport</c> reference. The board's job ends at "here is a file and here is who
    /// it is for"; deciding whether that goes into a directory, over AS2 or nowhere at all
    /// belongs to whatever subscribed. See <see cref="Transport.TransportGateway"/>.
    /// </remarks>
    public event EventHandler<OutboundDocument>? DocumentGenerated;

    /// <summary>
    /// Raised once for every load tender that lands on the board, however it arrived.
    /// </summary>
    /// <remarks>
    /// The seam <see cref="Tms.TmsBridge"/> hangs off. A load reaching the board is a load
    /// the customer's own system needs told about, and it should not matter whether it came
    /// off the file drop or out of the paste box.
    /// </remarks>
    public event EventHandler<Load>? LoadTendered;

    /// <summary>Every load on the board, newest tender first.</summary>
    public IReadOnlyList<Load> Loads =>
        _loads.Values.OrderByDescending(l => l.ReceivedAt).ToList();

    /// <summary>
    /// Every 997 the board has sent, oldest first — including the ones for interchanges
    /// that produced no loads at all, which are the interesting ones.
    /// </summary>
    public IReadOnlyList<FunctionalAcknowledgment> Acknowledgments => _acknowledgments.ToArray();

    /// <summary>Finds a load, or null.</summary>
    /// <param name="id">The board identifier.</param>
    public Load? Find(Guid id) => _loads.TryGetValue(id, out Load? load) ? load : null;

    /// <summary>
    /// Receives an interchange: parses it, acknowledges it, and puts any load tenders in it
    /// on the board.
    /// </summary>
    /// <remarks>
    /// <para>The 997 is generated whatever happens, which is the point of this method
    /// existing beside <see cref="Tender"/>. A file containing no 204 at all, or a 204 whose
    /// SE01 is wrong, still gets acknowledged — with a rejection naming the syntax error —
    /// because a partner who hears nothing back assumes the file was fine and the truck is
    /// coming.</para>
    /// <para>The one case that produces no acknowledgment is text that could not be
    /// tokenized, which throws. There is no sender to answer and no control number to quote,
    /// and the correct response is a TA1 rather than a 997.</para>
    /// </remarks>
    /// <param name="ediText">Raw X12 text beginning with ISA.</param>
    /// <returns>The loads created and the acknowledgment that was sent.</returns>
    /// <exception cref="X12ParseException">The interchange is not readable at all.</exception>
    public TenderReceipt Receive(string ediText)
    {
        Interchange interchange = X12Parser.Parse(ediText);
        IReadOnlyList<Load> loads = Edi204Reader.Read(interchange, ediText);

        FunctionalAcknowledgment acknowledgment = _acknowledgmentWriter.Write(interchange, _clock());

        // A load whose transaction set the 997 rejected still goes on the board, and that is
        // a decision rather than an oversight. A translator's job is to reject a defective
        // document; a dispatch board's job is to move freight, and those two are not the
        // same job. `samples/204-bad-se-count.edi` declares 21 segments where there are 22 —
        // the partner is told so, in an AK5*R*4, within seconds — and meanwhile there is a
        // real truck expected at a real dock tomorrow morning, tendered by a partner who
        // will resend a corrected file at some point and would rather the load was covered
        // when they do. Hiding the row would leave the dispatcher with nothing to work and
        // no idea why.
        //
        // The row is flagged instead. See Load.TenderRejected, which the board shows on the
        // grid, and which is the thing a real operation escalates on.
        foreach (Load load in loads)
        {
            load.Acknowledgment = acknowledgment;
            _loads[load.Id] = load;
        }

        _acknowledgments.Enqueue(acknowledgment);

        foreach (Load load in loads)
        {
            LoadTendered?.Invoke(this, load);
        }

        if (acknowledgment.Edi.Length > 0)
        {
            Emit(new OutboundDocument(
                "997",
                acknowledgment.InterchangeControlNumber,
                acknowledgment.SentBy,
                acknowledgment.SentTo,
                acknowledgment.Edi,
                loads.Count > 0 ? loads[0].Id : null,
                loads.Count > 0 ? loads[0].ShipmentId : string.Empty,
                DateTimeOffset.UtcNow));
        }

        return new TenderReceipt(loads, acknowledgment);
    }

    /// <summary>
    /// Ingests a 204 and puts every load tender in it on the board.
    /// </summary>
    /// <remarks>
    /// A thin wrapper over <see cref="Receive"/> for callers that only want the loads and
    /// treat "no loads" as a failure — the paste box and the seeder. The 997 is still
    /// generated and still sent.
    /// </remarks>
    /// <param name="ediText">Raw X12 text beginning with ISA.</param>
    /// <returns>The loads created.</returns>
    /// <exception cref="X12ParseException">The interchange is not readable.</exception>
    /// <exception cref="InvalidOperationException">The interchange produced no usable load tenders.</exception>
    public IReadOnlyList<Load> Tender(string ediText)
    {
        TenderReceipt receipt = Receive(ediText);

        if (receipt.Loads.Count == 0)
        {
            throw new InvalidOperationException(receipt.Explanation);
        }

        return receipt.Loads;
    }

    /// <summary>
    /// Removes a load from the board. Not an EDI operation — this is the demo's reset
    /// button, not a 204 cancellation, which would be a new tender with B2A01 = 01.
    /// </summary>
    /// <param name="id">The board identifier.</param>
    public bool Remove(Guid id) => _loads.TryRemove(id, out _);

    /// <summary>
    /// Empties the board. The control number sequence deliberately does not reset with it:
    /// the loads are gone, but the partner has still seen those ISA13 values.
    /// </summary>
    public void Clear()
    {
        _loads.Clear();

        while (_acknowledgments.TryDequeue(out _))
        {
            // The acknowledgments go with the loads they were about.
        }
    }

    /// <summary>
    /// Advances a load to the next status and generates the 214 that reports it.
    /// </summary>
    /// <param name="id">The load's board identifier.</param>
    /// <param name="status">
    /// The status being entered. Must be exactly one step forward: a load cannot be
    /// delivered before it is loaded, and a board that lets a dispatcher click it anyway is
    /// a board that sends a partner a delivery notice for freight still sitting on a dock.
    /// </param>
    /// <param name="occurredAt">
    /// When it happened, in local time at the location. Defaults to now. Dispatchers
    /// backdate constantly — the driver called at 14:10 and the update gets keyed at 15:40
    /// — and the 214 must carry when it happened, not when it was typed.
    /// </param>
    /// <param name="reasonCode">AT702, element 1651. <c>NS</c> unless something went wrong.</param>
    /// <param name="city">MS101. Defaults to the stop the status implies.</param>
    /// <param name="state">MS102.</param>
    /// <param name="note">A dispatcher note. Stays on the board; not sent.</param>
    /// <returns>
    /// The events generated, in order — usually one. Leaving an intermediate stop produces
    /// two: the completion of the work there and the departure from it. One click on a
    /// board, two things a partner needs told.
    /// </returns>
    /// <exception cref="KeyNotFoundException">No such load.</exception>
    /// <exception cref="InvalidOperationException">The transition is not the one the board offers.</exception>
    public IReadOnlyList<StatusEvent> Advance(
        Guid id,
        LoadStatus status,
        DateTime? occurredAt = null,
        string reasonCode = "NS",
        string? city = null,
        string? state = null,
        string? note = null)
    {
        Load load = Find(id) ?? throw new KeyNotFoundException($"No load with id {id}.");

        lock (load.Events)
        {
            bool stopsRemain = load.StopsRemainAfterCurrent;

            if (!StatusCatalog.CanTransition(load.Status, status, stopsRemain))
            {
                LoadStatus? offered = StatusCatalog.Next(load.Status, stopsRemain);

                throw new InvalidOperationException(
                    $"A load in '{StatusCatalog.DescribeStatus(load.Status)}' cannot move to " +
                    $"'{StatusCatalog.DescribeStatus(status)}'. The next step is " +
                    $"'{(offered is { } value ? StatusCatalog.DescribeStatus(value) : "none — the load is delivered")}'.");
            }

            // Every event in this transition happens at the stop the truck is working now.
            // That is the whole point of the pointer: a 214 sent from stop two of four has
            // to say stop two, not the final drop.
            Stop? stop = load.CurrentStop;
            bool atPickup = stop?.IsPickup ?? false;
            DateTime happenedAt = occurredAt ?? _clock();

            var emitted = new List<StatusEvent>();

            // Leaving a stop the truck had arrived at means the work there finished, and
            // that is a separate status code from the departure. A partner tracking a
            // multi-drop load needs the D1 as much as the CD — the D1 is the proof of
            // delivery for that stop's freight.
            bool leavingAStopMidRoute =
                status == LoadStatus.InTransit && load.Status == LoadStatus.AtConsignee;

            if (leavingAStopMidRoute)
            {
                emitted.Add(Emit(
                    load,
                    load.Status,
                    stop,
                    StatusCatalog.CompletionCode(atPickup),
                    CompletionLabel(load, atPickup),
                    reasonCode,
                    happenedAt,
                    city,
                    state,
                    note: string.Empty));
            }

            emitted.Add(Emit(
                load,
                status,
                stop,
                StatusCatalog.StatusCodeFor(status, atPickup),
                EventLabel(load, status),
                reasonCode,
                happenedAt,
                city,
                state,
                note ?? string.Empty));

            load.Status = status;

            // The pointer moves on departure, because that is the moment the truck stops
            // being at one place and starts running to the next.
            if (status == LoadStatus.InTransit && load.NextStop is { } next)
            {
                load.CurrentStopSequence = next.Sequence;
            }

            // Delivered is the only status that generates a second document. The 214 says
            // the freight arrived; the 210 asks to be paid for having taken it there, and
            // the carrier is not entitled to send one until the D1 exists.
            if (status == LoadStatus.Delivered && load.Invoice is null)
            {
                Invoice(load);
            }

            return emitted;
        }
    }

    /// <summary>
    /// Prices and invoices a delivered load, and sends the 210.
    /// </summary>
    /// <remarks>
    /// Called from <see cref="Advance"/> under the load's own lock, so the invoice sees the
    /// complete status history including the D1 that triggered it — which is where the 210
    /// gets its delivery date from.
    /// </remarks>
    private void Invoice(Load load)
    {
        FreightInvoice invoice = _invoiceWriter.Write(load, _clock());
        load.Invoice = invoice;

        Emit(new OutboundDocument(
            "210",
            invoice.InterchangeControlNumber,
            load.TenderedTo,
            load.TenderedBy,
            invoice.Edi,
            load.Id,
            load.ShipmentId,
            DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Runs an action with outbound sending suppressed.
    /// </summary>
    /// <remarks>
    /// <para>For seeding. The demonstration board starts with twelve loads already part-way
    /// through their runs, and every one of those status changes goes through the same
    /// <see cref="Advance"/> the buttons do — which is the point, because it means the
    /// reader and the writers are exercised on every process start. What it must not do is
    /// put eighty interchanges into a partner's directory every time the process
    /// restarts.</para>
    /// <para>The flag is <see cref="AsyncLocal{T}"/> rather than a field because a reseed
    /// can overlap with a dispatcher clicking a status on a load that is genuinely moving,
    /// and suppressing that one would be a lost 214. Scoping it to the calling flow means
    /// only the seeding is quiet.</para>
    /// </remarks>
    /// <param name="action">The work to do quietly.</param>
    public void WithoutSending(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        bool previous = _suppressed.Value;
        _suppressed.Value = true;

        try
        {
            action();
        }
        finally
        {
            _suppressed.Value = previous;
        }
    }

    /// <summary>Hands a generated interchange to whatever is listening, and never throws at the caller.</summary>
    private void Emit(OutboundDocument document)
    {
        if (_suppressed.Value)
        {
            return;
        }

        DocumentGenerated?.Invoke(this, document);
    }

    /// <summary>Writes one 214 and records the event it reports.</summary>
    private StatusEvent Emit(
        Load load,
        LoadStatus status,
        Stop? stop,
        string statusCode,
        string label,
        string reasonCode,
        DateTime happenedAt,
        string? city,
        string? state,
        string note)
    {
        string eventCity = Coalesce(city, stop?.Location.City);
        string eventState = Coalesce(state, stop?.Location.State);
        string eventCountry = Coalesce(null, stop?.Location.Country, "US");

        Edi214Result result = _writer.Write(
            load,
            statusCode,
            reasonCode,
            happenedAt,
            eventCity,
            eventState,
            eventCountry,
            _clock());

        var statusEvent = new StatusEvent
        {
            Status = status,
            StatusCode = statusCode,
            Label = label,
            ReasonCode = reasonCode,
            OccurredAt = happenedAt,
            City = eventCity,
            State = eventState,
            Country = eventCountry,
            StopSequence = stop?.Sequence ?? 0,
            StopOrdinal = load.CurrentStopOrdinal,
            StopName = stop?.Location.Name ?? string.Empty,
            Note = note,
            Edi214 = result.Edi,
            InterchangeControlNumber = result.InterchangeControlNumber,
            TransactionControlNumber = result.TransactionControlNumber,
            RoundTripDiagnostics = result.Diagnostics,
        };

        load.Events.Add(statusEvent);

        Emit(new OutboundDocument(
            "214",
            result.InterchangeControlNumber,
            load.TenderedTo,
            load.TenderedBy,
            result.Edi,
            load.Id,
            load.ShipmentId,
            statusEvent.RecordedAt));

        return statusEvent;
    }

    /// <summary>
    /// What the event log calls a status change.
    /// </summary>
    /// <remarks>
    /// On a two-stop load the plain board vocabulary is clearer than a stop number. On a
    /// four-stop load it is the other way round: "In transit" three times in a row tells a
    /// dispatcher nothing, and "Departed stop 2 · Reno NV" tells them everything.
    /// </remarks>
    private static string EventLabel(Load load, LoadStatus status)
    {
        if (!load.IsMultiStop)
        {
            return StatusCatalog.DescribeStatus(status);
        }

        int ordinal = load.CurrentStopOrdinal;

        return status switch
        {
            LoadStatus.AtShipper or LoadStatus.AtConsignee => $"Arrived stop {ordinal} of {load.Stops.Count}",
            LoadStatus.Loaded => $"Loaded at stop {ordinal}",
            LoadStatus.InTransit => $"Departed stop {ordinal} of {load.Stops.Count}",
            LoadStatus.Delivered => "Delivered — final stop",
            _ => StatusCatalog.DescribeStatus(status),
        };
    }

    /// <summary>The label for the work-finished event emitted when leaving a stop mid-route.</summary>
    private static string CompletionLabel(Load load, bool atPickup)
    {
        string verb = atPickup ? "Loaded" : "Unloaded";
        return load.IsMultiStop
            ? $"{verb} at stop {load.CurrentStopOrdinal} of {load.Stops.Count}"
            : verb;
    }

    private static string Coalesce(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;
}
