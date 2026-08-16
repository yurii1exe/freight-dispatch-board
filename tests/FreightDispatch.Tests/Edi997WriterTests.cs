using EdiX12.Core;
using FreightDispatch.Core;
using FreightDispatch.Core.Edi;
using FreightDispatch.Core.Model;
using Xunit;

namespace FreightDispatch.Tests;

public class Edi997WriterTests
{
    private static readonly DateTime Clock = new(2026, 8, 18, 9, 42, 0);

    [Fact]
    public void The_generated_997_parses_back_with_no_envelope_diagnostics()
    {
        FunctionalAcknowledgment ack = Acknowledge(Samples.Read(Samples.DryVan));

        Assert.True(
            ack.RoundTripClean,
            "Generated 997 failed re-parse: " + string.Join("; ", ack.RoundTripDiagnostics));
    }

    [Fact]
    public void A_clean_tender_is_acknowledged_accepted()
    {
        FunctionalAcknowledgment ack = Acknowledge(Samples.Read(Samples.DryVan));

        Assert.Equal("A", ack.Verdict);
        Assert.True(ack.IsAccepted);
        Assert.Empty(ack.Findings);

        IReadOnlyList<Segment> body = Body(ack);

        Assert.Equal(new[] { "AK1", "AK2", "AK5", "AK9" }, body.Select(s => s.Id));

        Segment ak1 = body.Single(s => s.Id == "AK1");
        Assert.Equal("SM", ak1[1]);      // the group being acknowledged, not FA
        Assert.Equal("4417", ak1[2]);
        Assert.Equal("005010", ak1[3]);

        Segment ak2 = body.Single(s => s.Id == "AK2");
        Assert.Equal("204", ak2[1]);
        Assert.Equal("0001", ak2[2]);

        Assert.Equal("A", body.Single(s => s.Id == "AK5")[1]);

        Segment ak9 = body.Single(s => s.Id == "AK9");
        Assert.Equal("A", ak9[1]);
        Assert.Equal("1", ak9[2]);       // declared by GE01
        Assert.Equal("1", ak9[3]);       // received
        Assert.Equal("1", ak9[4]);       // accepted
    }

    [Fact]
    public void GS01_of_the_acknowledgment_is_FA_not_the_group_it_acknowledges()
    {
        // AK101 echoes the acknowledged group's GS01. GS01 of the 997 itself is FA. Putting
        // SM in both is a file the partner routes into its load tender application.
        Interchange interchange = X12Parser.Parse(Acknowledge(Samples.Read(Samples.DryVan)).Edi);

        Assert.Equal("FA", interchange.Groups.Single().FunctionalIdentifierCode);
        Assert.Equal("997", interchange.Transactions.Single().IdentifierCode);
    }

    [Fact]
    public void The_997_goes_back_the_way_the_204_came()
    {
        Interchange interchange = X12Parser.Parse(Acknowledge(Samples.Read(Samples.DryVan)).Edi);

        Assert.Equal("DEMOCARRIER", interchange.SenderId);
        Assert.Equal("DEMOBROKER", interchange.ReceiverId);
        Assert.False(interchange.IsProduction);
    }

    [Fact]
    public void A_wrong_SE01_is_rejected_and_the_997_names_the_reason()
    {
        // The whole argument for sending one at all. samples/204-bad-se-count.edi declares
        // 21 segments where there are 22.
        FunctionalAcknowledgment ack = Acknowledge(Samples.Read(Samples.BadSeCount));

        Assert.Equal("R", ack.Verdict);
        Assert.True(ack.IsRejected);

        Segment ak5 = Body(ack).Single(s => s.Id == "AK5");
        Assert.Equal("R", ak5[1]);
        Assert.Equal("4", ak5[2]);       // element 718: segment count does not match

        Segment ak9 = Body(ack).Single(s => s.Id == "AK9");
        Assert.Equal("R", ak9[1]);
        Assert.Equal("0", ak9[4]);       // nothing accepted

        Assert.Contains(
            ack.Findings,
            f => f.Contains("Number of included segments does not match actual count", StringComparison.Ordinal));
    }

    [Fact]
    public void An_IEA02_that_does_not_echo_ISA13_is_reported_as_a_TA1_matter()
    {
        // The same defective sample also has IEA02 = 000004421 against ISA13 = 000004420.
        // A 997 acknowledges functional groups and structurally cannot say this, so it is
        // recorded rather than dropped — otherwise the sender gets an acknowledgment that
        // never mentions half of what is wrong with their file.
        FunctionalAcknowledgment ack = Acknowledge(Samples.Read(Samples.BadSeCount));

        Assert.Contains(ack.OutOfScope, f => f.Contains("TA1", StringComparison.Ordinal));
        Assert.Contains(ack.OutOfScope, f => f.Contains("000004421", StringComparison.Ordinal));
        Assert.DoesNotContain("000004421", ack.Edi, StringComparison.Ordinal);
    }

    [Fact]
    public void A_control_number_that_does_not_match_its_trailer_is_element_718_code_3()
    {
        FunctionalAcknowledgment ack = Acknowledge(
            Samples.Read(Samples.DryVan).Replace("SE*28*0001", "SE*28*0002", StringComparison.Ordinal));

        Segment ak5 = Body(ack).Single(s => s.Id == "AK5");

        Assert.Equal("R", ak5[1]);
        Assert.Equal("3", ak5[2]);
    }

    [Fact]
    public void A_wrong_GE01_is_accepted_but_errors_were_noted()
    {
        // The genuine 'E' case, and the reason it only ever appears at the group level: the
        // documents inside are perfectly usable, the envelope around them is not.
        FunctionalAcknowledgment ack = Acknowledge(
            Samples.Read(Samples.DryVan).Replace("GE*1*4417", "GE*2*4417", StringComparison.Ordinal));

        Assert.Equal("E", ack.Verdict);
        Assert.False(ack.IsRejected);

        Assert.Equal("A", Body(ack).Single(s => s.Id == "AK5")[1]);

        Segment ak9 = Body(ack).Single(s => s.Id == "AK9");
        Assert.Equal("E", ak9[1]);
        Assert.Equal("2", ak9[2]);       // AK902 is what GE01 claimed
        Assert.Equal("1", ak9[3]);       // AK903 is what was there
        Assert.Equal("1", ak9[4]);       // and it was still accepted
        Assert.Equal("5", ak9[5]);       // element 716: transaction set count does not match
    }

    [Fact]
    public void An_unsupported_transaction_set_beside_a_good_one_is_partially_accepted()
    {
        FunctionalAcknowledgment ack = Acknowledge(Fixtures.TwoTransactionSets);

        Assert.Equal("P", ack.Verdict);

        List<Segment> ak5 = Body(ack).Where(s => s.Id == "AK5").ToList();
        Assert.Equal(2, ak5.Count);
        Assert.Equal("A", ak5[0][1]);
        Assert.Equal("R", ak5[1][1]);
        Assert.Equal("1", ak5[1][2]);    // element 718: transaction set not supported

        Segment ak9 = Body(ack).Single(s => s.Id == "AK9");
        Assert.Equal("P", ak9[1]);
        Assert.Equal("2", ak9[3]);
        Assert.Equal("1", ak9[4]);
    }

    [Fact]
    public void An_unsupported_functional_group_is_rejected_whole_with_no_AK2_loops()
    {
        FunctionalAcknowledgment ack = Acknowledge(
            Samples.Read(Samples.DryVan).Replace("GS*SM*", "GS*IM*", StringComparison.Ordinal));

        Assert.Equal("R", ack.Verdict);

        IReadOnlyList<Segment> body = Body(ack);
        Assert.DoesNotContain(body, s => s.Id == "AK2");
        Assert.DoesNotContain(body, s => s.Id == "AK5");

        Segment ak9 = body.Single(s => s.Id == "AK9");
        Assert.Equal("R", ak9[1]);
        Assert.Equal("1", ak9[5]);       // element 716: functional group not supported
    }

    [Fact]
    public void A_repeated_ST02_inside_one_group_is_element_718_code_7()
    {
        // A partner resending without advancing the counter. The receiver cannot tell the
        // two documents apart, which is exactly what code 7 is for.
        FunctionalAcknowledgment ack = Acknowledge(Fixtures.DuplicateControlNumbers);

        List<Segment> ak5 = Body(ack).Where(s => s.Id == "AK5").ToList();

        Assert.Equal(2, ak5.Count);
        Assert.Equal("A", ak5[0][1]);
        Assert.Equal("R", ak5[1][1]);
        Assert.Contains("7", ak5[1].Elements);
    }

    [Fact]
    public void SE01_of_the_997_counts_the_ST_and_the_SE()
    {
        Interchange interchange = X12Parser.Parse(Acknowledge(Samples.Read(Samples.DryVan)).Edi);
        TransactionSet transaction = interchange.Transactions.Single();

        Assert.Equal(
            transaction.DeclaredSegmentCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            transaction.Trailer![1].Trim());
    }

    [Fact]
    public void Every_tender_the_board_receives_is_acknowledged()
    {
        var board = new LoadBoard(new ControlNumbers(4001), () => Clock);

        TenderReceipt receipt = board.Receive(Samples.Read(Samples.DryVan));

        Assert.Single(receipt.Loads);
        Assert.Equal("A", receipt.Acknowledgment.Verdict);
        Assert.Same(receipt.Acknowledgment, receipt.Loads[0].Acknowledgment);
        Assert.Single(board.Acknowledgments);
        Assert.False(receipt.Loads[0].TenderRejected);
    }

    [Fact]
    public void A_rejected_tender_still_reaches_the_board_and_says_so()
    {
        // The board's job is to move freight. The 997 tells the partner their file is wrong;
        // the row stays, flagged, because there is still a truck expected at a dock.
        var board = new LoadBoard(new ControlNumbers(4001), () => Clock);

        TenderReceipt receipt = board.Receive(Samples.Read(Samples.BadSeCount));

        Assert.Single(receipt.Loads);
        Assert.True(receipt.Loads[0].TenderRejected);
        Assert.Equal("4", receipt.Loads[0].TenderAcknowledgment!.ErrorCodes.Single());
        Assert.Equal(1, receipt.RejectedCount);
    }

    [Fact]
    public void An_interchange_with_no_204_in_it_is_still_acknowledged_and_explains_itself()
    {
        var board = new LoadBoard(new ControlNumbers(4001), () => Clock);

        TenderReceipt receipt = board.Receive(Fixtures.NoLoadTender);

        Assert.Empty(receipt.Loads);
        Assert.Equal("R", receipt.Acknowledgment.Verdict);
        Assert.Contains("204", receipt.Explanation, StringComparison.Ordinal);
        Assert.Contains("SM", receipt.Explanation, StringComparison.Ordinal);
    }

    private static FunctionalAcknowledgment Acknowledge(string edi) =>
        new Edi997Writer(new ControlNumbers(5001)).Write(X12Parser.Parse(edi), Clock);

    private static IReadOnlyList<Segment> Body(FunctionalAcknowledgment ack) =>
        X12Parser.Parse(ack.Edi).Transactions.First().Segments;
}
