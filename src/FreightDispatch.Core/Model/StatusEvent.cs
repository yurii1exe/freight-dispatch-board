namespace FreightDispatch.Core.Model;

/// <summary>
/// One thing that happened to a load, and the 214 that was sent about it.
/// </summary>
/// <remarks>
/// The event is the record; the EDI is a rendering of it. Keeping the generated text on
/// the event rather than regenerating on demand means the control numbers stay stable and
/// what the board shows is byte-for-byte what the partner received.
/// </remarks>
public sealed class StatusEvent
{
    /// <summary>Stable identifier, used in the API route for the raw 214.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>The board state entered.</summary>
    public LoadStatus Status { get; init; }

    /// <summary>AT701, the element 1650 code sent for this event.</summary>
    public string StatusCode { get; init; } = string.Empty;

    /// <summary>AT702, the element 1651 reason code. <c>NS</c> for a normal status.</summary>
    public string ReasonCode { get; init; } = "NS";

    /// <summary>
    /// AT705/AT706, when it happened, in local time at the location. Not UTC: X12 carries
    /// no offset and AT707 says <c>LT</c>.
    /// </summary>
    public DateTime OccurredAt { get; init; }

    /// <summary>AT707, the element 623 Time Code. <c>LT</c> local time.</summary>
    public string TimeCode { get; init; } = "LT";

    /// <summary>MS101, the city the truck was in.</summary>
    public string City { get; init; } = string.Empty;

    /// <summary>MS102, the state or province.</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>MS103, the country code.</summary>
    public string Country { get; init; } = "US";

    /// <summary>Free-text dispatcher note. Not sent in the 214 — it stays on the board.</summary>
    public string Note { get; init; } = string.Empty;

    /// <summary>When the board recorded the event, in UTC. Distinct from <see cref="OccurredAt"/>.</summary>
    public DateTimeOffset RecordedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>The complete 214 interchange generated for this event.</summary>
    public string Edi214 { get; init; } = string.Empty;

    /// <summary>ISA13 of the generated interchange.</summary>
    public string InterchangeControlNumber { get; init; } = string.Empty;

    /// <summary>ST02 of the generated transaction set.</summary>
    public string TransactionControlNumber { get; init; } = string.Empty;

    /// <summary>
    /// What <c>EdiX12.Core</c> said when the generated 214 was parsed straight back and
    /// validated. Empty means all nine envelope checks passed.
    /// </summary>
    public IReadOnlyList<string> RoundTripDiagnostics { get; init; } = Array.Empty<string>();

    /// <summary>True when the generated 214 re-parsed cleanly with no envelope diagnostics.</summary>
    public bool RoundTripClean => RoundTripDiagnostics.Count == 0;

    /// <summary>The element 1650 description, for the detail panel.</summary>
    public string StatusCodeName => StatusCatalog.DescribeStatusCode(StatusCode);
}
