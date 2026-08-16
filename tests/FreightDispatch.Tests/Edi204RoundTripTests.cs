using FreightDispatch.Core.Edi;
using FreightDispatch.Core.Model;
using Xunit;

namespace FreightDispatch.Tests;

/// <summary>
/// Reads a tender, writes it back out, reads it again. A reader you cannot round-trip is a
/// reader you are taking on faith — an element read from the wrong position survives
/// eyeballing a fixture and does not survive this.
/// </summary>
public class Edi204RoundTripTests
{
    private static readonly DateTime Clock = new(2026, 8, 18, 9, 42, 0);

    [Theory]
    [InlineData(Samples.DryVan)]
    [InlineData(Samples.Reefer)]
    [InlineData(Samples.PipeDelimited)]
    public void Every_field_the_board_uses_survives_a_round_trip(string sample)
    {
        Load original = Edi204Reader.Read(Samples.Read(sample)).Single();

        string rewritten = new Edi204Writer(new ControlNumbers(6001)).Write(original, Clock);
        Load reread = Edi204Reader.Read(rewritten).Single();

        Assert.Empty(reread.TenderDiagnostics);

        Assert.Equal(original.ShipmentId, reread.ShipmentId);
        Assert.Equal(original.Scac, reread.Scac);
        Assert.Equal(original.PaymentMethod, reread.PaymentMethod);
        Assert.Equal(original.PurposeCode, reread.PurposeCode);
        Assert.Equal(original.EquipmentCode, reread.EquipmentCode);
        Assert.Equal(original.EquipmentLength, reread.EquipmentLength);
        Assert.Equal(original.TemperatureControl, reread.TemperatureControl);
        Assert.Equal(original.TrailerNumber, reread.TrailerNumber);
        Assert.Equal(original.TotalWeight, reread.TotalWeight);
        Assert.Equal(original.WeightQualifier, reread.WeightQualifier);
        Assert.Equal(original.Notes, reread.Notes);

        Assert.Equal(
            original.References.Select(r => (r.Value, r.Qualifier)),
            reread.References.Select(r => (r.Value, r.Qualifier)));

        Assert.Equal(original.Stops.Count, reread.Stops.Count);

        foreach ((Stop before, Stop after) in original.Stops.Zip(reread.Stops))
        {
            Assert.Equal(before.Sequence, after.Sequence);
            Assert.Equal(before.ReasonCode, after.ReasonCode);
            Assert.Equal(before.Weight, after.Weight);
            Assert.Equal(before.WeightUnit, after.WeightUnit);
            Assert.Equal(before.Units, after.Units);
            Assert.Equal(before.UnitOfMeasure, after.UnitOfMeasure);
            Assert.Equal(before.Commodities, after.Commodities);
            Assert.Equal(before.Notes, after.Notes);
            Assert.Equal(before.Window.Earliest, after.Window.Earliest);
            Assert.Equal(before.Window.Latest, after.Window.Latest);
            Assert.Equal(before.Window.TimeCode, after.Window.TimeCode);

            Assert.Equal(before.Location.Name, after.Location.Name);
            Assert.Equal(before.Location.EntityIdentifierCode, after.Location.EntityIdentifierCode);
            Assert.Equal(before.Location.IdQualifier, after.Location.IdQualifier);
            Assert.Equal(before.Location.IdCode, after.Location.IdCode);
            Assert.Equal(before.Location.Address1, after.Location.Address1);
            Assert.Equal(before.Location.City, after.Location.City);
            Assert.Equal(before.Location.State, after.Location.State);
            Assert.Equal(before.Location.PostalCode, after.Location.PostalCode);
            Assert.Equal(before.Location.Country, after.Location.Country);
            Assert.Equal(before.Location.ContactName, after.Location.ContactName);
            Assert.Equal(before.Location.ContactPhone, after.Location.ContactPhone);

            Assert.Equal(
                before.References.Select(r => (r.Value, r.Qualifier)),
                after.References.Select(r => (r.Value, r.Qualifier)));
        }
    }

    [Fact]
    public void A_rewritten_tender_declares_the_right_segment_count()
    {
        Load original = Edi204Reader.Read(Samples.Read(Samples.Reefer)).Single();
        string rewritten = new Edi204Writer(new ControlNumbers(6001)).Write(original, Clock);

        EdiX12.Core.TransactionSet transaction = EdiX12.Core.X12Parser.Parse(rewritten).Transactions.Single();

        Assert.Equal("204", transaction.IdentifierCode);
        Assert.Equal(transaction.DeclaredSegmentCount, int.Parse(transaction.Trailer![1].Trim()));
    }

    [Fact]
    public void A_purchase_order_read_from_an_OID_is_written_back_as_an_L11()
    {
        // Not every read has a symmetric write. The reefer sample's second stop carries its
        // PO in OID02; on the way back out it becomes an L11 with a PO qualifier, because
        // the board's model holds it as a reference and no longer knows which segment it
        // arrived in. That is a lossy round trip and worth stating rather than discovering.
        Load original = Edi204Reader.Read(Samples.Read(Samples.Reefer)).Single();
        string rewritten = new Edi204Writer(new ControlNumbers(6001)).Write(original, Clock);

        Assert.DoesNotContain("OID", rewritten, StringComparison.Ordinal);
        Assert.Contains("L11*PO-77120*PO", rewritten, StringComparison.Ordinal);
    }
}
