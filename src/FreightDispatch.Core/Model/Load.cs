namespace FreightDispatch.Core.Model;

/// <summary>
/// A load: one 204 load tender normalised into something a dispatcher can work with.
/// </summary>
/// <remarks>
/// The 204 is the tender. The load is what the tender becomes once it is on the board and
/// people start moving it. Everything before <see cref="Status"/> comes off the wire and
/// does not change; everything from <see cref="Status"/> down is the board's own state.
/// </remarks>
public sealed class Load
{
    /// <summary>Board identifier. Not an EDI value — the 204 has no globally unique key.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// B204, Shipment Identification Number. The tendering party's load number, and the
    /// value that goes back in B1002 on every 214. This is the number both sides say on
    /// the phone.
    /// </summary>
    public string ShipmentId { get; init; } = string.Empty;

    /// <summary>B202, the Standard Carrier Alpha Code the load was tendered to.</summary>
    public string Scac { get; init; } = string.Empty;

    /// <summary>B206, Shipment Method of Payment: <c>PP</c> prepaid, <c>CC</c> collect, <c>TP</c> third party.</summary>
    public string PaymentMethod { get; init; } = string.Empty;

    /// <summary>B2A01, Transaction Set Purpose Code: <c>00</c> original, <c>04</c> change, <c>01</c> cancellation.</summary>
    public string PurposeCode { get; init; } = "00";

    /// <summary>N711, Equipment Description Code (element 40): <c>TF</c> dry van, <c>RT</c> reefer, <c>FT</c> flatbed.</summary>
    public string EquipmentCode { get; init; } = string.Empty;

    /// <summary>N715, Equipment Length in feet as sent — 53, 48, 20.</summary>
    public string EquipmentLength { get; init; } = string.Empty;

    /// <summary>N713, Temperature Control, present on reefer tenders, e.g. <c>-10F</c> or <c>34F</c>.</summary>
    public string TemperatureControl { get; init; } = string.Empty;

    /// <summary>N702, Equipment Number — the trailer, when the tender pre-assigns one.</summary>
    public string TrailerNumber { get; init; } = string.Empty;

    /// <summary>L301, the total weight for the load.</summary>
    public decimal? TotalWeight { get; init; }

    /// <summary>L302, Weight Qualifier — <c>G</c> gross, <c>N</c> net.</summary>
    public string WeightQualifier { get; init; } = string.Empty;

    /// <summary>Header-level L11 references: the BOL, the broker's load number, the PRO.</summary>
    public IReadOnlyList<ReferenceNumber> References { get; init; } = Array.Empty<ReferenceNumber>();

    /// <summary>Header-level NTE notes — instructions that apply to the whole move.</summary>
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();

    /// <summary>The header N1 loop, normally <c>BT</c> bill-to: who pays for this move.</summary>
    public Party? BillTo { get; init; }

    /// <summary>The stops, in sequence.</summary>
    public IReadOnlyList<Stop> Stops { get; init; } = Array.Empty<Stop>();

    /// <summary>ISA06 of the interchange the tender arrived in.</summary>
    public string TenderedBy { get; init; } = string.Empty;

    /// <summary>ISA08 of the interchange the tender arrived in.</summary>
    public string TenderedTo { get; init; } = string.Empty;

    /// <summary>True when ISA15 was <c>P</c>. A test tender should never reach the board unflagged.</summary>
    public bool IsProduction { get; init; }

    /// <summary>
    /// ST02 of the 204 transaction set this load came out of.
    /// </summary>
    /// <remarks>
    /// Kept because it is the only handle the 997 has on an individual document: AK202
    /// echoes it, and matching a rejection back to the load it rejected is done on this
    /// value and nothing else.
    /// </remarks>
    public string TransactionControlNumber { get; init; } = string.Empty;

    /// <summary>The 204 exactly as received, kept so the board can show the wire next to the human view.</summary>
    public string SourceEdi { get; init; } = string.Empty;

    /// <summary>When the tender was ingested.</summary>
    public DateTimeOffset ReceivedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Envelope diagnostics from parsing the inbound 204, if the sender got something wrong.</summary>
    public IReadOnlyList<string> TenderDiagnostics { get; init; } = Array.Empty<string>();

    /// <summary>
    /// The 997 that went back for the interchange this load arrived in.
    /// </summary>
    /// <remarks>
    /// Shared by every load in the same interchange, because a 997 acknowledges functional
    /// groups and not individual documents. A file carrying three tenders produces one
    /// acknowledgment with three AK2/AK5 loops in it, and all three loads point at it.
    /// </remarks>
    public FunctionalAcknowledgment? Acknowledgment { get; set; }

    /// <summary>
    /// The 210 invoice, once the load has delivered. Null before that, because there is
    /// nothing to bill for freight that has not moved.
    /// </summary>
    public FreightInvoice? Invoice { get; set; }

    /// <summary>The AK5 loop of this load's own transaction set, out of the 997 that was sent.</summary>
    public AcknowledgedTransactionSet? TenderAcknowledgment =>
        Acknowledgment?.Groups
            .SelectMany(g => g.TransactionSets)
            .FirstOrDefault(t => t.ControlNumber == TransactionControlNumber);

    /// <summary>
    /// True when the 997 told the partner this tender was refused.
    /// </summary>
    /// <remarks>
    /// The load is on the board regardless, and that is deliberate — a dispatcher still has
    /// a truck to cover while the sender fixes their file. This flag is what marks the row
    /// so nobody has to guess which of the two facts they are looking at.
    /// </remarks>
    public bool TenderRejected => TenderAcknowledgment?.AcknowledgmentCode == "R";

    /// <summary>Current board state.</summary>
    public LoadStatus Status { get; set; } = LoadStatus.Tendered;

    /// <summary>
    /// S501 of the stop the truck is working now — the one it has arrived at, or the one it
    /// is running to.
    /// </summary>
    /// <remarks>
    /// Without this a multi-stop load has nowhere to say where the truck actually is, and
    /// every status message names the final drop. On the four-stop reefer that means telling
    /// the partner the freight was delivered in Fresno, which is where it loaded.
    /// </remarks>
    public int CurrentStopSequence { get; set; }

    /// <summary>The stop the truck is working now.</summary>
    public Stop? CurrentStop =>
        Stops.FirstOrDefault(s => s.Sequence == CurrentStopSequence) ?? Stops.FirstOrDefault();

    /// <summary>1-based position of the current stop in the run, for "stop 2 of 4".</summary>
    public int CurrentStopOrdinal
    {
        get
        {
            for (int i = 0; i < Stops.Count; i++)
            {
                if (Stops[i].Sequence == CurrentStopSequence)
                {
                    return i + 1;
                }
            }

            return Stops.Count == 0 ? 0 : 1;
        }
    }

    /// <summary>The next stop after the current one, or null when the truck is on its last.</summary>
    public Stop? NextStop =>
        Stops.FirstOrDefault(s => s.Sequence > CurrentStopSequence);

    /// <summary>True when there is still a stop to run after the current one.</summary>
    public bool StopsRemainAfterCurrent => NextStop is not null;

    /// <summary>True when this load has more than a straight pickup and drop.</summary>
    public bool IsMultiStop => Stops.Count > 2;

    /// <summary>Every status change, oldest first, each carrying its generated 214.</summary>
    public List<StatusEvent> Events { get; } = new();

    /// <summary>The first pickup stop, which is what the board shows as origin.</summary>
    public Stop? Origin => Stops.FirstOrDefault(s => s.IsPickup) ?? Stops.FirstOrDefault();

    /// <summary>The last delivery stop, which is what the board shows as destination.</summary>
    public Stop? Destination => Stops.LastOrDefault(s => !s.IsPickup) ?? Stops.LastOrDefault();

    /// <summary>Number of stops beyond a straight pickup-and-drop. Zero for a two-stop load.</summary>
    public int ExtraStops => Math.Max(0, Stops.Count - 2);
}
