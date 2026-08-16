using System.Globalization;
using EdiX12.Core;
using FreightDispatch.Core.Model;

namespace FreightDispatch.Core.Edi;

/// <summary>
/// Generates a 997 Functional Acknowledgment for an interchange that has just been received.
/// </summary>
/// <remarks>
/// <para>The 997 is the first thing a trading partner expects back, and they expect it in
/// minutes, not overnight. It is also the smallest transaction set in common use — six
/// segments acknowledges a clean file — and the one most often either not sent at all or
/// sent with the counts made up.</para>
/// <para>What it does and does not say matters more than its size:</para>
/// <list type="bullet">
/// <item><description>It acknowledges <b>functional groups</b>, not interchanges and not
/// business content. One AK1…AK9 transaction set per inbound GS.</description></item>
/// <item><description>A clean 997 means the syntax survived. It does not mean the load was
/// accepted — that is a 990 — and treating one as the other is how a broker ends up
/// believing a truck is covered.</description></item>
/// <item><description>Interchange-level defects are out of its scope entirely. A missing
/// IEA or an IEA02 that does not echo ISA13 is a TA1's job. Those findings are recorded on
/// <see cref="FunctionalAcknowledgment.OutOfScope"/> rather than dropped, because a sender
/// whose IEA02 is wrong will otherwise get a clean 997 and conclude the file was
/// perfect.</description></item>
/// </list>
/// <code>
/// ST*997*0001~
/// AK1*SM*4417*005010~      the group being acknowledged: GS01, GS06, GS08
/// AK2*204*0001~            one per transaction set in that group: ST01, ST02
/// AK5*A~                   its verdict, plus up to five element 718 error codes
/// AK9*A*1*1*1~             the group's verdict, declared / received / accepted
/// SE*6*0001~
/// </code>
/// <para>AK3 and AK4 — segment and element notes — are not written. They report errors
/// against a specific segment position and element within a transaction set, which needs a
/// segment-level model of the 204 to find. This acknowledges what the envelope validator
/// can actually prove, and stays silent about what it cannot, which is the difference
/// between a useful acknowledgment and a guess.</para>
/// </remarks>
public sealed class Edi997Writer
{
    /// <summary>
    /// The transaction sets this board can act on. Anything else in an inbound group is
    /// acknowledged with element 718 code 1, <c>Transaction set not supported</c>, which is
    /// a rejection and is the honest answer — silently discarding a document the partner
    /// believes was delivered is worse than telling them.
    /// </summary>
    public static readonly IReadOnlyCollection<string> SupportedTransactionSets = new[] { "204" };

    /// <summary>
    /// The functional groups this board can act on. <c>SM</c> is the motor carrier load
    /// tender group that carries the 204.
    /// </summary>
    public static readonly IReadOnlyCollection<string> SupportedFunctionalGroups = new[] { "SM" };

    private readonly ControlNumbers _controlNumbers;
    private readonly X12Delimiters _delimiters;

    /// <summary>Creates a writer.</summary>
    /// <param name="controlNumbers">The ISA13/GS06/ST02 sequence to draw from.</param>
    /// <param name="delimiters">Delimiters for the outbound file. Defaults to <c>* : ~ ^</c>.</param>
    public Edi997Writer(ControlNumbers controlNumbers, X12Delimiters? delimiters = null)
    {
        _controlNumbers = controlNumbers ?? throw new ArgumentNullException(nameof(controlNumbers));
        _delimiters = delimiters ?? X12Delimiters.Default;
    }

    /// <summary>
    /// Acknowledges an interchange.
    /// </summary>
    /// <param name="inbound">The interchange as parsed, defects and all.</param>
    /// <param name="generatedAt">
    /// ISA09/ISA10 and GS04/GS05. In service this is within minutes of receipt, which is
    /// the whole expectation the 997 exists to meet.
    /// </param>
    /// <returns>
    /// The acknowledgment. <see cref="FunctionalAcknowledgment.Edi"/> is empty when the
    /// interchange carried no functional groups at all — there is nothing for a 997 to
    /// acknowledge and the correct response is a TA1, which is recorded on
    /// <see cref="FunctionalAcknowledgment.OutOfScope"/>.
    /// </returns>
    public FunctionalAcknowledgment Write(Interchange inbound, DateTime generatedAt)
    {
        if (inbound is null)
        {
            throw new ArgumentNullException(nameof(inbound));
        }

        List<AcknowledgedGroup> groups = inbound.Groups.Select(Judge).ToList();
        IReadOnlyList<string> outOfScope = InterchangeFindings(inbound);

        if (groups.Count == 0)
        {
            return new FunctionalAcknowledgment
            {
                AcknowledgedInterchangeControlNumber = inbound.ControlNumber,
                SentBy = inbound.ReceiverId,
                SentTo = inbound.SenderId,
                GeneratedAt = generatedAt,
                Groups = groups,
                OutOfScope = outOfScope
                    .Append(
                        "The interchange contains no functional groups, so there is nothing a 997 " +
                        "can acknowledge. The response to this is a TA1 interchange acknowledgment.")
                    .ToList(),
            };
        }

        string interchangeControl = _controlNumbers.NextInterchange();
        string groupControl = _controlNumbers.NextGroup();

        var writer = new X12Writer(_delimiters);

        // The acknowledgment goes back the way the file came, so sender and receiver swap.
        writer.BeginInterchange(
            senderQualifier: "ZZ",
            senderId: inbound.ReceiverId,
            receiverQualifier: "ZZ",
            receiverId: inbound.SenderId,
            timestamp: generatedAt,
            controlNumber: interchangeControl,
            production: inbound.IsProduction);

        // GS01 FA is the functional identifier for the 997 itself. It is not the identifier
        // of the group being acknowledged — that goes in AK101, and putting the acknowledged
        // group's code in GS01 is a file the partner routes to the wrong application.
        writer.BeginGroup("FA", inbound.ReceiverId, inbound.SenderId, generatedAt, groupControl);

        string firstTransactionControl = string.Empty;

        foreach (AcknowledgedGroup group in groups)
        {
            string transactionControl = _controlNumbers.NextTransaction();
            if (firstTransactionControl.Length == 0)
            {
                firstTransactionControl = transactionControl;
            }

            writer.BeginTransaction("997", transactionControl);

            writer.Segment(
                "AK1",
                group.FunctionalIdentifierCode,
                group.GroupControlNumber,
                group.VersionReleaseIndustryCode);

            foreach (AcknowledgedTransactionSet transaction in group.TransactionSets)
            {
                writer.Segment("AK2", transaction.IdentifierCode, transaction.ControlNumber);

                // AK502 through AK506 hold up to five element 718 codes. Five is the limit,
                // and a transaction set with more than five distinct syntax errors is a
                // transaction set nobody is going to fix from an acknowledgment anyway.
                string?[] ak5 = new string?[6];
                ak5[0] = transaction.AcknowledgmentCode;
                for (int i = 0; i < transaction.ErrorCodes.Count && i < 5; i++)
                {
                    ak5[i + 1] = transaction.ErrorCodes[i];
                }

                writer.Segment("AK5", ak5);
            }

            string?[] ak9 = new string?[9];
            ak9[0] = group.AcknowledgmentCode;
            ak9[1] = group.TransactionSetsDeclared.ToString(CultureInfo.InvariantCulture);
            ak9[2] = group.TransactionSetsReceived.ToString(CultureInfo.InvariantCulture);
            ak9[3] = group.TransactionSetsAccepted.ToString(CultureInfo.InvariantCulture);
            for (int i = 0; i < group.ErrorCodes.Count && i < 5; i++)
            {
                ak9[i + 4] = group.ErrorCodes[i];
            }

            writer.Segment("AK9", ak9);
            writer.EndTransaction();
        }

        writer.EndGroup();
        writer.EndInterchange();

        string edi = writer.ToString();

        // Parse what was just written straight back through the same library that read the
        // file being acknowledged. An acknowledgment the partner cannot parse is worse than
        // no acknowledgment, because it looks like an answer.
        IReadOnlyList<string> diagnostics;
        try
        {
            diagnostics = X12Parser.Parse(edi).Validate().Select(d => d.ToString()).ToList();
        }
        catch (X12ParseException ex)
        {
            diagnostics = new[] { $"X12-GENERATED-UNPARSEABLE: {ex.Message}" };
        }

        return new FunctionalAcknowledgment
        {
            Edi = edi,
            InterchangeControlNumber = interchangeControl,
            TransactionControlNumber = firstTransactionControl,
            AcknowledgedInterchangeControlNumber = inbound.ControlNumber,
            SentBy = inbound.ReceiverId,
            SentTo = inbound.SenderId,
            GeneratedAt = generatedAt,
            Groups = groups,
            OutOfScope = outOfScope,
            RoundTripDiagnostics = diagnostics,
        };
    }

    /// <summary>
    /// Judges one functional group: every transaction set in it, then the group envelope.
    /// </summary>
    private static AcknowledgedGroup Judge(FunctionalGroup group)
    {
        var groupErrors = new List<string>();

        int received = group.Transactions.Count;
        int declared = ReadCount(group.Trailer?[1]) ?? received;

        if (group.Trailer is null)
        {
            groupErrors.Add("3");   // Functional group trailer missing
        }
        else
        {
            if (group.Trailer[2].Trim() != group.ControlNumber)
            {
                groupErrors.Add("4");   // GS06 and GE02 do not agree
            }

            if (declared != received)
            {
                groupErrors.Add("5");   // Declared transaction set count is wrong
            }
        }

        if (!IsPositiveInteger(group.ControlNumber))
        {
            groupErrors.Add("6");   // Group control number violates syntax
        }

        // An unsupported functional group is rejected whole. Acknowledging its individual
        // transaction sets would be pretending to have read documents that were routed to
        // an application that does not exist here.
        if (!SupportedFunctionalGroups.Contains(group.FunctionalIdentifierCode, StringComparer.Ordinal))
        {
            return new AcknowledgedGroup
            {
                FunctionalIdentifierCode = group.FunctionalIdentifierCode,
                GroupControlNumber = group.ControlNumber,
                VersionReleaseIndustryCode = group.VersionReleaseIndustryCode,
                AcknowledgmentCode = "R",
                TransactionSetsDeclared = declared,
                TransactionSetsReceived = received,
                TransactionSetsAccepted = 0,
                ErrorCodes = new[] { "1" }.Concat(groupErrors).Take(5).ToList(),
                TransactionSets = Array.Empty<AcknowledgedTransactionSet>(),
            };
        }

        var seenControlNumbers = new HashSet<string>(StringComparer.Ordinal);
        var acknowledged = new List<AcknowledgedTransactionSet>();

        foreach (TransactionSet transaction in group.Transactions)
        {
            acknowledged.Add(Judge(transaction, seenControlNumbers));
        }

        int accepted = acknowledged.Count(t => t.AcknowledgmentCode is "A" or "E");
        int rejected = acknowledged.Count - accepted;

        string verdict =
            acknowledged.Count > 0 && accepted == 0 ? "R"
            : rejected > 0 ? "P"
            : groupErrors.Count > 0 ? "E"
            : "A";

        return new AcknowledgedGroup
        {
            FunctionalIdentifierCode = group.FunctionalIdentifierCode,
            GroupControlNumber = group.ControlNumber,
            VersionReleaseIndustryCode = group.VersionReleaseIndustryCode,
            AcknowledgmentCode = verdict,
            TransactionSetsDeclared = declared,
            TransactionSetsReceived = received,
            TransactionSetsAccepted = accepted,
            ErrorCodes = groupErrors.Take(5).ToList(),
            TransactionSets = acknowledged,
        };
    }

    /// <summary>
    /// Judges one transaction set against the three envelope rules a receiver checks before
    /// it looks at the business data, plus whether the document is one this board handles.
    /// </summary>
    /// <remarks>
    /// The verdict here is <c>A</c> or <c>R</c> and never <c>E</c>, and that is not an
    /// omission. Every element 718 code is a structural defect: a document whose declared
    /// segment count is wrong has not been read correctly and cannot be acted on. "Accepted
    /// but errors were noted" belongs to content problems reported through AK3/AK4 — a code
    /// value outside the agreed list, a date that is not a date — which an envelope
    /// validator has no way to find. <c>E</c> therefore appears here only at the group
    /// level, where a wrong GE01 does not stop the documents inside from being usable.
    /// </remarks>
    private static AcknowledgedTransactionSet Judge(TransactionSet transaction, HashSet<string> seenControlNumbers)
    {
        var errors = new List<string>();

        if (transaction.IdentifierCode.Length == 0)
        {
            errors.Add("6");    // Missing or invalid transaction set identifier
        }
        else if (!SupportedTransactionSets.Contains(transaction.IdentifierCode, StringComparer.Ordinal))
        {
            errors.Add("1");    // Transaction set not supported
        }

        if (transaction.Trailer is null)
        {
            errors.Add("2");    // Transaction set trailer missing
        }
        else
        {
            if (transaction.Trailer[2].Trim() != transaction.ControlNumber)
            {
                errors.Add("3");    // ST02 and SE02 do not match
            }

            if (ReadCount(transaction.Trailer[1]) != transaction.DeclaredSegmentCount)
            {
                errors.Add("4");    // SE01 does not match the actual segment count
            }
        }

        // A repeated ST02 inside one group is the case element 718 code 7 is really for:
        // the receiver cannot tell the two documents apart, and a partner resending a file
        // without advancing the counter is common enough to be worth catching here.
        if (transaction.ControlNumber.Length == 0 || !seenControlNumbers.Add(transaction.ControlNumber))
        {
            errors.Add("7");    // Missing or invalid transaction set control number
        }

        return new AcknowledgedTransactionSet
        {
            IdentifierCode = transaction.IdentifierCode,
            ControlNumber = transaction.ControlNumber,
            AcknowledgmentCode = errors.Count == 0 ? "A" : "R",
            ErrorCodes = errors.Take(5).ToList(),
        };
    }

    /// <summary>
    /// Interchange-level findings, which a 997 cannot report and a TA1 can.
    /// </summary>
    private static IReadOnlyList<string> InterchangeFindings(Interchange inbound)
    {
        var findings = new List<string>();

        if (inbound.Trailer is null)
        {
            findings.Add(
                "The interchange has no IEA trailer. A 997 acknowledges functional groups and " +
                "cannot report this; the message for it is a TA1.");
            return findings;
        }

        if (inbound.Trailer[2].Trim() != inbound.ControlNumber)
        {
            findings.Add(
                $"IEA02 is '{inbound.Trailer[2].Trim()}' but ISA13 is '{inbound.ControlNumber}'. " +
                "A 997 acknowledges functional groups and cannot report this; the message for it is a TA1.");
        }

        string declaredGroups = inbound.Trailer[1].Trim();
        if (declaredGroups != inbound.Groups.Count.ToString(CultureInfo.InvariantCulture))
        {
            findings.Add(
                $"IEA01 declares '{declaredGroups}' functional groups and the interchange contains " +
                $"{inbound.Groups.Count}. This is a TA1 finding, not a 997 one.");
        }

        return findings;
    }

    private static int? ReadCount(string? value) =>
        int.TryParse((value ?? string.Empty).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : null;

    private static bool IsPositiveInteger(string value) =>
        value.Length > 0 &&
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) &&
        parsed > 0;
}
