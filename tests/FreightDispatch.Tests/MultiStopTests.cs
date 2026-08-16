using EdiX12.Core;
using FreightDispatch.Core;
using FreightDispatch.Core.Edi;
using FreightDispatch.Core.Model;
using Xunit;

namespace FreightDispatch.Tests;

/// <summary>
/// The four-stop reefer: load in Fresno, part-unload in Reno, part-unload in Salt Lake City,
/// complete unload in Denver.
/// </summary>
/// <remarks>
/// Reporting every arrival against the final drop is the mistake this suite exists to
/// prevent. It is invisible on a two-stop load, which is why it survives so long, and it
/// tells the partner the freight was delivered in Fresno — which is where it loaded.
/// </remarks>
public class MultiStopTests
{
    private static readonly DateTime Clock = new(2026, 8, 18, 9, 42, 0);

    [Fact]
    public void The_pointer_starts_at_the_first_stop()
    {
        Load load = Tender(out _);

        Assert.Equal(1, load.CurrentStopSequence);
        Assert.Equal(1, load.CurrentStopOrdinal);
        Assert.Equal("VALLEY HARVEST COLD STORAGE", load.CurrentStop!.Location.Name);
        Assert.True(load.IsMultiStop);
        Assert.True(load.StopsRemainAfterCurrent);
    }

    [Fact]
    public void Every_status_message_names_the_stop_the_truck_is_actually_at()
    {
        Load load = Tender(out LoadBoard board);

        List<(string Code, string City)> reported = RunToCompletion(board, load)
            .Select(e => (e.StatusCode, MS1City(e)))
            .ToList();

        Assert.Equal(
            new[]
            {
                ("XB", "FRESNO"),          // acknowledged, heading to the pickup
                ("X3", "FRESNO"),          // arrived at pickup
                ("CP", "FRESNO"),          // loading complete
                ("AF", "FRESNO"),          // departed the pickup with the freight
                ("X1", "RENO"),            // arrived at drop one
                ("D1", "RENO"),            // part unload complete
                ("CD", "RENO"),            // departed drop one
                ("X1", "SALT LAKE CITY"),  // arrived at drop two
                ("D1", "SALT LAKE CITY"),
                ("CD", "SALT LAKE CITY"),
                ("X1", "DENVER"),          // arrived at the final drop
                ("D1", "DENVER"),          // complete unload — the load is done
            },
            reported);
    }

    [Fact]
    public void Departure_from_a_delivery_is_CD_and_from_a_pickup_is_AF()
    {
        // Both mean "the truck left", and sending AF from a consignee tells the partner the
        // freight was picked up there.
        Load load = Tender(out LoadBoard board);
        List<StatusEvent> events = RunToCompletion(board, load);

        Assert.Equal(1, events.Count(e => e.StatusCode == "AF"));
        Assert.Equal(2, events.Count(e => e.StatusCode == "CD"));
        Assert.Equal("FRESNO", MS1City(events.Single(e => e.StatusCode == "AF")));
    }

    [Fact]
    public void Leaving_an_intermediate_stop_emits_the_completion_and_the_departure()
    {
        Load load = Tender(out LoadBoard board);

        board.Advance(load.Id, LoadStatus.Dispatched, Clock);
        board.Advance(load.Id, LoadStatus.AtShipper, Clock);
        board.Advance(load.Id, LoadStatus.Loaded, Clock);
        board.Advance(load.Id, LoadStatus.InTransit, Clock);
        board.Advance(load.Id, LoadStatus.AtConsignee, Clock);

        // One click on the board. Two things the partner needs telling.
        IReadOnlyList<StatusEvent> emitted = board.Advance(load.Id, LoadStatus.InTransit, Clock);

        Assert.Equal(2, emitted.Count);
        Assert.Equal("D1", emitted[0].StatusCode);
        Assert.Equal("CD", emitted[1].StatusCode);
        Assert.All(emitted, e => Assert.Equal("RENO", MS1City(e)));
        Assert.All(emitted, e => Assert.True(e.RoundTripClean));
    }

    [Fact]
    public void The_pointer_advances_on_departure_not_on_arrival()
    {
        Load load = Tender(out LoadBoard board);

        board.Advance(load.Id, LoadStatus.Dispatched, Clock);
        board.Advance(load.Id, LoadStatus.AtShipper, Clock);
        board.Advance(load.Id, LoadStatus.Loaded, Clock);
        Assert.Equal(1, load.CurrentStopSequence);

        board.Advance(load.Id, LoadStatus.InTransit, Clock);
        Assert.Equal(2, load.CurrentStopSequence);

        board.Advance(load.Id, LoadStatus.AtConsignee, Clock);
        Assert.Equal(2, load.CurrentStopSequence);

        board.Advance(load.Id, LoadStatus.InTransit, Clock);
        Assert.Equal(3, load.CurrentStopSequence);
    }

    [Fact]
    public void The_board_cycles_at_consignee_until_the_last_stop()
    {
        Load load = Tender(out LoadBoard board);

        board.Advance(load.Id, LoadStatus.Dispatched, Clock);
        board.Advance(load.Id, LoadStatus.AtShipper, Clock);
        board.Advance(load.Id, LoadStatus.Loaded, Clock);
        board.Advance(load.Id, LoadStatus.InTransit, Clock);
        board.Advance(load.Id, LoadStatus.AtConsignee, Clock);

        // Stop 2 of 4: the next step is back on the road, not delivered.
        Assert.Equal(LoadStatus.InTransit, StatusCatalog.Next(load.Status, load.StopsRemainAfterCurrent));
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => board.Advance(load.Id, LoadStatus.Delivered, Clock));
        Assert.Contains("In transit", error.Message, StringComparison.Ordinal);

        board.Advance(load.Id, LoadStatus.InTransit, Clock);
        board.Advance(load.Id, LoadStatus.AtConsignee, Clock);
        board.Advance(load.Id, LoadStatus.InTransit, Clock);
        board.Advance(load.Id, LoadStatus.AtConsignee, Clock);

        // Stop 4 of 4: now, and only now, delivered is on offer.
        Assert.False(load.StopsRemainAfterCurrent);
        Assert.Equal(LoadStatus.Delivered, StatusCatalog.Next(load.Status, load.StopsRemainAfterCurrent));
    }

    [Fact]
    public void A_two_stop_load_still_walks_straight_through()
    {
        // The cycle must not change the ordinary case, which is most loads.
        var board = new LoadBoard(new ControlNumbers(4001), () => Clock);
        Load load = board.Tender(Samples.Read(Samples.DryVan)).Single();

        var codes = new List<string>();
        while (StatusCatalog.Next(load.Status, load.StopsRemainAfterCurrent) is { } next)
        {
            codes.AddRange(board.Advance(load.Id, next, Clock).Select(e => e.StatusCode));
        }

        Assert.Equal(new[] { "XB", "X3", "CP", "AF", "X1", "D1" }, codes);
        Assert.Equal(LoadStatus.Delivered, load.Status);
        Assert.False(load.IsMultiStop);
    }

    [Fact]
    public void Event_labels_name_the_stop_on_a_multi_stop_load()
    {
        Load load = Tender(out LoadBoard board);
        List<StatusEvent> events = RunToCompletion(board, load);

        Assert.Contains(events, e => e.Label == "Arrived stop 2 of 4");
        Assert.Contains(events, e => e.Label == "Unloaded at stop 2 of 4");
        Assert.Contains(events, e => e.Label == "Departed stop 2 of 4");
        Assert.Contains(events, e => e.Label == "Delivered — final stop");

        // And every event knows which stop it belongs to, for the detail panel.
        Assert.Equal(
            new[] { 1, 1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4 },
            events.Select(e => e.StopSequence));
    }

    [Fact]
    public void Every_generated_214_still_parses_back_clean()
    {
        Load load = Tender(out LoadBoard board);

        foreach (StatusEvent statusEvent in RunToCompletion(board, load))
        {
            Assert.True(
                statusEvent.RoundTripClean,
                $"{statusEvent.StatusCode}: {string.Join("; ", statusEvent.RoundTripDiagnostics)}");
        }
    }

    private static Load Tender(out LoadBoard board)
    {
        board = new LoadBoard(new ControlNumbers(4001), () => Clock);
        return board.Tender(Samples.Read(Samples.Reefer)).Single();
    }

    /// <summary>Walks the board's own transition graph until there is nothing left to do.</summary>
    private static List<StatusEvent> RunToCompletion(LoadBoard board, Load load)
    {
        var events = new List<StatusEvent>();

        while (StatusCatalog.Next(load.Status, load.StopsRemainAfterCurrent) is { } next)
        {
            events.AddRange(board.Advance(load.Id, next, Clock));
        }

        return events;
    }

    /// <summary>MS101 out of the generated interchange — where the truck said it was.</summary>
    private static string MS1City(StatusEvent statusEvent) =>
        X12Parser.Parse(statusEvent.Edi214)
            .Transactions.Single()
            .Segments.Single(s => s.Id == "MS1")[1];
}
