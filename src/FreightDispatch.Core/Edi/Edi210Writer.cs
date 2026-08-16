using System.Globalization;
using EdiX12.Core;
using FreightDispatch.Core.Model;

namespace FreightDispatch.Core.Edi;

/// <summary>
/// Generates a 210 Motor Carrier Freight Details and Invoice from a delivered load.
/// </summary>
/// <remarks>
/// <para>The 214 tells the broker where the truck is. The 210 asks to be paid, and it is
/// the only document in this loop that ends up in somebody's accounts payable system. It is
/// generated on delivery because that is when the carrier is entitled to bill — before the
/// D1 there is nothing to invoice, and a 210 that arrives before the freight does is a 210
/// that gets held.</para>
/// <para>Three things about the 210 catch people out, and all three are handled here:</para>
/// <list type="number">
/// <item><description><b>The money elements are N2.</b> B307 Net Amount Due, L104 Amount
/// Charged and L305 Amount Charged all carry an implied decimal point and no explicit one.
/// $2,653.75 is written <c>265375</c>. The rate elements beside them — L102 Freight Rate,
/// type R — do take a decimal point, so one segment can legitimately contain <c>2.5</c> and
/// <c>265375</c> and both are right.</description></item>
/// <item><description><b>The reference segment is N9, not L11.</b> Both carry a reference
/// number and a qualifier and they carry them in opposite order: N901 is the qualifier and
/// N902 the value, while L1101 is the value and L1102 the qualifier. A mapper that copies
/// the 214's L11 handling into the 210 produces <c>N9*BOL8842190*BM</c>, which is a
/// reference of type "BOL8842190" and fails on a code list the error message will not
/// name.</description></item>
/// <item><description><b>The charges have to come from somewhere the tender is not.</b> See
/// <see cref="InvoiceRates"/> — a 204 carries no rate at all.</description></item>
/// </list>
/// <code>
/// ST*210*4008~
/// B3**INV-LD10041872*LD10041872*PP*L*20260820*323758****DEMO~
/// C3*USD~
/// ITD*01*3*****30~
/// N9*BM*BOL8842190~        qualifier first — the opposite of L11
/// G62*11*20260818~         shipped on this date
/// G62*35*20260819~         delivered on this date
/// N1*BT … N1*SH … N1*CN~   who pays, who shipped, who received
/// LX*1~                    the freight itself
/// L5*1*CANNED VEGETABLES PALLETIZED~
/// L0*1***42150*G***24*PLT**L~
/// L1*1*2.5*CW*265375****LHS****LINEHAUL~
/// LX*2~                    an accessorial: no L5, no L0, just the charge
/// L1*2***58383****405****FUEL SURCHARGE 22 PERCENT OF LINEHAUL~
/// L3*42150*G***323758******24*L~
/// SE*23*4008~
/// </code>
/// </remarks>
public sealed class Edi210Writer
{
    private readonly ControlNumbers _controlNumbers;
    private readonly X12Delimiters _delimiters;
    private readonly InvoiceRates _rates;

    /// <summary>Creates a writer.</summary>
    /// <param name="controlNumbers">The ISA13/GS06/ST02 sequence to draw from.</param>
    /// <param name="rates">The rate card to price against. Defaults to <see cref="InvoiceRates.Demo"/>.</param>
    /// <param name="delimiters">Delimiters for the outbound file. Defaults to <c>* : ~ ^</c>.</param>
    public Edi210Writer(ControlNumbers controlNumbers, InvoiceRates? rates = null, X12Delimiters? delimiters = null)
    {
        _controlNumbers = controlNumbers ?? throw new ArgumentNullException(nameof(controlNumbers));
        _rates = rates ?? InvoiceRates.Demo;
        _delimiters = delimiters ?? X12Delimiters.Default;
    }

    /// <summary>
    /// Prices a load and writes the interchange that invoices it.
    /// </summary>
    /// <param name="load">The delivered load.</param>
    /// <param name="generatedAt">ISA09/ISA10, GS04/GS05, and B306 the invoice date.</param>
    /// <returns>The invoice, its charge lines and the generated interchange.</returns>
    public FreightInvoice Write(Load load, DateTime generatedAt)
    {
        if (load is null)
        {
            throw new ArgumentNullException(nameof(load));
        }

        IReadOnlyList<InvoiceCharge> charges = Price(load);
        long total = charges.Sum(c => c.AmountCents);

        DateTime? shippedOn = FirstEventDate(load, "AF");
        DateTime? deliveredOn = LastEventDate(load, "D1");

        string interchangeControl = _controlNumbers.NextInterchange();
        string groupControl = _controlNumbers.NextGroup();
        string transactionControl = _controlNumbers.NextTransaction();

        // A real carrier's invoice number comes out of its own billing sequence and has
        // nothing to do with the load number. Deriving it here keeps the demo reproducible;
        // it would collide the first time a load was re-invoiced after a correction, which
        // is what B308 Correction Indicator exists for.
        string invoiceNumber = $"INV-{load.ShipmentId}";

        var writer = new X12Writer(_delimiters);

        // Like the 214, the invoice travels back the way the tender came.
        writer.BeginInterchange(
            senderQualifier: "ZZ",
            senderId: load.TenderedTo,
            receiverQualifier: "ZZ",
            receiverId: load.TenderedBy,
            timestamp: generatedAt,
            controlNumber: interchangeControl,
            production: load.IsProduction);

        // GS01 IM is the functional identifier for the motor carrier invoice group.
        writer.BeginGroup("IM", load.TenderedTo, load.TenderedBy, generatedAt, groupControl);
        writer.BeginTransaction("210", transactionControl);

        // B304 Shipment Method of Payment is mandatory. A tender that omitted B206 gets PP
        // rather than an empty mandatory element, because an empty mandatory element is a
        // rejection and prepaid is what a brokered load is in practice.
        string paymentMethod = string.IsNullOrWhiteSpace(load.PaymentMethod) ? "PP" : load.PaymentMethod;

        writer.Segment(
            "B3",
            null,                                       // B301 shipment qualifier
            invoiceNumber,                              // B302 invoice number
            load.ShipmentId,                            // B303 shipment identification number
            paymentMethod,                              // B304 shipment method of payment
            "L",                                        // B305 weight unit code — pounds
            X12Values.WriteDate(generatedAt),           // B306 invoice date
            total.ToString(CultureInfo.InvariantCulture), // B307 net amount due, N2: cents
            null,                                       // B308 correction indicator
            null,                                       // B309 delivery date — see below
            null,                                       // B310 date/time qualifier
            load.Scac);                                 // B311 SCAC

        // B309 and B310 are a conditional pair: the date is meaningless without the
        // qualifier that says what kind of date it is. Rather than send one and guess at the
        // other, the ship and delivery dates go in G62 segments below, where element 432
        // states outright which is which.
        writer.Segment("C3", _rates.CurrencyCode);

        // ITD01 01 basic terms, ITD02 3 terms start from the invoice date, ITD07 net days.
        writer.Segment("ITD", "01", "3", null, null, null, null,
            _rates.PaymentTermsDays.ToString(CultureInfo.InvariantCulture));

        // N901 is the qualifier and N902 the value. L11 has them the other way round.
        foreach (ReferenceNumber reference in SelectReferences(load))
        {
            writer.Segment("N9", reference.Qualifier, reference.Value);
        }

        if (shippedOn is { } shipped)
        {
            writer.Segment("G62", "11", X12Values.WriteDate(shipped));
        }

        if (deliveredOn is { } delivered)
        {
            writer.Segment("G62", "35", X12Values.WriteDate(delivered));
        }

        WriteParty(writer, "BT", load.BillTo ?? BillToFallback(load));
        WriteParty(writer, "SH", load.Origin?.Location);
        WriteParty(writer, "CN", load.Destination?.Location);

        if (!string.IsNullOrWhiteSpace(load.TrailerNumber))
        {
            // N701 equipment initial, N702 equipment number, N711 equipment description code.
            writer.Segment("N7", load.Scac, load.TrailerNumber, null, null, null, null, null,
                null, null, null, load.EquipmentCode);
        }

        foreach (InvoiceCharge charge in charges)
        {
            string line = charge.LineNumber.ToString(CultureInfo.InvariantCulture);

            writer.Segment("LX", line);

            if (!string.IsNullOrWhiteSpace(charge.Commodity))
            {
                writer.Segment("L5", line, charge.Commodity);
            }

            if (charge.HasFreight)
            {
                writer.Segment(
                    "L0",
                    line,                                       // L001 lading line item number
                    null,                                       // L002 billed/rated-as quantity
                    null,                                       // L003 billed/rated-as qualifier
                    Number(charge.Weight),                      // L004 weight
                    "G",                                        // L005 weight qualifier — gross
                    null,                                       // L006 volume
                    null,                                       // L007 volume unit qualifier
                    Number(charge.Quantity),                    // L008 lading quantity
                    charge.PackagingCode,                       // L009 packaging form code
                    null,                                       // L010 dunnage description
                    "L");                                       // L011 weight unit code — pounds
            }

            writer.Segment(
                "L1",
                line,                                                   // L101 lading line item number
                Number(charge.Rate),                                    // L102 freight rate, type R
                charge.RateQualifier,                                   // L103 rate/value qualifier
                charge.AmountCents.ToString(CultureInfo.InvariantCulture), // L104 amount charged, N2
                null,                                                   // L105 advances
                null,                                                   // L106 prepaid amount
                null,                                                   // L107 rate combination point
                charge.SpecialChargeCode,                               // L108 special charge code
                null,                                                   // L109 rate class code
                null,                                                   // L110 entitlement code
                null,                                                   // L111 charge method of payment
                charge.Description);                                    // L112 special charge description
        }

        writer.Segment(
            "L3",
            Number(load.TotalWeight),                       // L301 weight
            "G",                                            // L302 weight qualifier
            null,                                           // L303 freight rate
            null,                                           // L304 rate/value qualifier
            total.ToString(CultureInfo.InvariantCulture),   // L305 amount charged, N2
            null,                                           // L306 advances
            null,                                           // L307 prepaid amount
            null,                                           // L308 special charge code
            null,                                           // L309 volume
            null,                                           // L310 volume unit qualifier
            Number(TotalQuantity(load)),                    // L311 lading quantity
            "L");                                           // L312 weight unit code

        writer.EndTransaction();
        writer.EndGroup();
        writer.EndInterchange();

        string edi = writer.ToString();

        IReadOnlyList<string> diagnostics;
        try
        {
            diagnostics = X12Parser.Parse(edi).Validate().Select(d => d.ToString()).ToList();
        }
        catch (X12ParseException ex)
        {
            diagnostics = new[] { $"X12-GENERATED-UNPARSEABLE: {ex.Message}" };
        }

        return new FreightInvoice
        {
            InvoiceNumber = invoiceNumber,
            InvoiceDate = generatedAt.Date,
            ShippedOn = shippedOn,
            DeliveredOn = deliveredOn,
            Charges = charges,
            TotalWeight = load.TotalWeight,
            TotalQuantity = TotalQuantity(load),
            CurrencyCode = _rates.CurrencyCode,
            PaymentTermsDays = _rates.PaymentTermsDays,
            Edi = edi,
            InterchangeControlNumber = interchangeControl,
            TransactionControlNumber = transactionControl,
            GeneratedAt = generatedAt,
            RoundTripDiagnostics = diagnostics,
        };
    }

    /// <summary>
    /// Turns a load into charge lines.
    /// </summary>
    /// <remarks>
    /// Linehaul first, then one stop-off line for every stop past the first two, then fuel
    /// on top of the linehaul. The stop-off lines are read straight off the S5 loops, which
    /// is the point: the board's stop count and the invoice's stop-off count come from the
    /// same place and cannot drift apart.
    /// </remarks>
    private IReadOnlyList<InvoiceCharge> Price(Load load)
    {
        var charges = new List<InvoiceCharge>();
        int line = 1;

        long linehaul = _rates.Linehaul(load.TotalWeight);

        charges.Add(new InvoiceCharge(
            LineNumber: line++,
            Description: "LINEHAUL",
            SpecialChargeCode: "LHS",
            AmountCents: linehaul,
            Rate: _rates.LinehaulPerHundredweightCents / 100m,
            RateQualifier: "CW",
            Weight: load.TotalWeight,
            Quantity: TotalQuantity(load),
            PackagingCode: "PLT",
            Commodity: load.Origin?.Commodities.FirstOrDefault() ?? string.Empty));

        // The first pickup and the last delivery are the move. Every stop between them is a
        // door the driver had to open again.
        foreach (Stop stop in load.Stops.Skip(1).Take(Math.Max(0, load.Stops.Count - 2)))
        {
            charges.Add(new InvoiceCharge(
                LineNumber: line++,
                Description: $"STOP OFF - {Describe(stop)}",
                SpecialChargeCode: "SOC",
                AmountCents: _rates.StopOffCents,
                Weight: stop.Weight,
                Quantity: stop.Units,
                PackagingCode: stop.Units.HasValue ? "PLT" : string.Empty,
                Commodity: stop.Commodities.FirstOrDefault() ?? string.Empty));
        }

        long fuel = _rates.FuelSurcharge(linehaul);
        if (fuel > 0)
        {
            int percent = (int)Math.Round(_rates.FuelSurchargeRate * 100m, 0, MidpointRounding.AwayFromZero);

            charges.Add(new InvoiceCharge(
                LineNumber: line,
                Description: $"FUEL SURCHARGE {percent} PERCENT OF LINEHAUL",
                SpecialChargeCode: "405",
                AmountCents: fuel));
        }

        return charges;
    }

    /// <summary>
    /// The references worth putting on an invoice: the bill of lading and the order number
    /// are what an accounts payable clerk matches the invoice against.
    /// </summary>
    private static IEnumerable<ReferenceNumber> SelectReferences(Load load)
    {
        string[] wanted = { "BM", "OQ", "CO", "PO" };

        return load.References
            .Where(r => wanted.Contains(r.Qualifier) && !string.IsNullOrWhiteSpace(r.Value))
            .GroupBy(r => r.Qualifier)
            .Select(g => g.First());
    }

    /// <summary>
    /// Who to bill when the tender carried no BT party loop, which is most of the time.
    /// </summary>
    /// <remarks>
    /// The invoice goes to whoever tendered the load. That is not a guess — the interchange
    /// they sent it in names them, and it is the same identifier the 214s have been going
    /// back to all along.
    /// </remarks>
    private static Party? BillToFallback(Load load) =>
        string.IsNullOrWhiteSpace(load.TenderedBy)
            ? null
            : new Party
            {
                EntityIdentifierCode = "BT",
                Name = load.TenderedBy,
                IdQualifier = "ZZ",
                IdCode = load.TenderedBy,
            };

    private static void WriteParty(X12Writer writer, string entityCode, Party? party)
    {
        if (party is null || string.IsNullOrWhiteSpace(party.Name))
        {
            return;
        }

        writer.Segment("N1", entityCode, party.Name, party.IdQualifier, party.IdCode);

        if (!string.IsNullOrWhiteSpace(party.Address1))
        {
            writer.Segment("N3", party.Address1, party.Address2);
        }

        if (!string.IsNullOrWhiteSpace(party.City))
        {
            writer.Segment("N4", party.City, party.State, party.PostalCode, party.Country);
        }
    }

    /// <summary>Total handling units across the pickups, which is what the trailer was loaded with.</summary>
    private static decimal? TotalQuantity(Load load)
    {
        decimal[] units = load.Stops
            .Where(s => s.IsPickup && s.Units.HasValue)
            .Select(s => s.Units!.Value)
            .ToArray();

        return units.Length == 0 ? null : units.Sum();
    }

    private static string Describe(Stop stop) =>
        string.IsNullOrWhiteSpace(stop.Location.CityState)
            ? $"STOP {stop.Sequence}"
            : stop.Location.CityState.Replace(",", string.Empty);

    private static string? Number(decimal? value) =>
        value is { } number ? X12Values.WriteDecimal(number) : null;

    /// <summary>The date of the first event carrying a given element 1650 code.</summary>
    private static DateTime? FirstEventDate(Load load, string statusCode) =>
        load.Events.FirstOrDefault(e => e.StatusCode == statusCode)?.OccurredAt;

    /// <summary>The date of the last event carrying a given element 1650 code.</summary>
    private static DateTime? LastEventDate(Load load, string statusCode) =>
        load.Events.LastOrDefault(e => e.StatusCode == statusCode)?.OccurredAt;
}
