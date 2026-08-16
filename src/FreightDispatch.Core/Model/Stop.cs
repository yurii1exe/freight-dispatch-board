namespace FreightDispatch.Core.Model;

/// <summary>
/// One stop on a load — an S5 loop from the 204.
/// </summary>
/// <remarks>
/// A truckload move is a sequence of stops, not a pickup and a delivery. Multi-stop
/// tenders are ordinary: three pickups and one drop, or one pickup and four drops.
/// S501 carries the sequence and S502 the reason the truck is there.
/// </remarks>
public sealed class Stop
{
    /// <summary>S501, Stop Sequence Number. 1-based, in the order the driver runs them.</summary>
    public int Sequence { get; init; }

    /// <summary>S502, Stop Reason Code (element 163): <c>LD</c>, <c>UL</c>, <c>CL</c>, <c>CU</c>, <c>PL</c>, <c>PU</c>.</summary>
    public string ReasonCode { get; init; } = string.Empty;

    /// <summary>The reason code expanded for display.</summary>
    public string ReasonName => DescribeReason(ReasonCode);

    /// <summary>True when the truck is picking up here rather than delivering.</summary>
    public bool IsPickup => ReasonCode is "LD" or "CL" or "PL" or "PA";

    /// <summary>S503/S504, the weight handled at this stop and its unit code (<c>L</c> pounds, <c>K</c> kilograms).</summary>
    public decimal? Weight { get; init; }

    /// <summary>S504, Weight Unit Code.</summary>
    public string WeightUnit { get; init; } = string.Empty;

    /// <summary>S505/S506, the number of units handled at this stop and the unit of measure.</summary>
    public decimal? Units { get; init; }

    /// <summary>S506, Unit or Basis for Measurement Code, e.g. <c>PL</c> pallets, <c>CA</c> cases.</summary>
    public string UnitOfMeasure { get; init; } = string.Empty;

    /// <summary>The N1 loop for this stop: who and where.</summary>
    public Party Location { get; init; } = new();

    /// <summary>The appointment or requested window, from this stop's G62 segments.</summary>
    public StopWindow Window { get; init; } = new();

    /// <summary>L11 references scoped to this stop — PO numbers, delivery order numbers.</summary>
    public IReadOnlyList<ReferenceNumber> References { get; init; } = Array.Empty<ReferenceNumber>();

    /// <summary>NTE free-form notes: dock instructions, appointment requirements, lumper rules.</summary>
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();

    /// <summary>L5 commodity descriptions ordered at this stop.</summary>
    public IReadOnlyList<string> Commodities { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Expands the stop reason codes a motor carrier load tender actually uses. The full
    /// element 163 list runs to 23 codes, most of them rail and intermodal.
    /// </summary>
    /// <param name="code">S502, a value of X12 element 163.</param>
    public static string DescribeReason(string code) => code switch
    {
        "CL" => "Complete Load",
        "CU" => "Complete Unload",
        "LD" => "Load",
        "UL" => "Unload",
        "PL" => "Part Load",
        "PU" => "Part Unload",
        "PA" => "Pickup Pre-loaded Equipment",
        "DT" => "Drop Trailer",
        "SL" => "Spot for Load",
        "SU" => "Spot for Unload",
        "TL" => "Transload",
        "IN" => "Inspection",
        "AA" => "Point of Delay",
        _ => code,
    };
}

/// <summary>
/// The time window for a stop, assembled from its G62 Date/Time segments.
/// </summary>
/// <remarks>
/// <para>A 204 does not carry "the appointment" as one field. It carries a pair of G62
/// segments whose qualifiers say what kind of boundary each one is:</para>
/// <code>
/// pickup    G62*37  Ship Not Before Date        G62*38  Ship Not Later Than Date
/// delivery  G62*53  Deliver Not Before Date     G62*54  Deliver No Later Than Date
/// </code>
/// <para>A single G62*10 (Requested Ship) or G62*02 (Delivery Requested) with no partner
/// means an open-ended date rather than a window, and the board says so instead of
/// inventing a closing time.</para>
/// <para>The times carry no zone. X12 has a Time Code element (623) — <c>LT</c> means
/// "local time at the stop", which is the usual value and the reason these are held as
/// unzoned <see cref="DateTime"/> rather than converted to UTC.</para>
/// </remarks>
public sealed class StopWindow
{
    /// <summary>Start of the window, in local time at the stop, or null if the tender gave none.</summary>
    public DateTime? Earliest { get; init; }

    /// <summary>End of the window, in local time at the stop, or null for an open-ended request.</summary>
    public DateTime? Latest { get; init; }

    /// <summary>X12 element 623 Time Code as sent, e.g. <c>LT</c> local time, <c>ET</c> eastern.</summary>
    public string TimeCode { get; init; } = string.Empty;

    /// <summary>True when the tender pinned both ends of the window.</summary>
    public bool IsAppointment => Earliest.HasValue && Latest.HasValue;

    /// <summary>True when no G62 was present at all.</summary>
    public bool IsEmpty => !Earliest.HasValue && !Latest.HasValue;
}
