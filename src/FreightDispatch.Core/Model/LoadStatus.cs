namespace FreightDispatch.Core.Model;

/// <summary>
/// Where a load is, from the dispatcher's point of view.
/// </summary>
/// <remarks>
/// These are board states, not X12 codes. Each one maps to exactly one X12 element 1650
/// Shipment Status Code when a 214 is emitted — see <see cref="StatusCatalog"/>. Keeping
/// the two vocabularies separate is deliberate: dispatchers say "he's loaded", partners
/// require <c>CP</c>, and the translation belongs in one table rather than scattered
/// through the UI.
/// </remarks>
public enum LoadStatus
{
    /// <summary>The 204 arrived and nobody has done anything with it yet.</summary>
    Tendered = 0,

    /// <summary>A truck is assigned. The carrier has accepted the load.</summary>
    Dispatched = 1,

    /// <summary>The truck is at the first pickup.</summary>
    AtShipper = 2,

    /// <summary>Loading finished. Product is on the trailer.</summary>
    Loaded = 3,

    /// <summary>Departed the pickup and running to the consignee.</summary>
    InTransit = 4,

    /// <summary>The truck is at the delivery location.</summary>
    AtConsignee = 5,

    /// <summary>Unloaded and released. The load is done.</summary>
    Delivered = 6,
}

/// <summary>
/// The translation table between board states and the wire.
/// </summary>
/// <remarks>
/// <para>Every status change on the board produces one 214 carrying one AT7 segment. AT701
/// is the Shipment Status Code (element 1650); AT702 is the Shipment Status Reason Code
/// (element 1651), which is <c>NS</c> — Normal Status — unless something has gone
/// wrong.</para>
/// <para>The mappings below are the conventional ones. Partners do vary: some want
/// <c>AF</c> for departure and nothing for loading, some want <c>X6</c> pings every four
/// hours in transit, some reject <c>XB</c> outright because they treat the 990 as the
/// acknowledgment. That variation is why this is a table and not a switch statement
/// buried in the 214 writer.</para>
/// </remarks>
public static class StatusCatalog
{
    /// <summary>
    /// The X12 element 1650 Shipment Status Code emitted for each board state.
    /// </summary>
    /// <param name="status">The board state being entered.</param>
    /// <returns>A two-character element 1650 code.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="status"/> is <see cref="LoadStatus.Tendered"/>, which produces no
    /// 214 — the load has not moved and there is nothing to report.
    /// </exception>
    public static string StatusCodeFor(LoadStatus status) => status switch
    {
        LoadStatus.Dispatched => "XB",   // Shipment Acknowledged
        LoadStatus.AtShipper => "X3",    // Arrived at Pickup Location
        LoadStatus.Loaded => "CP",       // Completed Loading at Pickup Location
        LoadStatus.InTransit => "AF",    // Carrier Departed Pickup Location with Shipment
        LoadStatus.AtConsignee => "X1",  // Arrived at Delivery Location
        LoadStatus.Delivered => "D1",    // Completed Unloading at Delivery Location
        _ => throw new ArgumentOutOfRangeException(
            nameof(status), status,
            "Tendered is the state before any 214 exists. Nothing has happened to report."),
    };

    /// <summary>The X12 description of an element 1650 code, for showing next to the generated segment.</summary>
    /// <param name="code">A value of element 1650.</param>
    public static string DescribeStatusCode(string code) => code switch
    {
        "XB" => "Shipment Acknowledged",
        "X3" => "Arrived at Pickup Location",
        "CP" => "Completed Loading at Pickup Location",
        "AF" => "Carrier Departed Pickup Location with Shipment",
        "X6" => "En Route to Delivery Location",
        "X1" => "Arrived at Delivery Location",
        "D1" => "Completed Unloading at Delivery Location",
        "SD" => "Shipment Delayed",
        "AH" => "Attempted Delivery",
        "CA" => "Shipment Cancelled",
        _ => code,
    };

    /// <summary>The label a dispatcher reads on the board.</summary>
    /// <param name="status">The board state.</param>
    public static string DescribeStatus(LoadStatus status) => status switch
    {
        LoadStatus.Tendered => "Tendered",
        LoadStatus.Dispatched => "Dispatched",
        LoadStatus.AtShipper => "At shipper",
        LoadStatus.Loaded => "Loaded",
        LoadStatus.InTransit => "In transit",
        LoadStatus.AtConsignee => "At consignee",
        LoadStatus.Delivered => "Delivered",
        _ => status.ToString(),
    };

    /// <summary>
    /// The status the board offers next. Loads move forward one step at a time; there is
    /// no path back, because a 214 is a statement about something that happened and you
    /// cannot un-send it — the correction is a further 214, not a rewrite.
    /// </summary>
    /// <param name="status">The current state.</param>
    /// <returns>The next state, or null when the load is delivered.</returns>
    public static LoadStatus? Next(LoadStatus status) =>
        status == LoadStatus.Delivered ? null : status + 1;

    /// <summary>
    /// Whether a transition is one the board allows. Forward by one step only.
    /// </summary>
    /// <param name="from">Current state.</param>
    /// <param name="to">Proposed state.</param>
    public static bool CanTransition(LoadStatus from, LoadStatus to) => Next(from) == to;

    /// <summary>Every board state, in order.</summary>
    public static IReadOnlyList<LoadStatus> All { get; } = new[]
    {
        LoadStatus.Tendered,
        LoadStatus.Dispatched,
        LoadStatus.AtShipper,
        LoadStatus.Loaded,
        LoadStatus.InTransit,
        LoadStatus.AtConsignee,
        LoadStatus.Delivered,
    };
}
