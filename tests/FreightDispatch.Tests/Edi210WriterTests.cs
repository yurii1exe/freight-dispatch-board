using System.Globalization;
using EdiX12.Core;
using FreightDispatch.Core;
using FreightDispatch.Core.Edi;
using FreightDispatch.Core.Model;
using Xunit;

namespace FreightDispatch.Tests;

public class Edi210WriterTests
{
    private static readonly DateTime Clock = new(2026, 8, 20, 16, 5, 0);

    [Fact]
    public void The_generated_210_parses_back_with_no_envelope_diagnostics()
    {
        FreightInvoice invoice = Deliver(Samples.DryVan);

        Assert.True(
            invoice.RoundTripClean,
            "Generated 210 failed re-parse: " + string.Join("; ", invoice.RoundTripDiagnostics));
    }

    [Fact]
    public void There_is_no_invoice_until_the_load_delivers()
    {
        var board = new LoadBoard(new ControlNumbers(4001), () => Clock);
        Load load = board.Tender(Samples.Read(Samples.DryVan))[0];

        board.AdvanceOne(load.Id, LoadStatus.Dispatched, Clock);
        board.AdvanceOne(load.Id, LoadStatus.AtShipper, Clock);
        board.AdvanceOne(load.Id, LoadStatus.Loaded, Clock);
        board.AdvanceOne(load.Id, LoadStatus.InTransit, Clock);
        board.AdvanceOne(load.Id, LoadStatus.AtConsignee, Clock);

        Assert.Null(load.Invoice);

        board.AdvanceOne(load.Id, LoadStatus.Delivered, Clock);

        Assert.NotNull(load.Invoice);
    }

    [Fact]
    public void The_210_is_an_IM_group_addressed_back_at_the_tendering_party()
    {
        Interchange interchange = X12Parser.Parse(Deliver(Samples.DryVan).Edi);

        Assert.Equal("DEMOCARRIER", interchange.SenderId);
        Assert.Equal("DEMOBROKER", interchange.ReceiverId);
        Assert.Equal("IM", interchange.Groups.Single().FunctionalIdentifierCode);
        Assert.Equal("210", interchange.Transactions.Single().IdentifierCode);
    }

    [Fact]
    public void Money_is_written_as_N2_with_no_decimal_point()
    {
        // $3,237.58 goes on the wire as 323758. Sending 3237.58 is an invoice some
        // receivers read as $32.38 and others reject.
        FreightInvoice invoice = Deliver(Samples.DryVan);

        Assert.Equal(323_758L, invoice.TotalCents);
        Assert.Equal(3237.58m, invoice.Total);

        Segment b3 = Body(invoice).Single(s => s.Id == "B3");
        Assert.Equal("323758", b3[7]);
        Assert.DoesNotContain('.', b3[7]);

        Segment l3 = Body(invoice).Single(s => s.Id == "L3");
        Assert.Equal("323758", l3[5]);
    }

    [Fact]
    public void The_rate_element_takes_a_decimal_point_and_the_charge_beside_it_does_not()
    {
        // L102 Freight Rate is type R; L104 Amount Charged is N2. One segment, two number
        // formats, and both are right.
        Segment l1 = Body(Deliver(Samples.DryVan)).First(s => s.Id == "L1");

        Assert.Equal("2.5", l1[2]);
        Assert.Equal("CW", l1[3]);
        Assert.Equal("265375", l1[4]);
        Assert.Equal("LHS", l1[8]);
        Assert.Equal("LINEHAUL", l1[12]);
    }

    [Fact]
    public void B3_carries_the_invoice_number_the_shipment_id_and_the_SCAC()
    {
        Segment b3 = Body(Deliver(Samples.DryVan)).Single(s => s.Id == "B3");

        Assert.Equal("INV-LD10041872", b3[2]);
        Assert.Equal("LD10041872", b3[3]);
        Assert.Equal("PP", b3[4]);
        Assert.Equal("L", b3[5]);
        Assert.Equal("20260820", b3[6]);
        Assert.Equal("DEMO", b3[11]);
    }

    [Fact]
    public void The_reference_segment_is_N9_with_the_qualifier_first()
    {
        // N901 is the qualifier and N902 the value. L11 has them the other way round, and a
        // mapper that copies the 214's handling produces a reference of type "BOL8842190".
        var references = Body(Deliver(Samples.DryVan))
            .Where(s => s.Id == "N9")
            .Select(s => (Qualifier: s[1], Value: s[2]))
            .ToList();

        Assert.Contains(("BM", "BOL8842190"), references);
        Assert.Contains(("OQ", "LD10041872"), references);
        Assert.DoesNotContain(Body(Deliver(Samples.DryVan)), s => s.Id == "L11");
    }

    [Fact]
    public void The_ship_and_delivery_dates_come_off_the_load_own_status_history()
    {
        // Not from "now", and not from the tender's appointment windows. The invoice states
        // when the freight actually moved, which is what a broker's audit compares against.
        var picked = new DateTime(2026, 8, 18, 7, 15, 0);
        var delivered = new DateTime(2026, 8, 19, 13, 5, 0);

        var board = new LoadBoard(new ControlNumbers(4001), () => Clock);
        Load load = board.Tender(Samples.Read(Samples.DryVan))[0];

        board.AdvanceOne(load.Id, LoadStatus.Dispatched, picked.AddHours(-2));
        board.AdvanceOne(load.Id, LoadStatus.AtShipper, picked.AddMinutes(-30));
        board.AdvanceOne(load.Id, LoadStatus.Loaded, picked.AddMinutes(-5));
        board.AdvanceOne(load.Id, LoadStatus.InTransit, picked);
        board.AdvanceOne(load.Id, LoadStatus.AtConsignee, delivered.AddMinutes(-40));
        board.AdvanceOne(load.Id, LoadStatus.Delivered, delivered);

        FreightInvoice invoice = load.Invoice!;

        Assert.Equal(picked, invoice.ShippedOn);
        Assert.Equal(delivered, invoice.DeliveredOn);

        var dates = Body(invoice)
            .Where(s => s.Id == "G62")
            .Select(s => (Qualifier: s[1], Date: s[2]))
            .ToList();

        Assert.Contains(("11", "20260818"), dates);    // shipped on this date
        Assert.Contains(("35", "20260819"), dates);    // delivered on this date
    }

    [Fact]
    public void A_two_stop_load_has_no_stop_off_line()
    {
        FreightInvoice invoice = Deliver(Samples.DryVan);

        Assert.Equal(new[] { "LHS", "405" }, invoice.Charges.Select(c => c.SpecialChargeCode));
    }

    [Fact]
    public void Every_stop_beyond_the_first_two_is_a_stop_off_line()
    {
        // Four stops on the reefer: a pickup, two part unloads and a final unload. The two
        // in the middle are doors the driver had to open again, and they are billed.
        FreightInvoice invoice = Deliver(Samples.Reefer);

        Assert.Equal(
            new[] { "LHS", "SOC", "SOC", "405" },
            invoice.Charges.Select(c => c.SpecialChargeCode));

        Assert.Equal(
            new[] { "STOP OFF - RENO NV", "STOP OFF - SALT LAKE CITY UT" },
            invoice.Charges.Where(c => c.SpecialChargeCode == "SOC").Select(c => c.Description));

        // 38,400 lb: $1,600.00 base + 384 cwt at $2.50, two stop-offs at $75.00, then 22%
        // fuel on the linehaul alone.
        Assert.Equal(256_000L, invoice.Charges[0].AmountCents);
        Assert.Equal(7_500L, invoice.Charges[1].AmountCents);
        Assert.Equal(56_320L, invoice.Charges[3].AmountCents);
        Assert.Equal(327_320L, invoice.TotalCents);
    }

    [Fact]
    public void Each_charge_line_is_its_own_LX_loop_and_the_line_numbers_run_in_order()
    {
        IReadOnlyList<Segment> body = Body(Deliver(Samples.Reefer));

        Assert.Equal(
            new[] { "1", "2", "3", "4" },
            body.Where(s => s.Id == "LX").Select(s => s[1]));

        Assert.Equal(
            new[] { "1", "2", "3", "4" },
            body.Where(s => s.Id == "L1").Select(s => s[1]));
    }

    [Fact]
    public void Only_lines_carrying_freight_get_an_L0()
    {
        // The fuel surcharge is not a thing that weighs anything.
        IReadOnlyList<Segment> body = Body(Deliver(Samples.DryVan));

        Segment l0 = body.Single(s => s.Id == "L0");

        Assert.Equal("1", l0[1]);
        Assert.Equal("42150", l0[4]);
        Assert.Equal("G", l0[5]);
        Assert.Equal("24", l0[8]);
        Assert.Equal("PLT", l0[9]);
        Assert.Equal("L", l0[11]);
    }

    [Fact]
    public void The_summary_L3_totals_the_weight_the_units_and_the_charges()
    {
        Segment l3 = Body(Deliver(Samples.DryVan)).Single(s => s.Id == "L3");

        Assert.Equal("42150", l3[1]);
        Assert.Equal("G", l3[2]);
        Assert.Equal("323758", l3[5]);
        Assert.Equal("24", l3[11]);
        Assert.Equal("L", l3[12]);
    }

    [Fact]
    public void The_bill_to_falls_back_to_whoever_tendered_the_load()
    {
        // The dry van tender carries no BT loop, which is normal. The invoice still has to
        // be addressed to somebody, and the interchange named them.
        Segment billTo = Body(Deliver(Samples.DryVan)).First(s => s.Id == "N1");

        Assert.Equal("BT", billTo[1]);
        Assert.Equal("DEMOBROKER", billTo[2]);
    }

    [Fact]
    public void Payment_terms_state_the_basis_date_as_well_as_the_days()
    {
        // ITD07 alone is "net 30 from something". ITD02 code 3 says the something is the
        // invoice date.
        Segment itd = Body(Deliver(Samples.DryVan)).Single(s => s.Id == "ITD");

        Assert.Equal("01", itd[1]);
        Assert.Equal("3", itd[2]);
        Assert.Equal("30", itd[7]);
        Assert.Equal("USD", Body(Deliver(Samples.DryVan)).Single(s => s.Id == "C3")[1]);
    }

    [Fact]
    public void SE01_of_the_210_counts_the_ST_and_the_SE()
    {
        TransactionSet transaction = X12Parser.Parse(Deliver(Samples.DryVan).Edi).Transactions.Single();

        Assert.Equal(
            transaction.DeclaredSegmentCount.ToString(CultureInfo.InvariantCulture),
            transaction.Trailer![1].Trim());
    }

    [Fact]
    public void The_rate_card_is_replaceable_without_touching_the_writer()
    {
        // The pricing is a demo rate card and nothing in the writer knows the numbers.
        var rates = new InvoiceRates
        {
            LinehaulBaseCents = 100_000,
            LinehaulPerHundredweightCents = 0,
            StopOffCents = 0,
            FuelSurchargeRate = 0m,
            PaymentTermsDays = 15,
        };

        var board = new LoadBoard(new ControlNumbers(4001), () => Clock, rates);
        Load load = board.Tender(Samples.Read(Samples.DryVan))[0];
        Walk(board, load);

        Assert.Equal(100_000L, load.Invoice!.TotalCents);
        Assert.Single(load.Invoice.Charges);
        Assert.Equal("15", Body(load.Invoice).Single(s => s.Id == "ITD")[7]);
    }

    private static FreightInvoice Deliver(string sample)
    {
        var board = new LoadBoard(new ControlNumbers(4001), () => Clock);
        Load load = board.Tender(Samples.Read(sample))[0];
        Walk(board, load);

        return load.Invoice!;
    }

    /// <summary>Walks a load to Delivered along the board's own transition graph.</summary>
    private static void Walk(LoadBoard board, Load load)
    {
        int step = 0;

        while (StatusCatalog.Next(load.Status, load.StopsRemainAfterCurrent) is { } next && step++ < 24)
        {
            board.Advance(load.Id, next, Clock);
        }
    }

    private static IReadOnlyList<Segment> Body(FreightInvoice invoice) =>
        X12Parser.Parse(invoice.Edi).Transactions.Single().Segments;
}
