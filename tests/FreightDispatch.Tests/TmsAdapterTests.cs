using FreightDispatch.Core;
using FreightDispatch.Core.Edi;
using FreightDispatch.Core.Model;
using FreightDispatch.Core.Tms;
using Xunit;

namespace FreightDispatch.Tests;

public class TmsAdapterTests
{
    private static readonly DateTime Clock = new(2026, 8, 20, 16, 5, 0);

    [Fact]
    public async Task A_load_reaching_the_board_is_pushed_across_without_being_asked()
    {
        (LoadBoard board, MockTmsAdapter adapter, TmsBridge bridge) = Wire();

        Load load = await Tender(board, adapter);

        TmsHeldLoad held = adapter.Held.Single();

        Assert.Equal("TMS-0001", held.TmsLoadId);
        Assert.Equal("LD10041872", held.ShipmentId);
        Assert.Equal(load.Id, held.BoardLoadId);
        Assert.NotEqual(load.Id.ToString(), held.TmsLoadId);
        Assert.Contains(bridge.Log, e => e.Kind == "push" && e.Ok);
    }

    [Fact]
    public async Task A_refusal_is_an_answer_and_not_an_exception()
    {
        // Duplicate load numbers are the commonest reason a real push comes back rejected,
        // and a board that treats a refusal as an exception crashes on an ordinary Tuesday.
        (LoadBoard board, MockTmsAdapter adapter, TmsBridge bridge) = Wire();
        Load load = await Tender(board, adapter);

        TmsPushResult second = await bridge.PushAsync(load);

        Assert.False(second.Accepted);
        Assert.Contains("already open", second.Message, StringComparison.Ordinal);
        Assert.Contains(bridge.Log, e => e.Kind == "push" && !e.Ok);
    }

    [Fact]
    public void The_adapter_owns_the_vocabulary_translation()
    {
        // The far system's codes are neither X12's nor this board's, which is the whole
        // reason the interface exists. Nothing above the adapter ever sees them.
        Assert.Equal(LoadStatus.Dispatched, MockTmsAdapter.Translate("COVERED"));
        Assert.Equal(LoadStatus.InTransit, MockTmsAdapter.Translate("ROLLING"));
        Assert.Equal(LoadStatus.Delivered, MockTmsAdapter.Translate("EMPTY"));
        Assert.Null(MockTmsAdapter.Translate("ON_FIRE"));
    }

    [Fact]
    public async Task A_status_callback_moves_the_load_and_generates_the_214()
    {
        (LoadBoard board, MockTmsAdapter adapter, _) = Wire();
        Load load = await Tender(board, adapter);

        Assert.True(await adapter.RaiseStatusAsync("LD10041872", "COVERED", Clock));

        Assert.Equal(LoadStatus.Dispatched, load.Status);

        StatusEvent statusEvent = load.Events.Single();
        Assert.Equal("XB", statusEvent.StatusCode);
        Assert.True(statusEvent.RoundTripClean);
    }

    [Fact]
    public async Task A_callback_that_jumps_ahead_walks_the_board_forward_and_sends_every_214()
    {
        // The far system says LOADED while the board still has the load as tendered, because
        // nobody clicked anything here. Refusing it would leave the two permanently out of
        // step; the partner still needs the XB and the X3.
        (LoadBoard board, MockTmsAdapter adapter, _) = Wire();
        Load load = await Tender(board, adapter);

        Assert.True(await adapter.RaiseStatusAsync("LD10041872", "LOADED", Clock, "JOLIET", "IL"));

        Assert.Equal(LoadStatus.Loaded, load.Status);
        Assert.Equal(new[] { "XB", "X3", "CP" }, load.Events.Select(e => e.StatusCode));
        Assert.All(load.Events, e => Assert.True(e.RoundTripClean));
    }

    [Fact]
    public async Task A_callback_that_reports_a_status_already_passed_is_refused_not_rewound()
    {
        // A 214 is a statement about something that happened. There is no path backwards.
        (LoadBoard board, MockTmsAdapter adapter, TmsBridge bridge) = Wire();
        Load load = await Tender(board, adapter);

        await adapter.RaiseStatusAsync("LD10041872", "ROLLING", Clock);
        int events = load.Events.Count;

        await adapter.RaiseStatusAsync("LD10041872", "COVERED", Clock);

        Assert.Equal(LoadStatus.InTransit, load.Status);
        Assert.Equal(events, load.Events.Count);
        Assert.Contains(bridge.Log, e => e.Summary.Contains("further 214", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Delivering_through_a_callback_still_produces_the_invoice()
    {
        (LoadBoard board, MockTmsAdapter adapter, _) = Wire();
        Load load = await Tender(board, adapter);

        Assert.True(await adapter.RaiseStatusAsync("LD10041872", "EMPTY", Clock));

        Assert.Equal(LoadStatus.Delivered, load.Status);
        Assert.NotNull(load.Invoice);
        Assert.True(load.Invoice!.RoundTripClean);
    }

    [Fact]
    public async Task A_callback_for_a_load_the_board_does_not_have_is_logged_and_dropped()
    {
        (LoadBoard board, MockTmsAdapter adapter, TmsBridge bridge) = Wire();
        await Tender(board, adapter);

        board.Clear();

        Assert.True(await adapter.RaiseStatusAsync("LD10041872", "COVERED", Clock));
        Assert.Contains(bridge.Log, e => e.Kind == "status" && !e.Ok);
    }

    [Fact]
    public async Task A_code_the_adapter_does_not_know_never_reaches_the_board()
    {
        (LoadBoard board, MockTmsAdapter adapter, _) = Wire();
        Load load = await Tender(board, adapter);

        Assert.False(await adapter.RaiseStatusAsync("LD10041872", "ON_FIRE", Clock));
        Assert.Equal(LoadStatus.Tendered, load.Status);
        Assert.Empty(load.Events);
    }

    [Fact]
    public async Task A_load_with_no_shipment_id_is_refused_because_there_is_nothing_to_key_on()
    {
        (_, MockTmsAdapter adapter, TmsBridge bridge) = Wire();

        TmsPushResult result = await bridge.PushAsync(new Load());

        Assert.False(result.Accepted);
        Assert.Contains("B204", result.Message, StringComparison.Ordinal);
        Assert.Empty(adapter.Held);
    }

    /// <summary>Tenders the dry van and waits for the bridge's own pump to push it across.</summary>
    private static async Task<Load> Tender(LoadBoard board, MockTmsAdapter adapter)
    {
        Load load = board.Tender(Samples.Read(Samples.DryVan))[0];

        Assert.True(
            await TempDrop.WaitUntil(() => adapter.Held.Count == 1),
            "the load was never pushed to the adapter");

        return load;
    }

    private static (LoadBoard Board, MockTmsAdapter Adapter, TmsBridge Bridge) Wire()
    {
        var board = new LoadBoard(new ControlNumbers(4001), () => Clock);
        var adapter = new MockTmsAdapter();
        var bridge = new TmsBridge(board, adapter);

        bridge.StartAsync().GetAwaiter().GetResult();

        return (board, adapter, bridge);
    }
}
