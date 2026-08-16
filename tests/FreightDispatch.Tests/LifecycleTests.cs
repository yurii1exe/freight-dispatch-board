using EdiX12.Core;
using FreightDispatch.Core;
using FreightDispatch.Core.Edi;
using FreightDispatch.Core.Model;
using FreightDispatch.Core.Transport;
using Xunit;

namespace FreightDispatch.Tests;

/// <summary>
/// The whole loop, over a real transport, with nothing stubbed.
/// </summary>
/// <remarks>
/// Every other test in this project asserts on one document. This one asserts that the
/// documents happen — in order, in the right directory, without anybody calling a writer by
/// hand. A file goes into a watched directory the way a partner's SFTP would put it there,
/// and four transaction sets come back out of another one.
/// </remarks>
public class LifecycleTests
{
    private static readonly DateTime Clock = new(2026, 8, 20, 16, 5, 0);

    [Fact]
    public async Task A_204_dropped_in_a_directory_becomes_a_997_then_214s_then_a_210()
    {
        using var drop = new TempDrop();

        var board = new LoadBoard(new ControlNumbers(4001), () => Clock);
        await using var gateway = new TransportGateway(board, drop.Transport);
        await gateway.StartAsync();

        // ---------------------------------------------------------------- 204 in
        drop.Drop("LD10041872.edi", Samples.Read(Samples.DryVan));

        Assert.True(
            await TempDrop.WaitUntil(() => board.Loads.Count == 1),
            "the tender never reached the board");

        Load load = board.Loads.Single();
        Assert.Equal("LD10041872", load.ShipmentId);

        // --------------------------------------------------------------- 997 out
        Assert.True(
            await TempDrop.WaitUntil(() => Outbound(drop, "997").Count == 1),
            "no 997 was written");

        Interchange acknowledgment = Parse(Outbound(drop, "997").Single());

        Assert.Equal("FA", acknowledgment.Groups.Single().FunctionalIdentifierCode);
        Assert.Equal(
            "A",
            acknowledgment.Transactions.Single().Segments.Single(s => s.Id == "AK5")[1]);

        // The inbound file is filed away and will not be read again.
        Assert.True(await TempDrop.WaitUntil(() =>
            TempDrop.Files(drop.Transport.ProcessedDirectory).Count == 1));
        Assert.Empty(TempDrop.Files(drop.Transport.InboundDirectory));

        // --------------------------------------------------- work the load, 214s out
        foreach (LoadStatus status in new[]
                 {
                     LoadStatus.Dispatched,
                     LoadStatus.AtShipper,
                     LoadStatus.Loaded,
                     LoadStatus.InTransit,
                     LoadStatus.AtConsignee,
                 })
        {
            board.Advance(load.Id, status, Clock);
        }

        Assert.True(
            await TempDrop.WaitUntil(() => Outbound(drop, "214").Count == 5),
            "the five in-progress 214s did not all arrive");

        // -------------------------------------------------------- deliver, 210 out
        board.Advance(load.Id, LoadStatus.Delivered, Clock);

        Assert.True(
            await TempDrop.WaitUntil(() => Outbound(drop, "210").Count == 1),
            "no 210 was written on delivery");

        Assert.True(await gateway.WaitForIdleAsync(TimeSpan.FromSeconds(5)));

        // ------------------------------------------------------------ the whole run
        IReadOnlyList<string> everything = TempDrop.Files(drop.Transport.OutboundDirectory);
        Assert.Equal(8, everything.Count);

        // File names sort into the order the documents were generated, which is the order
        // they have to reach the partner in.
        Assert.Equal(
            new[] { "997", "214", "214", "214", "214", "214", "214", "210" },
            everything.Select(f => Parse(f).Transactions.First().IdentifierCode));

        // Six 214s from five clicks plus the delivery: the AtConsignee → Delivered step is
        // one event, but the X1 and the D1 are two.
        Assert.Equal(6, load.Events.Count);
        Assert.Equal(
            new[] { "XB", "X3", "CP", "AF", "X1", "D1" },
            load.Events.Select(e => e.StatusCode));

        // Every generated file goes back through the parser that read the tender. This is
        // the assertion the whole repository is built around.
        foreach (string file in everything)
        {
            Interchange interchange = Parse(file);
            IReadOnlyList<X12Diagnostic> diagnostics = interchange.Validate();

            Assert.True(
                diagnostics.Count == 0,
                $"{Path.GetFileName(file)} did not re-parse clean: " +
                string.Join("; ", diagnostics.Select(d => d.ToString())));

            Assert.Equal("DEMOCARRIER", interchange.SenderId);
            Assert.Equal("DEMOBROKER", interchange.ReceiverId);
        }

        // ISA13 is unique across every document the board sent, whatever its type.
        List<string> controlNumbers = everything.Select(f => Parse(f).ControlNumber).ToList();
        Assert.Equal(controlNumbers.Count, controlNumbers.Distinct().Count());

        // And the money came out at the end.
        Assert.NotNull(load.Invoice);
        Assert.Equal(323_758L, load.Invoice!.TotalCents);

        Assert.Contains(gateway.Log, e => e.Direction == TransportDirection.Inbound && e.Ok);
        Assert.Equal(8, gateway.Log.Count(e => e.Direction == TransportDirection.Outbound && e.Ok));
    }

    [Fact]
    public async Task A_defective_tender_dropped_in_the_directory_gets_a_rejecting_997_back()
    {
        using var drop = new TempDrop();

        var board = new LoadBoard(new ControlNumbers(4001), () => Clock);
        await using var gateway = new TransportGateway(board, drop.Transport);
        await gateway.StartAsync();

        drop.Drop("broken.edi", Samples.Read(Samples.BadSeCount));

        Assert.True(
            await TempDrop.WaitUntil(() => Outbound(drop, "997").Count == 1),
            "no 997 was written for the defective tender");

        IReadOnlyList<Segment> body = Parse(Outbound(drop, "997").Single()).Transactions.Single().Segments;

        Segment ak5 = body.Single(s => s.Id == "AK5");
        Assert.Equal("R", ak5[1]);
        Assert.Equal("4", ak5[2]);

        Assert.Equal("R", body.Single(s => s.Id == "AK9")[1]);

        // Rejected by the acknowledgment, still on the board, and flagged. There is a truck
        // expected at a dock either way.
        Assert.True(await TempDrop.WaitUntil(() => board.Loads.Count == 1));
        Assert.True(board.Loads.Single().TenderRejected);

        // The file was dealt with, not failed: the partner has their answer.
        Assert.True(await TempDrop.WaitUntil(() =>
            TempDrop.Files(drop.Transport.ProcessedDirectory).Count == 1));
        Assert.Empty(TempDrop.Files(drop.Transport.ErrorDirectory));
    }

    [Fact]
    public async Task Text_that_is_not_an_interchange_produces_no_acknowledgment_and_goes_to_a_human()
    {
        // The one case where silence is correct: there is no readable ISA, so there is no
        // sender to answer and no control number to quote. A TA1 needs an interchange
        // header just as much as a 997 does.
        using var drop = new TempDrop();

        var board = new LoadBoard(new ControlNumbers(4001), () => Clock);
        await using var gateway = new TransportGateway(board, drop.Transport);
        await gateway.StartAsync();

        drop.Drop("garbage.edi", "PO*1*NOT AN INTERCHANGE~");

        Assert.True(
            await TempDrop.WaitUntil(() => TempDrop.Files(drop.Transport.ErrorDirectory).Count == 1),
            "the unreadable file was not set aside");

        Assert.Empty(TempDrop.Files(drop.Transport.OutboundDirectory));
        Assert.Empty(board.Loads);
        Assert.Empty(board.Acknowledgments);
        Assert.Contains(gateway.Log, e => !e.Ok && e.Direction == TransportDirection.Inbound);
    }

    [Fact]
    public async Task A_multi_stop_load_dropped_in_produces_a_214_for_every_stop_and_one_invoice()
    {
        using var drop = new TempDrop();

        var board = new LoadBoard(new ControlNumbers(4001), () => Clock);
        await using var gateway = new TransportGateway(board, drop.Transport);
        await gateway.StartAsync();

        drop.Drop("reefer.edi", Samples.Read(Samples.Reefer));
        Assert.True(await TempDrop.WaitUntil(() => board.Loads.Count == 1));

        Load load = board.Loads.Single();
        int step = 0;

        while (StatusCatalog.Next(load.Status, load.StopsRemainAfterCurrent) is { } next && step++ < 24)
        {
            board.Advance(load.Id, next, Clock);
        }

        Assert.True(await TempDrop.WaitUntil(() => Outbound(drop, "210").Count == 1));
        Assert.True(await gateway.WaitForIdleAsync(TimeSpan.FromSeconds(5)));

        // The twelve-message walk of the four-stop reefer, one file each.
        Assert.Equal(12, Outbound(drop, "214").Count);
        Assert.Single(Outbound(drop, "997"));
        Assert.Single(Outbound(drop, "210"));

        // Two part unloads in the middle, two stop-off lines on the invoice.
        Assert.Equal(2, load.Invoice!.Charges.Count(c => c.SpecialChargeCode == "SOC"));

        foreach (string file in TempDrop.Files(drop.Transport.OutboundDirectory))
        {
            Assert.Empty(Parse(file).Validate());
        }
    }

    private static IReadOnlyList<string> Outbound(TempDrop drop, string transactionSet) =>
        TempDrop.Files(drop.Transport.OutboundDirectory)
            .Where(f => Path.GetFileName(f).EndsWith($"-{transactionSet}.edi", StringComparison.Ordinal))
            .ToList();

    private static Interchange Parse(string path) => X12Parser.Parse(File.ReadAllText(path));
}
