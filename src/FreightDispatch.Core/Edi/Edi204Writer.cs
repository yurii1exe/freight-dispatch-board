using EdiX12.Core;
using FreightDispatch.Core.Model;

namespace FreightDispatch.Core.Edi;

/// <summary>
/// Writes a <see cref="Load"/> back out as a 204 Motor Carrier Load Tender.
/// </summary>
/// <remarks>
/// <para>The board receives 204s; it does not normally send them. This exists for two
/// reasons, both of which are worth more than the code costs.</para>
/// <para>The first is testing. A reader you cannot round-trip is a reader you are taking
/// on faith: parse a tender, write it back, parse it again, and every field either survives
/// or you have found a gap. That test catches an element read from the wrong position far
/// more reliably than eyeballing a fixture.</para>
/// <para>The second is that a broker tendering to a carrier is the same message in the
/// other direction, so anything built on this board that wants to <em>offer</em> a load
/// rather than receive one already has the writer.</para>
/// </remarks>
public sealed class Edi204Writer
{
    private readonly ControlNumbers _controlNumbers;
    private readonly X12Delimiters _delimiters;

    /// <summary>Creates a writer.</summary>
    /// <param name="controlNumbers">The ISA13/GS06/ST02 sequence to draw from.</param>
    /// <param name="delimiters">Delimiters for the outbound file. Defaults to <c>* : ~ ^</c>.</param>
    public Edi204Writer(ControlNumbers controlNumbers, X12Delimiters? delimiters = null)
    {
        _controlNumbers = controlNumbers ?? throw new ArgumentNullException(nameof(controlNumbers));
        _delimiters = delimiters ?? X12Delimiters.Default;
    }

    /// <summary>
    /// Writes one interchange containing one 204.
    /// </summary>
    /// <param name="load">The load to tender.</param>
    /// <param name="generatedAt">ISA09/ISA10 and GS04/GS05.</param>
    /// <returns>The complete interchange text.</returns>
    public string Write(Load load, DateTime generatedAt)
    {
        if (load is null)
        {
            throw new ArgumentNullException(nameof(load));
        }

        var writer = new X12Writer(_delimiters);

        writer.BeginInterchange(
            senderQualifier: "ZZ",
            senderId: load.TenderedBy,
            receiverQualifier: "ZZ",
            receiverId: load.TenderedTo,
            timestamp: generatedAt,
            controlNumber: _controlNumbers.NextInterchange(),
            production: load.IsProduction);

        // GS01 SM is the functional identifier for motor carrier load tender.
        writer.BeginGroup("SM", load.TenderedBy, load.TenderedTo, generatedAt, _controlNumbers.NextGroup());
        writer.BeginTransaction("204", _controlNumbers.NextTransaction());

        // B201 tariff service and B203 SPLC are left empty; B205 is the weight unit, which
        // the stops carry instead.
        writer.Segment("B2", null, load.Scac, null, load.ShipmentId, null, load.PaymentMethod);

        // B2A02 LT is the application type for a tender.
        writer.Segment("B2A", load.PurposeCode, "LT");

        foreach (ReferenceNumber reference in load.References)
        {
            writer.Segment("L11", reference.Value, reference.Qualifier);
        }

        if (HasEquipment(load))
        {
            // N702 equipment number, N711 description code, N713 temperature, N715 length.
            writer.Segment(
                "N7",
                null, load.TrailerNumber, null, null, null, null, null, null, null, null,
                load.EquipmentCode,
                null,
                load.TemperatureControl,
                null,
                load.EquipmentLength);
        }

        foreach (string note in load.Notes)
        {
            writer.Segment("NTE", "OTH", note);
        }

        if (load.BillTo is not null)
        {
            WriteParty(writer, load.BillTo);
        }

        foreach (Stop stop in load.Stops)
        {
            WriteStop(writer, stop);
        }

        if (load.TotalWeight.HasValue)
        {
            writer.Segment("L3", X12Values.WriteDecimal(load.TotalWeight.Value), load.WeightQualifier);
        }

        writer.EndTransaction();
        writer.EndGroup();
        writer.EndInterchange();

        return writer.ToString();
    }

    private static bool HasEquipment(Load load) =>
        !string.IsNullOrEmpty(load.EquipmentCode) ||
        !string.IsNullOrEmpty(load.EquipmentLength) ||
        !string.IsNullOrEmpty(load.TrailerNumber) ||
        !string.IsNullOrEmpty(load.TemperatureControl);

    /// <summary>Writes one S5 stop-off loop.</summary>
    private static void WriteStop(X12Writer writer, Stop stop)
    {
        writer.Segment(
            "S5",
            stop.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
            stop.ReasonCode,
            stop.Weight.HasValue ? X12Values.WriteDecimal(stop.Weight.Value) : null,
            stop.WeightUnit,
            stop.Units.HasValue ? X12Values.WriteDecimal(stop.Units.Value) : null,
            stop.UnitOfMeasure);

        foreach (ReferenceNumber reference in stop.References)
        {
            writer.Segment("L11", reference.Value, reference.Qualifier);
        }

        WriteWindow(writer, stop);

        WriteParty(writer, stop.Location);

        int line = 1;
        foreach (string commodity in stop.Commodities)
        {
            writer.Segment("L5", line.ToString(System.Globalization.CultureInfo.InvariantCulture), commodity);
            line++;
        }

        if (stop.Weight.HasValue)
        {
            writer.Segment(
                "AT8",
                "G",
                string.IsNullOrEmpty(stop.WeightUnit) ? "L" : stop.WeightUnit,
                X12Values.WriteDecimal(stop.Weight.Value),
                stop.Units.HasValue ? X12Values.WriteDecimal(stop.Units.Value) : null);
        }

        foreach (string note in stop.Notes)
        {
            writer.Segment("NTE", "OTH", note);
        }
    }

    /// <summary>
    /// Writes the stop's window as the pair of G62 segments the qualifiers imply.
    /// </summary>
    /// <remarks>
    /// A pickup opens with <c>37</c> Ship Not Before and closes with <c>38</c> Ship Not
    /// Later Than; a delivery opens with <c>53</c> Deliver Not Before and closes with
    /// <c>54</c> Deliver No Later Than. The time qualifiers in G6203 pair with them —
    /// <c>I</c>/<c>K</c> earliest and latest requested pickup, <c>G</c>/<c>L</c> earliest
    /// and latest requested delivery.
    /// </remarks>
    private static void WriteWindow(X12Writer writer, Stop stop)
    {
        string timeCode = string.IsNullOrEmpty(stop.Window.TimeCode) ? "LT" : stop.Window.TimeCode;
        (string open, string close) = stop.IsPickup ? ("37", "38") : ("53", "54");
        (string openTime, string closeTime) = stop.IsPickup ? ("I", "K") : ("G", "L");

        if (stop.Window.Earliest is { } earliest)
        {
            writer.Segment(
                "G62", open, X12Values.WriteDate(earliest), openTime, X12Values.WriteTime(earliest), timeCode);
        }

        if (stop.Window.Latest is { } latest)
        {
            writer.Segment(
                "G62", close, X12Values.WriteDate(latest), closeTime, X12Values.WriteTime(latest), timeCode);
        }
    }

    /// <summary>Writes an N1/N3/N4/G61 party loop.</summary>
    private static void WriteParty(X12Writer writer, Party party)
    {
        if (string.IsNullOrWhiteSpace(party.Name))
        {
            return;
        }

        writer.Segment("N1", party.EntityIdentifierCode, party.Name, party.IdQualifier, party.IdCode);

        if (!string.IsNullOrWhiteSpace(party.Address1))
        {
            writer.Segment("N3", party.Address1, party.Address2);
        }

        if (!string.IsNullOrWhiteSpace(party.City))
        {
            writer.Segment("N4", party.City, party.State, party.PostalCode, party.Country);
        }

        if (!string.IsNullOrWhiteSpace(party.ContactName))
        {
            // G6101 IC is the information contact — the person the driver calls.
            writer.Segment("G61", "IC", party.ContactName, "TE", party.ContactPhone);
        }
    }
}
