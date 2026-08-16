using EdiX12.Core;
using FreightDispatch.Core.Model;

namespace FreightDispatch.Core.Edi;

/// <summary>
/// Generates a 214 Transportation Carrier Shipment Status Message from a load and one
/// status event.
/// </summary>
/// <remarks>
/// <para>One event, one 214. Batching several status events into one transaction set is
/// legal — the 1100 loop repeats up to ten times — but it makes the file harder to trace
/// back to what happened, and partners acknowledge interchanges rather than events.</para>
/// <para>Segment order follows the 5010 214 structure, which is not the order people
/// assume. The N1 party loop is in the <em>detail</em> area, inside the LX loop and after
/// the AT7/MS1/MS2 group, not in the heading next to B10:</para>
/// <code>
/// ST
/// B10                              shipment identification
///   LX      loop 1000
///   L11                            reference numbers
///     AT7   loop 1100              the status itself
///     MS1                          where the truck is
///     MS2                          which trailer
///     N1    loop 1200              shipper and consignee
///     N3
///     N4
/// SE
/// </code>
/// </remarks>
public sealed class Edi214Writer
{
    private readonly ControlNumbers _controlNumbers;
    private readonly X12Delimiters _delimiters;

    /// <summary>Creates a writer.</summary>
    /// <param name="controlNumbers">The ISA13/GS06/ST02 sequence to draw from.</param>
    /// <param name="delimiters">Delimiters for the outbound file. Defaults to <c>* : ~ ^</c>.</param>
    public Edi214Writer(ControlNumbers controlNumbers, X12Delimiters? delimiters = null)
    {
        _controlNumbers = controlNumbers ?? throw new ArgumentNullException(nameof(controlNumbers));
        _delimiters = delimiters ?? X12Delimiters.Default;
    }

    /// <summary>
    /// Writes a complete interchange containing one 214 for one status event.
    /// </summary>
    /// <param name="load">The load the status is about.</param>
    /// <param name="statusCode">AT701, the element 1650 Shipment Status Code.</param>
    /// <param name="reasonCode">AT702, the element 1651 reason. <c>NS</c> for a normal status.</param>
    /// <param name="occurredAt">AT705/AT706, when it happened, in local time at the location.</param>
    /// <param name="city">MS101, the city the truck was in. Falls back to the relevant stop.</param>
    /// <param name="state">MS102, the state or province.</param>
    /// <param name="country">MS103, the country code.</param>
    /// <param name="generatedAt">
    /// ISA09/ISA10 and GS04/GS05 — when the file was produced, which is not the same
    /// instant as when the event happened and is frequently hours later.
    /// </param>
    /// <returns>The interchange text and the control numbers it was issued.</returns>
    public Edi214Result Write(
        Load load,
        string statusCode,
        string reasonCode,
        DateTime occurredAt,
        string city,
        string state,
        string country,
        DateTime generatedAt)
    {
        if (load is null)
        {
            throw new ArgumentNullException(nameof(load));
        }

        string interchangeControl = _controlNumbers.NextInterchange();
        string groupControl = _controlNumbers.NextGroup();
        string transactionControl = _controlNumbers.NextTransaction();

        var writer = new X12Writer(_delimiters);

        // The 214 travels back the way the 204 came, so sender and receiver swap. Getting
        // this backwards produces a file the partner routes straight to their own inbound
        // queue and then reports as an unknown sender.
        writer.BeginInterchange(
            senderQualifier: "ZZ",
            senderId: load.TenderedTo,
            receiverQualifier: "ZZ",
            receiverId: load.TenderedBy,
            timestamp: generatedAt,
            controlNumber: interchangeControl,
            production: load.IsProduction);

        // GS01 QM is the functional identifier for transportation carrier shipment status.
        writer.BeginGroup("QM", load.TenderedTo, load.TenderedBy, generatedAt, groupControl);
        writer.BeginTransaction("214", transactionControl);

        // B1001 is the carrier's own reference for the trip; the PRO number if the tender
        // carried one, otherwise the shipment id again. B1002 is the tendering party's
        // shipment identification number, which is the value they will match on, and B1003
        // the SCAC of the carrier reporting the status.
        string carrierReference = FindReference(load, "CN") ?? load.ShipmentId;
        writer.Segment("B10", carrierReference, load.ShipmentId, load.Scac);

        writer.Segment("LX", "1");

        // Carry the tender's identifying references back so the partner can match the
        // status to their load without relying on B1002 alone. The bill of lading and the
        // order number are the two everybody keys on.
        foreach (ReferenceNumber reference in SelectReferences(load))
        {
            writer.Segment("L11", reference.Value, reference.Qualifier);
        }

        // AT703 and AT704 are appointment status and appointment reason; both are empty
        // here because this is a status report, not an appointment negotiation.
        writer.Segment(
            "AT7",
            statusCode,
            reasonCode,
            null,
            null,
            X12Values.WriteDate(occurredAt),
            X12Values.WriteTime(occurredAt),
            "LT");

        if (!string.IsNullOrWhiteSpace(city))
        {
            writer.Segment("MS1", city, state, country);
        }

        if (!string.IsNullOrWhiteSpace(load.TrailerNumber))
        {
            // MS201 is the SCAC of the equipment owner, MS202 the equipment number.
            writer.Segment("MS2", load.Scac, load.TrailerNumber);
        }

        WriteParty(writer, "SH", load.Origin?.Location);
        WriteParty(writer, "CN", load.Destination?.Location);

        writer.EndTransaction();
        writer.EndGroup();
        writer.EndInterchange();

        string edi = writer.ToString();

        // Parse what was just written straight back through the same library that reads
        // inbound files. A generator that has never been read by a parser is a generator
        // that works until the first partner tries it.
        IReadOnlyList<string> diagnostics;
        try
        {
            diagnostics = X12Parser.Parse(edi).Validate().Select(d => d.ToString()).ToList();
        }
        catch (X12ParseException ex)
        {
            diagnostics = new[] { $"X12-GENERATED-UNPARSEABLE: {ex.Message}" };
        }

        return new Edi214Result(edi, interchangeControl, groupControl, transactionControl, diagnostics);
    }

    /// <summary>
    /// Writes an N1/N3/N4 party loop, skipping it entirely when there is nothing to say.
    /// An N1 with an empty N102 is worse than no N1.
    /// </summary>
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

    /// <summary>
    /// The references worth echoing on a status message: the bill of lading and the order
    /// or load numbers. Sending all of them back is not free — some partners cap L11
    /// repetition and reject the excess.
    /// </summary>
    private static IEnumerable<ReferenceNumber> SelectReferences(Load load)
    {
        string[] wanted = { "BM", "OQ", "CO", "LO", "SI" };

        return load.References
            .Where(r => wanted.Contains(r.Qualifier) && !string.IsNullOrWhiteSpace(r.Value))
            .GroupBy(r => r.Qualifier)
            .Select(g => g.First());
    }

    /// <summary>Finds the first header reference with a given qualifier, or null.</summary>
    private static string? FindReference(Load load, string qualifier) =>
        load.References.FirstOrDefault(r => r.Qualifier == qualifier)?.Value;
}

/// <summary>
/// A generated 214 and everything that had to be recorded about generating it.
/// </summary>
/// <param name="Edi">The complete interchange text.</param>
/// <param name="InterchangeControlNumber">ISA13 that was issued.</param>
/// <param name="GroupControlNumber">GS06 that was issued.</param>
/// <param name="TransactionControlNumber">ST02 that was issued.</param>
/// <param name="Diagnostics">
/// What <c>EdiX12.Core</c> reported when the generated file was parsed back. Empty means
/// all nine envelope checks passed.
/// </param>
public sealed record Edi214Result(
    string Edi,
    string InterchangeControlNumber,
    string GroupControlNumber,
    string TransactionControlNumber,
    IReadOnlyList<string> Diagnostics);
