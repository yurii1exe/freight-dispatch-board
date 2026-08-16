using FreightDispatch.Core.Edi;
using FreightDispatch.Core.Model;
using Xunit;

namespace FreightDispatch.Tests;

public class Edi204ReaderTests
{
    [Fact]
    public void Reads_the_shipment_identification_and_carrier_from_B2()
    {
        Load load = Single(Samples.DryVan);

        Assert.Equal("LD10041872", load.ShipmentId);
        Assert.Equal("DEMO", load.Scac);
        Assert.Equal("PP", load.PaymentMethod);
        Assert.Equal("00", load.PurposeCode);
    }

    [Fact]
    public void Reads_equipment_from_the_positions_N7_actually_uses()
    {
        // N711 is the equipment description code and N715 the length. They sit ten and
        // fourteen empty elements into the segment, which is exactly the kind of counting
        // a test earns its keep on.
        Load load = Single(Samples.DryVan);

        Assert.Equal("TF", load.EquipmentCode);
        Assert.Equal("53", load.EquipmentLength);
        Assert.Equal(string.Empty, load.TrailerNumber);
        Assert.Equal(string.Empty, load.TemperatureControl);
    }

    [Fact]
    public void Reads_the_reefer_temperature_and_pre_assigned_trailer()
    {
        Load load = Single(Samples.Reefer);

        Assert.Equal("RT", load.EquipmentCode);
        Assert.Equal("53", load.EquipmentLength);
        Assert.Equal("34F", load.TemperatureControl);
        Assert.Equal("531104", load.TrailerNumber);
    }

    [Fact]
    public void Splits_header_references_from_stop_references()
    {
        Load load = Single(Samples.DryVan);

        Assert.Equal(
            new[] { ("LD10041872", "OQ"), ("BOL8842190", "BM") },
            load.References.Select(r => (r.Value, r.Qualifier)));

        // PO-556231 arrived after the first S5, so it belongs to that stop and not to the
        // load. Getting this wrong puts stop two's purchase order on stop one.
        Assert.Equal(
            new[] { ("PO-556231", "PO") },
            load.Stops[0].References.Select(r => (r.Value, r.Qualifier)));
        Assert.Equal(
            new[] { ("DO-99120", "DO") },
            load.Stops[1].References.Select(r => (r.Value, r.Qualifier)));
    }

    [Fact]
    public void Builds_a_window_from_the_pair_of_G62_qualifiers()
    {
        Load load = Single(Samples.DryVan);
        Stop pickup = load.Stops[0];

        // G62*37 opens the window, G62*38 closes it.
        Assert.Equal(new DateTime(2026, 8, 18, 7, 0, 0), pickup.Window.Earliest);
        Assert.Equal(new DateTime(2026, 8, 18, 12, 0, 0), pickup.Window.Latest);
        Assert.True(pickup.Window.IsAppointment);
        Assert.Equal("LT", pickup.Window.TimeCode);

        Stop delivery = load.Stops[1];
        Assert.Equal(new DateTime(2026, 8, 19, 6, 0, 0), delivery.Window.Earliest);
        Assert.Equal(new DateTime(2026, 8, 19, 14, 0, 0), delivery.Window.Latest);
    }

    [Fact]
    public void Attaches_addresses_and_contacts_to_the_stop_that_opened_the_N1_loop()
    {
        Load load = Single(Samples.DryVan);

        Party shipper = load.Stops[0].Location;
        Assert.Equal("SH", shipper.EntityIdentifierCode);
        Assert.Equal("NORTHWIND FOODS PROCESSING", shipper.Name);
        Assert.Equal("1450 CATERPILLAR DRIVE", shipper.Address1);
        Assert.Equal("JOLIET, IL", shipper.CityState);
        Assert.Equal("60436", shipper.PostalCode);
        Assert.Equal("DANA WHITFIELD", shipper.ContactName);
        Assert.Equal("7735550188", shipper.ContactPhone);

        Party consignee = load.Stops[1].Location;
        Assert.Equal("CN", consignee.EntityIdentifierCode);
        Assert.Equal("MEMPHIS, TN", consignee.CityState);
        Assert.Equal("MARCUS ELLERY", consignee.ContactName);
    }

    [Fact]
    public void Keeps_stops_in_S501_sequence_and_classifies_them()
    {
        Load load = Single(Samples.Reefer);

        Assert.Equal(new[] { 1, 2, 3, 4 }, load.Stops.Select(s => s.Sequence));
        Assert.Equal(new[] { "CL", "PU", "PU", "CU" }, load.Stops.Select(s => s.ReasonCode));
        Assert.Equal(new[] { true, false, false, false }, load.Stops.Select(s => s.IsPickup));
        Assert.Equal("FRESNO, CA", load.Origin!.Location.CityState);
        Assert.Equal("DENVER, CO", load.Destination!.Location.CityState);
        Assert.Equal(2, load.ExtraStops);
    }

    [Fact]
    public void Reads_a_purchase_order_out_of_an_OID_as_well_as_an_L11()
    {
        // Stop two carries its PO in OID02, stops three and four in L11. Both forms are
        // ordinary and a board that only reads one of them loses references silently.
        Load load = Single(Samples.Reefer);

        Assert.Contains(load.Stops[1].References, r => r.Value == "PO-77120" && r.Qualifier == "PO");
        Assert.Contains(load.Stops[2].References, r => r.Value == "PO-77121" && r.Qualifier == "PO");
    }

    [Fact]
    public void Reads_a_pipe_delimited_tender_identically()
    {
        // '|' element separator, '>' component separator, newline segment terminator, '!'
        // repetition separator. All declared in the ISA, all legal, and the file that
        // breaks every parser that starts with text.Split('~').
        Load load = Single(Samples.PipeDelimited);

        Assert.Equal("LD10042311", load.ShipmentId);
        Assert.Equal("TEST", load.Scac);
        Assert.Equal("FT", load.EquipmentCode);
        Assert.Equal("48", load.EquipmentLength);
        Assert.Equal(2, load.Stops.Count);
        Assert.Equal("GARY, IN", load.Origin!.Location.CityState);
        Assert.Equal("HOUSTON, TX", load.Destination!.Location.CityState);
        Assert.Equal(46800m, load.TotalWeight);
        Assert.Empty(load.TenderDiagnostics);
    }

    [Fact]
    public void Reports_envelope_defects_without_refusing_the_tender()
    {
        Load load = Single(Samples.BadSeCount);

        // The load is usable. The truck still has to be dispatched.
        Assert.Equal("LD10042407", load.ShipmentId);
        Assert.Equal(2, load.Stops.Count);

        // And the two defects are named, with the numbers that are wrong.
        Assert.Contains(load.TenderDiagnostics, d => d.Contains("X12-SE01-COUNT", StringComparison.Ordinal));
        Assert.Contains(load.TenderDiagnostics, d => d.Contains("X12-IEA02-CONTROL", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_sample_carries_the_test_usage_indicator()
    {
        // ISA15 is 'T'. A sample file that says 'P' is a sample file somebody will
        // eventually send into a production endpoint.
        foreach (string name in new[] { Samples.DryVan, Samples.Reefer, Samples.PipeDelimited, Samples.BadSeCount })
        {
            Assert.False(Single(name).IsProduction, name);
        }
    }

    private static Load Single(string sample) => Edi204Reader.Read(Samples.Read(sample)).Single();
}
