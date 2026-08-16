namespace FreightDispatch.Core.Model;

/// <summary>
/// A 997 Functional Acknowledgment that was generated for one inbound interchange, plus
/// everything a human needs to read it without a code list open.
/// </summary>
/// <remarks>
/// <para>The 997 is the first thing a trading partner expects back, usually within minutes
/// of sending. It says one thing: <em>I received your functional group and here is whether
/// it survived syntax checking.</em> It says nothing about whether the load was accepted —
/// that is a 990, and confusing the two is how a broker ends up believing a truck is
/// covered because the acknowledgment came back clean.</para>
/// <para>The generated text is kept on the record rather than regenerated on demand, so the
/// control numbers stay stable and what the board shows is byte for byte what the partner
/// received.</para>
/// </remarks>
public sealed class FunctionalAcknowledgment
{
    /// <summary>Board identifier, used in the API route for the raw 997.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>The complete generated interchange.</summary>
    public string Edi { get; init; } = string.Empty;

    /// <summary>ISA13 of the generated interchange.</summary>
    public string InterchangeControlNumber { get; init; } = string.Empty;

    /// <summary>ST02 of the first generated 997 transaction set.</summary>
    public string TransactionControlNumber { get; init; } = string.Empty;

    /// <summary>ISA13 of the interchange being acknowledged, for matching it up at the other end.</summary>
    public string AcknowledgedInterchangeControlNumber { get; init; } = string.Empty;

    /// <summary>Who the acknowledgment was addressed to — the sender of the file it acknowledges.</summary>
    public string SentTo { get; init; } = string.Empty;

    /// <summary>Who sent it — the receiver of the file it acknowledges.</summary>
    public string SentBy { get; init; } = string.Empty;

    /// <summary>When it was generated, in local time, matching ISA09/ISA10.</summary>
    public DateTime GeneratedAt { get; init; }

    /// <summary>One entry per functional group acknowledged. Each is one AK1…AK9 transaction set.</summary>
    public IReadOnlyList<AcknowledgedGroup> Groups { get; init; } = Array.Empty<AcknowledgedGroup>();

    /// <summary>
    /// Problems the acknowledgment could not report, with the reason.
    /// </summary>
    /// <remarks>
    /// The 997 acknowledges functional groups. Interchange-level defects — a missing IEA,
    /// an IEA02 that does not echo ISA13 — are outside its scope entirely; the message for
    /// those is a TA1 Interchange Acknowledgment, which rides in the ISA/IEA envelope rather
    /// than in a functional group. Saying so is more useful than silently dropping the
    /// finding, because a sender whose IEA02 is wrong will otherwise get a clean 997 and
    /// conclude the file was perfect.
    /// </remarks>
    public IReadOnlyList<string> OutOfScope { get; init; } = Array.Empty<string>();

    /// <summary>
    /// What <c>EdiX12.Core</c> reported when the generated 997 was parsed straight back.
    /// Empty means the envelope is sound.
    /// </summary>
    public IReadOnlyList<string> RoundTripDiagnostics { get; init; } = Array.Empty<string>();

    /// <summary>True when the generated 997 re-parsed with no envelope diagnostics.</summary>
    public bool RoundTripClean => RoundTripDiagnostics.Count == 0;

    /// <summary>
    /// The worst verdict across every group, which is what a dispatcher actually needs to
    /// see on the row: <c>A</c>, <c>E</c>, <c>P</c> or <c>R</c>.
    /// </summary>
    public string Verdict
    {
        get
        {
            string[] worstFirst = { "R", "P", "E", "A" };

            foreach (string code in worstFirst)
            {
                if (Groups.Any(g => g.AcknowledgmentCode == code))
                {
                    return code;
                }
            }

            return "A";
        }
    }

    /// <summary>The verdict expanded, e.g. <c>Rejected</c>.</summary>
    public string VerdictLabel => AcknowledgmentCodes.DescribeGroupCode(Verdict);

    /// <summary>True when nothing was rejected and no errors were noted.</summary>
    public bool IsAccepted => Verdict == "A";

    /// <summary>True when at least one transaction set was rejected.</summary>
    public bool IsRejected => Verdict is "R" or "P";

    /// <summary>
    /// Every syntax error the acknowledgment actually reports, already expanded.
    /// </summary>
    /// <remarks>
    /// Strictly what went on the wire. <see cref="OutOfScope"/> is deliberately not folded
    /// in: the difference between "we told the partner this" and "we noticed this and could
    /// not tell them" is the entire point of keeping the second list, and merging them would
    /// leave an operator believing the sender had been informed of something they had not.
    /// </remarks>
    public IReadOnlyList<string> Findings =>
        Groups.SelectMany(g => g.Findings).ToList();
}

/// <summary>One acknowledged functional group: the AK1 heading, its AK2/AK5 loops and its AK9.</summary>
public sealed class AcknowledgedGroup
{
    /// <summary>AK101, echoing GS01 of the group being acknowledged — <c>SM</c> for a 204.</summary>
    public string FunctionalIdentifierCode { get; init; } = string.Empty;

    /// <summary>AK102, echoing GS06.</summary>
    public string GroupControlNumber { get; init; } = string.Empty;

    /// <summary>AK103, echoing GS08, e.g. <c>005010</c>.</summary>
    public string VersionReleaseIndustryCode { get; init; } = string.Empty;

    /// <summary>AK901: <c>A</c>, <c>E</c>, <c>P</c> or <c>R</c>.</summary>
    public string AcknowledgmentCode { get; init; } = "A";

    /// <summary>AK901 expanded.</summary>
    public string AcknowledgmentLabel => AcknowledgmentCodes.DescribeGroupCode(AcknowledgmentCode);

    /// <summary>AK902, the number of transaction sets GE01 declared.</summary>
    public int TransactionSetsDeclared { get; init; }

    /// <summary>AK903, the number actually received.</summary>
    public int TransactionSetsReceived { get; init; }

    /// <summary>AK904, the number accepted.</summary>
    public int TransactionSetsAccepted { get; init; }

    /// <summary>AK905–AK909, the element 716 group-level syntax error codes.</summary>
    public IReadOnlyList<string> ErrorCodes { get; init; } = Array.Empty<string>();

    /// <summary>The AK2/AK5 loops, one per transaction set in the group.</summary>
    public IReadOnlyList<AcknowledgedTransactionSet> TransactionSets { get; init; } =
        Array.Empty<AcknowledgedTransactionSet>();

    /// <summary>Every error in this group, expanded into a sentence.</summary>
    public IReadOnlyList<string> Findings =>
        ErrorCodes
            .Select(code => $"AK9 group {GroupControlNumber}: {code} — {AcknowledgmentCodes.DescribeGroupError(code)}")
            .Concat(TransactionSets.SelectMany(t => t.Findings))
            .ToList();
}

/// <summary>One acknowledged transaction set: the AK2 heading and its AK5 verdict.</summary>
public sealed class AcknowledgedTransactionSet
{
    /// <summary>AK201, echoing ST01 — <c>204</c>.</summary>
    public string IdentifierCode { get; init; } = string.Empty;

    /// <summary>AK202, echoing ST02.</summary>
    public string ControlNumber { get; init; } = string.Empty;

    /// <summary>AK501: <c>A</c>, <c>E</c> or <c>R</c>.</summary>
    public string AcknowledgmentCode { get; init; } = "A";

    /// <summary>AK501 expanded.</summary>
    public string AcknowledgmentLabel => AcknowledgmentCodes.DescribeTransactionCode(AcknowledgmentCode);

    /// <summary>AK502–AK506, the element 718 transaction-set syntax error codes.</summary>
    public IReadOnlyList<string> ErrorCodes { get; init; } = Array.Empty<string>();

    /// <summary>Every error on this transaction set, expanded into a sentence.</summary>
    public IReadOnlyList<string> Findings =>
        ErrorCodes
            .Select(code =>
                $"AK5 {IdentifierCode} {ControlNumber}: {code} — {AcknowledgmentCodes.DescribeTransactionError(code)}")
            .ToList();
}

/// <summary>
/// The 997's four code lists, expanded.
/// </summary>
/// <remarks>
/// A 997 carries digits, not sentences: <c>AK5*R*4</c> is the whole of what the partner is
/// told. Every one of these lists is short and every one of them is looked up by hand at
/// three in the morning by somebody trying to find out why a load did not arrive, which is
/// the argument for keeping them next to the writer that emits them.
/// </remarks>
public static class AcknowledgmentCodes
{
    /// <summary>Element 717, Transaction Set Acknowledgment Code (AK501).</summary>
    /// <param name="code">The code as written.</param>
    public static string DescribeTransactionCode(string code) => code switch
    {
        "A" => "Accepted",
        "E" => "Accepted but errors were noted",
        "M" => "Rejected — message authentication code failed",
        "P" => "Partially accepted",
        "R" => "Rejected",
        "W" => "Rejected — assurance failed validity tests",
        "X" => "Rejected — content after decryption could not be analysed",
        _ => code,
    };

    /// <summary>Element 715, Functional Group Acknowledge Code (AK901).</summary>
    /// <param name="code">The code as written.</param>
    public static string DescribeGroupCode(string code) => code switch
    {
        "A" => "Accepted",
        "E" => "Accepted but errors were noted",
        "M" => "Rejected — message authentication code failed",
        "P" => "Partially accepted — at least one transaction set was rejected",
        "R" => "Rejected",
        "W" => "Rejected — assurance failed validity tests",
        "X" => "Rejected — content after decryption could not be analysed",
        _ => code,
    };

    /// <summary>Element 718, Transaction Set Syntax Error Code (AK502–AK506).</summary>
    /// <param name="code">The code as written.</param>
    public static string DescribeTransactionError(string code) => code switch
    {
        "1" => "Transaction set not supported",
        "2" => "Transaction set trailer missing",
        "3" => "Transaction set control number in header and trailer do not match",
        "4" => "Number of included segments does not match actual count",
        "5" => "One or more segments in error",
        "6" => "Missing or invalid transaction set identifier",
        "7" => "Missing or invalid transaction set control number",
        _ => code,
    };

    /// <summary>Element 716, Functional Group Syntax Error Code (AK905–AK909).</summary>
    /// <param name="code">The code as written.</param>
    public static string DescribeGroupError(string code) => code switch
    {
        "1" => "Functional group not supported",
        "2" => "Functional group version not supported",
        "3" => "Functional group trailer missing",
        "4" => "Group control number in the functional group header and trailer do not agree",
        "5" => "Number of included transaction sets does not match actual count",
        "6" => "Group control number violates syntax",
        _ => code,
    };
}
