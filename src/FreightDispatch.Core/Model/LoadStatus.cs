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
/// <para><b>The codes depend on the stop, not only on the state.</b> Arriving somewhere is
/// <c>X3</c> at a pickup and <c>X1</c> at a delivery; finishing there is <c>CP</c> against
/// <c>D1</c>; leaving is <c>AF</c> against <c>CD</c>. A board that emits the delivery codes
/// for every stop is telling a four-stop load's partner that the truck delivered in Fresno,
/// which is where it loaded.</para>
/// </remarks>
public static class StatusCatalog
{
    /// <summary>
    /// The X12 element 1650 code for arriving at a stop.
    /// </summary>
    /// <param name="atPickup">True when the stop is a pickup (S502 <c>CL</c>, <c>LD</c>, <c>PL</c>).</param>
    public static string ArrivalCode(bool atPickup) =>
        atPickup
            ? "X3"   // Arrived at Pickup Location
            : "X1";  // Arrived at Delivery Location

    /// <summary>
    /// The X12 element 1650 code for finishing work at a stop.
    /// </summary>
    /// <param name="atPickup">True when the stop is a pickup.</param>
    public static string CompletionCode(bool atPickup) =>
        atPickup
            ? "CP"   // Completed Loading at Pickup Location
            : "D1";  // Completed Unloading at Delivery Location

    /// <summary>
    /// The X12 element 1650 code for leaving a stop.
    /// </summary>
    /// <param name="fromPickup">True when the stop being left is a pickup.</param>
    public static string DepartureCode(bool fromPickup) =>
        fromPickup
            ? "AF"   // Carrier Departed Pickup Location with Shipment
            : "CD";  // Carrier Departed Delivery Location

    /// <summary>
    /// The X12 element 1650 code for a board state, given the kind of stop it happened at.
    /// </summary>
    /// <param name="status">The board state being entered.</param>
    /// <param name="atPickup">
    /// True when the stop involved is a pickup. Ignored for
    /// <see cref="LoadStatus.Dispatched"/>, which is about the shipment rather than a place.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="status"/> is <see cref="LoadStatus.Tendered"/>, which produces no
    /// 214 — the load has not moved and there is nothing to report.
    /// </exception>
    public static string StatusCodeFor(LoadStatus status, bool atPickup = false) => status switch
    {
        LoadStatus.Dispatched => "XB",                    // Shipment Acknowledged
        LoadStatus.AtShipper => ArrivalCode(atPickup: true),
        LoadStatus.Loaded => CompletionCode(atPickup: true),
        LoadStatus.InTransit => DepartureCode(atPickup),
        LoadStatus.AtConsignee => ArrivalCode(atPickup),
        LoadStatus.Delivered => CompletionCode(atPickup),
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
        "CD" => "Carrier Departed Delivery Location",
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
    /// The status the board offers next.
    /// </summary>
    /// <remarks>
    /// <para>The pickup leg runs once and then the board <b>cycles</b>: a truck with three
    /// drops on it arrives, works the stop, leaves, and arrives again. So
    /// <see cref="LoadStatus.AtConsignee"/> leads back to <see cref="LoadStatus.InTransit"/>
    /// while stops remain, and only reaches <see cref="LoadStatus.Delivered"/> at the last
    /// one.</para>
    /// <para>There is no path backwards. A 214 is a statement about something that happened
    /// and you cannot un-send it — the correction is a further 214, not a rewrite.</para>
    /// </remarks>
    /// <param name="status">The current state.</param>
    /// <param name="stopsRemainAfterCurrent">
    /// True when the load has stops beyond the one the truck is working now.
    /// </param>
    /// <returns>The next state, or null when the load is finished.</returns>
    public static LoadStatus? Next(LoadStatus status, bool stopsRemainAfterCurrent = false)
    {
        if (status == LoadStatus.Delivered)
        {
            return null;
        }

        if (status == LoadStatus.AtConsignee)
        {
            return stopsRemainAfterCurrent ? LoadStatus.InTransit : LoadStatus.Delivered;
        }

        return status + 1;
    }

    /// <summary>
    /// Whether a transition is one the board allows.
    /// </summary>
    /// <param name="from">Current state.</param>
    /// <param name="to">Proposed state.</param>
    /// <param name="stopsRemainAfterCurrent">True when stops remain beyond the current one.</param>
    public static bool CanTransition(LoadStatus from, LoadStatus to, bool stopsRemainAfterCurrent = false) =>
        Next(from, stopsRemainAfterCurrent) == to;

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
