using EdiX12.Core;
using FreightDispatch.Core;
using FreightDispatch.Core.Edi;
using FreightDispatch.Core.Model;
using Xunit;

namespace FreightDispatch.Tests;

public class Edi214WriterTests
{
    private static readonly DateTime Clock = new(2026, 8, 18, 9, 42, 0);

    [Fact]
    public void The_generated_214_parses_back_with_no_envelope_diagnostics()
    {
        // The whole point. A generator that has never been read by a parser is a generator
        // that works until the first partner tries it.
        StatusEvent statusEvent = Advance(LoadStatus.Dispatched);

        Assert.True(
            statusEvent.RoundTripClean,
            "Generated 214 failed re-parse: " + string.Join("; ", statusEvent.RoundTripDiagnostics));
    }

    [Fact]
    public void SE01_counts_the_ST_and_the_SE_as_well_as_the_body()
    {
        StatusEvent statusEvent = Advance(LoadStatus.Dispatched);
        Interchange interchange = X12Parser.Parse(statusEvent.Edi214);
        TransactionSet transaction = interchange.Transactions.Single();

        int declared = int.Parse(transaction.Trailer![1].Trim());

        Assert.Equal(transaction.Segments.Count + 2, declared);
        Assert.Equal(transaction.DeclaredSegmentCount, declared);
    }

    [Fact]
    public void Trailers_echo_their_headers()
    {
        StatusEvent statusEvent = Advance(LoadStatus.Dispatched);
        Interchange interchange = X12Parser.Parse(statusEvent.Edi214);
        FunctionalGroup group = interchange.Groups.Single();
        TransactionSet transaction = group.Transactions.Single();

        Assert.Equal(interchange.ControlNumber, interchange.Trailer![2].Trim());
        Assert.Equal(group.ControlNumber, group.Trailer![2].Trim());
        Assert.Equal(transaction.ControlNumber, transaction.Trailer![2].Trim());
    }

    [Fact]
    public void The_ISA_is_exactly_the_fixed_width_the_specification_requires()
    {
        // 105 characters plus a terminator. Receivers read the delimiters out of it by
        // byte offset, so an ISA one character short is a file the other end cannot
        // tokenize at all.
        StatusEvent statusEvent = Advance(LoadStatus.Dispatched);
        string isaLine = statusEvent.Edi214.Split('\n')[0];

        Assert.Equal(X12Tokenizer.IsaSegmentLength, isaLine.Length);

        // ISA16, the component separator, is at offset 104 — not offset 16, which is the
        // tail of ISA02. The segment terminator is whatever follows it.
        Assert.Equal(':', isaLine[104]);
        Assert.Equal('~', isaLine[105]);
    }

    [Fact]
    public void The_214_goes_back_the_way_the_204_came()
    {
        // Sender and receiver swap. A 214 addressed the same way as the tender lands in
        // the sender's own inbound queue and gets reported as an unknown partner.
        StatusEvent statusEvent = Advance(LoadStatus.Dispatched);
        Interchange interchange = X12Parser.Parse(statusEvent.Edi214);

        Assert.Equal("DEMOCARRIER", interchange.SenderId);
        Assert.Equal("DEMOBROKER", interchange.ReceiverId);
        Assert.Equal("QM", interchange.Groups.Single().FunctionalIdentifierCode);
        Assert.Equal("214", interchange.Transactions.Single().IdentifierCode);
        Assert.False(interchange.IsProduction);
    }

    [Fact]
    public void B10_carries_the_tendering_party_shipment_id_and_the_carrier_SCAC()
    {
        StatusEvent statusEvent = Advance(LoadStatus.Dispatched);
        Segment b10 = Body(statusEvent).Single(s => s.Id == "B10");

        Assert.Equal("LD10041872", b10[2]);
        Assert.Equal("DEMO", b10[3]);
    }

    [Theory]
    [InlineData(LoadStatus.Dispatched, "XB")]
    [InlineData(LoadStatus.AtShipper, "X3")]
    [InlineData(LoadStatus.Loaded, "CP")]
    [InlineData(LoadStatus.InTransit, "AF")]
    [InlineData(LoadStatus.AtConsignee, "X1")]
    [InlineData(LoadStatus.Delivered, "D1")]
    public void Each_board_state_emits_its_element_1650_code(LoadStatus status, string expected)
    {
        LoadBoard board = Board(out Guid id);

        StatusEvent? last = null;
        foreach (LoadStatus step in StatusCatalog.All.Skip(1))
        {
            last = board.Advance(id, step, occurredAt: Clock);
            if (step == status)
            {
                break;
            }
        }

        Segment at7 = Body(last!).Single(s => s.Id == "AT7");

        Assert.Equal(expected, at7[1]);
        Assert.Equal("NS", at7[2]);
        Assert.Equal("LT", at7[7]);
        Assert.Equal(expected, last!.StatusCode);
    }

    [Fact]
    public void AT7_carries_when_it_happened_not_when_it_was_keyed()
    {
        // Dispatchers backdate constantly: the driver called at 14:10 and the update gets
        // keyed at 15:40. The 214 has to say 14:10.
        var happened = new DateTime(2026, 8, 18, 14, 10, 0);
        LoadBoard board = Board(out Guid id);
        StatusEvent statusEvent = board.Advance(id, LoadStatus.Dispatched, occurredAt: happened);

        Segment at7 = Body(statusEvent).Single(s => s.Id == "AT7");

        Assert.Equal("20260818", at7[5]);
        Assert.Equal("1410", at7[6]);

        // While the interchange itself is stamped with the generation time.
        Interchange interchange = X12Parser.Parse(statusEvent.Edi214);
        Assert.Equal(Clock, interchange.InterchangeDate);
    }

    [Fact]
    public void MS1_names_the_stop_the_status_happened_at()
    {
        LoadBoard board = Board(out Guid id);

        board.Advance(id, LoadStatus.Dispatched, occurredAt: Clock);
        StatusEvent atShipper = board.Advance(id, LoadStatus.AtShipper, occurredAt: Clock);
        board.Advance(id, LoadStatus.Loaded, occurredAt: Clock);
        board.Advance(id, LoadStatus.InTransit, occurredAt: Clock);
        StatusEvent atConsignee = board.Advance(id, LoadStatus.AtConsignee, occurredAt: Clock);

        Segment pickup = Body(atShipper).Single(s => s.Id == "MS1");
        Assert.Equal("JOLIET", pickup[1]);
        Assert.Equal("IL", pickup[2]);

        Segment delivery = Body(atConsignee).Single(s => s.Id == "MS1");
        Assert.Equal("MEMPHIS", delivery[1]);
        Assert.Equal("TN", delivery[2]);
    }

    [Fact]
    public void The_party_loop_is_in_the_detail_area_after_the_status_not_before_it()
    {
        // 5010 puts the N1 loop inside the LX loop, after AT7/MS1/MS2. Putting it in the
        // heading next to B10 is the intuitive order and the wrong one.
        IReadOnlyList<Segment> body = Body(Advance(LoadStatus.Dispatched));
        List<string> ids = body.Select(s => s.Id).ToList();

        Assert.Equal(
            new[] { "B10", "LX", "L11", "L11", "AT7", "MS1", "N1", "N3", "N4", "N1", "N3", "N4" },
            ids);
        Assert.True(ids.IndexOf("AT7") < ids.IndexOf("N1"));
    }

    [Fact]
    public void Reference_numbers_from_the_tender_come_back_on_the_status()
    {
        IReadOnlyList<Segment> body = Body(Advance(LoadStatus.Dispatched));
        var references = body.Where(s => s.Id == "L11").Select(s => (s[1], s[2])).ToList();

        Assert.Contains(("BOL8842190", "BM"), references);
        Assert.Contains(("LD10041872", "OQ"), references);
    }

    [Fact]
    public void MS2_appears_only_when_the_tender_named_a_trailer()
    {
        Assert.DoesNotContain(Body(Advance(LoadStatus.Dispatched)), s => s.Id == "MS2");

        LoadBoard reefer = new(new ControlNumbers(9001), () => Clock);
        Guid id = reefer.Tender(Samples.Read(Samples.Reefer))[0].Id;
        StatusEvent statusEvent = reefer.Advance(id, LoadStatus.Dispatched, occurredAt: Clock);

        Segment ms2 = Body(statusEvent).Single(s => s.Id == "MS2");
        Assert.Equal("SMPL", ms2[1]);
        Assert.Equal("531104", ms2[2]);
    }

    [Fact]
    public void Control_numbers_ascend_and_never_repeat()
    {
        LoadBoard board = Board(out Guid id);

        var issued = StatusCatalog.All
            .Skip(1)
            .Select(status => board.Advance(id, status, occurredAt: Clock).InterchangeControlNumber)
            .ToList();

        Assert.Equal(issued.Count, issued.Distinct().Count());
        Assert.Equal(issued.OrderBy(n => n, StringComparer.Ordinal), issued);
        Assert.All(issued, n => Assert.Equal(9, n.Length));
    }

    [Fact]
    public void A_pipe_delimited_tender_still_produces_a_conventional_214()
    {
        // Inbound delimiters are the sender's choice; outbound are ours. Echoing a
        // partner's '|' back at them is a decision, not a default, and the default here is
        // the conventional set.
        var board = new LoadBoard(new ControlNumbers(7001), () => Clock);
        Guid id = board.Tender(Samples.Read(Samples.PipeDelimited))[0].Id;

        StatusEvent statusEvent = board.Advance(id, LoadStatus.Dispatched, occurredAt: Clock);

        Assert.True(statusEvent.RoundTripClean);
        Assert.Equal('*', X12Tokenizer.ReadDelimiters(statusEvent.Edi214).Element);
        Assert.Equal('~', X12Tokenizer.ReadDelimiters(statusEvent.Edi214).Segment);
    }

    [Fact]
    public void Element_data_never_contains_a_delimiter()
    {
        // X12 has no escape sequence. A consignee named "SMITH * SONS" tendered into a
        // star-delimited interchange would silently become two elements.
        var writer = new X12Writer();
        writer.BeginInterchange("ZZ", "SENDER", "ZZ", "RECEIVER", Clock, "1", production: false);
        writer.BeginGroup("QM", "SENDER", "RECEIVER", Clock, "1");
        writer.BeginTransaction("214", "0001");
        writer.Segment("N1", "CN", "SMITH * SONS ~ INC");
        writer.EndTransaction();
        writer.EndGroup();
        writer.EndInterchange();

        Interchange interchange = X12Parser.Parse(writer.ToString());
        Segment n1 = interchange.Transactions.Single().Segments.Single();

        Assert.Equal("SMITH  SONS  INC", n1[2]);
        Assert.Empty(interchange.Validate());
    }

    private static StatusEvent Advance(LoadStatus status)
    {
        LoadBoard board = Board(out Guid id);
        return board.Advance(id, status, occurredAt: Clock);
    }

    private static LoadBoard Board(out Guid loadId)
    {
        var board = new LoadBoard(new ControlNumbers(4001), () => Clock);
        loadId = board.Tender(Samples.Read(Samples.DryVan))[0].Id;
        return board;
    }

    private static IReadOnlyList<Segment> Body(StatusEvent statusEvent) =>
        X12Parser.Parse(statusEvent.Edi214).Transactions.Single().Segments;
}
