using FreightDispatch.Core;
using FreightDispatch.Core.Edi;
using FreightDispatch.Core.Model;
using Xunit;

namespace FreightDispatch.Tests;

public class LoadBoardTests
{
    private static readonly DateTime Clock = new(2026, 8, 18, 9, 42, 0);

    [Fact]
    public void The_whole_loop_runs_204_in_six_214s_out()
    {
        var board = new LoadBoard(new ControlNumbers(4001), () => Clock);

        Load load = board.Tender(Samples.Read(Samples.DryVan)).Single();
        Assert.Equal(LoadStatus.Tendered, load.Status);

        foreach (LoadStatus status in StatusCatalog.All.Skip(1))
        {
            StatusEvent statusEvent = board.Advance(load.Id, status, occurredAt: Clock);
            Assert.Equal(status, statusEvent.Status);
            Assert.True(statusEvent.RoundTripClean, string.Join("; ", statusEvent.RoundTripDiagnostics));
        }

        Assert.Equal(LoadStatus.Delivered, load.Status);
        Assert.Equal(6, load.Events.Count);
        Assert.Equal(
            new[] { "XB", "X3", "CP", "AF", "X1", "D1" },
            load.Events.Select(e => e.StatusCode));
    }

    [Fact]
    public void A_load_cannot_skip_a_step()
    {
        // Marking freight delivered while it is still on a dock is a phone call from the
        // customer, and a board that allows the click is the reason it happens.
        var board = new LoadBoard(new ControlNumbers(4001), () => Clock);
        Load load = board.Tender(Samples.Read(Samples.DryVan)).Single();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => board.Advance(load.Id, LoadStatus.Delivered, occurredAt: Clock));

        Assert.Contains("Dispatched", error.Message, StringComparison.Ordinal);
        Assert.Equal(LoadStatus.Tendered, load.Status);
        Assert.Empty(load.Events);
    }

    [Fact]
    public void A_delivered_load_has_nowhere_left_to_go()
    {
        var board = new LoadBoard(new ControlNumbers(4001), () => Clock);
        Load load = board.Tender(Samples.Read(Samples.DryVan)).Single();

        foreach (LoadStatus status in StatusCatalog.All.Skip(1))
        {
            board.Advance(load.Id, status, occurredAt: Clock);
        }

        Assert.Null(StatusCatalog.Next(LoadStatus.Delivered));
        Assert.Throws<InvalidOperationException>(
            () => board.Advance(load.Id, LoadStatus.Delivered, occurredAt: Clock));
    }

    [Fact]
    public void An_interchange_that_is_not_a_load_tender_is_rejected_by_name()
    {
        // A 214 parses perfectly and is still not a tender. Saying so beats an empty board
        // and no explanation.
        var board = new LoadBoard(new ControlNumbers(4001), () => Clock);
        Load load = board.Tender(Samples.Read(Samples.DryVan)).Single();
        string generated = board.Advance(load.Id, LoadStatus.Dispatched, occurredAt: Clock).Edi214;

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => board.Tender(generated));

        Assert.Contains("no 204 transaction sets", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Tendering_the_same_file_twice_produces_two_loads()
    {
        // Duplicate tenders are a real operational event — a partner resends after a
        // timeout. The board does not silently merge them, because deciding that two
        // tenders are the same load is a business rule and not a parser's decision.
        var board = new LoadBoard(new ControlNumbers(4001), () => Clock);
        board.Tender(Samples.Read(Samples.DryVan));
        board.Tender(Samples.Read(Samples.DryVan));

        Assert.Equal(2, board.Loads.Count);
        Assert.Equal(2, board.Loads.Count(l => l.ShipmentId == "LD10041872"));
    }

    [Fact]
    public void Status_events_accumulate_and_are_never_rewritten()
    {
        var board = new LoadBoard(new ControlNumbers(4001), () => Clock);
        Load load = board.Tender(Samples.Read(Samples.DryVan)).Single();

        StatusEvent first = board.Advance(load.Id, LoadStatus.Dispatched, occurredAt: Clock);
        StatusEvent second = board.Advance(load.Id, LoadStatus.AtShipper, occurredAt: Clock);

        Assert.Equal(new[] { first.Id, second.Id }, load.Events.Select(e => e.Id));
        Assert.NotEqual(first.Edi214, second.Edi214);
        Assert.NotEqual(first.InterchangeControlNumber, second.InterchangeControlNumber);
    }

    [Fact]
    public void A_dispatcher_note_stays_on_the_board_and_off_the_wire()
    {
        var board = new LoadBoard(new ControlNumbers(4001), () => Clock);
        Load load = board.Tender(Samples.Read(Samples.DryVan)).Single();

        StatusEvent statusEvent = board.Advance(
            load.Id, LoadStatus.Dispatched, occurredAt: Clock, note: "Driver Ray, cell 312 555 0117");

        Assert.Equal("Driver Ray, cell 312 555 0117", statusEvent.Note);
        Assert.DoesNotContain("Ray", statusEvent.Edi214, StringComparison.Ordinal);
    }
}
