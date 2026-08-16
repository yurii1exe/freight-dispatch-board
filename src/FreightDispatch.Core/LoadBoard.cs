using System.Collections.Concurrent;
using FreightDispatch.Core.Edi;
using FreightDispatch.Core.Model;

namespace FreightDispatch.Core;

/// <summary>
/// The board: the loads currently on it and the operations a dispatcher performs on them.
/// </summary>
/// <remarks>
/// <para>State is in memory. This is a demonstration of the tender-to-status loop, not a
/// TMS; a real board needs the loads, the status events and the control number sequence in
/// one durable transaction, because those three going out of step is how a partner ends up
/// with two interchanges numbered 000000417 and no way to tell which one was the
/// delivery.</para>
/// <para>Everything the board does is additive. A status event is a statement about
/// something that happened, and the correction for a wrong one is another 214, never an
/// edit — which is why <see cref="Load.Events"/> only ever grows.</para>
/// </remarks>
public sealed class LoadBoard
{
    private readonly ConcurrentDictionary<Guid, Load> _loads = new();
    private readonly ControlNumbers _controlNumbers;
    private readonly Edi214Writer _writer;
    private readonly Func<DateTime> _clock;

    /// <summary>Creates a board.</summary>
    /// <param name="controlNumbers">
    /// The outbound control number sequence. Shared across every load, because ISA13 is
    /// unique per interchange and not per shipment.
    /// </param>
    /// <param name="clock">
    /// Supplies "now" in local time, for the ISA and GS timestamps. Injected so tests can
    /// produce a byte-identical file twice.
    /// </param>
    public LoadBoard(ControlNumbers? controlNumbers = null, Func<DateTime>? clock = null)
    {
        _controlNumbers = controlNumbers ?? new ControlNumbers(4001);
        _clock = clock ?? (() => DateTime.Now);
        _writer = new Edi214Writer(_controlNumbers);
    }

    /// <summary>Every load on the board, newest tender first.</summary>
    public IReadOnlyList<Load> Loads =>
        _loads.Values.OrderByDescending(l => l.ReceivedAt).ToList();

    /// <summary>Finds a load, or null.</summary>
    /// <param name="id">The board identifier.</param>
    public Load? Find(Guid id) => _loads.TryGetValue(id, out Load? load) ? load : null;

    /// <summary>
    /// Ingests a 204 and puts every load tender in it on the board.
    /// </summary>
    /// <param name="ediText">Raw X12 text beginning with ISA.</param>
    /// <returns>The loads created.</returns>
    /// <exception cref="EdiX12.Core.X12ParseException">The interchange is not readable.</exception>
    /// <exception cref="InvalidOperationException">The interchange contained no 204 transaction sets.</exception>
    public IReadOnlyList<Load> Tender(string ediText)
    {
        IReadOnlyList<Load> loads = Edi204Reader.Read(ediText);

        if (loads.Count == 0)
        {
            throw new InvalidOperationException(
                "The interchange parsed, but it contains no 204 transaction sets. " +
                "Check GS01 and ST01 — a 214 or a 990 will parse perfectly and still not be a load tender.");
        }

        foreach (Load load in loads)
        {
            _loads[load.Id] = load;
        }

        return loads;
    }

    /// <summary>
    /// Removes a load from the board. Not an EDI operation — this is the demo's reset
    /// button, not a 204 cancellation, which would be a new tender with B2A01 = 01.
    /// </summary>
    /// <param name="id">The board identifier.</param>
    public bool Remove(Guid id) => _loads.TryRemove(id, out _);

    /// <summary>
    /// Empties the board. The control number sequence deliberately does not reset with it:
    /// the loads are gone, but the partner has still seen those ISA13 values.
    /// </summary>
    public void Clear() => _loads.Clear();

    /// <summary>
    /// Advances a load to the next status and generates the 214 that reports it.
    /// </summary>
    /// <param name="id">The load's board identifier.</param>
    /// <param name="status">
    /// The status being entered. Must be exactly one step forward: a load cannot be
    /// delivered before it is loaded, and a board that lets a dispatcher click it anyway is
    /// a board that sends a partner a delivery notice for freight still sitting on a dock.
    /// </param>
    /// <param name="occurredAt">
    /// When it happened, in local time at the location. Defaults to now. Dispatchers
    /// backdate constantly — the driver called at 14:10 and the update gets keyed at 15:40
    /// — and the 214 must carry when it happened, not when it was typed.
    /// </param>
    /// <param name="reasonCode">AT702, element 1651. <c>NS</c> unless something went wrong.</param>
    /// <param name="city">MS101. Defaults to the stop the status implies.</param>
    /// <param name="state">MS102.</param>
    /// <param name="note">A dispatcher note. Stays on the board; not sent.</param>
    /// <returns>The event, carrying the generated 214.</returns>
    /// <exception cref="KeyNotFoundException">No such load.</exception>
    /// <exception cref="InvalidOperationException">The transition is not one step forward.</exception>
    public StatusEvent Advance(
        Guid id,
        LoadStatus status,
        DateTime? occurredAt = null,
        string reasonCode = "NS",
        string? city = null,
        string? state = null,
        string? note = null)
    {
        Load load = Find(id) ?? throw new KeyNotFoundException($"No load with id {id}.");

        lock (load.Events)
        {
            if (!StatusCatalog.CanTransition(load.Status, status))
            {
                throw new InvalidOperationException(
                    $"A load in '{StatusCatalog.DescribeStatus(load.Status)}' cannot move to " +
                    $"'{StatusCatalog.DescribeStatus(status)}'. The next step is " +
                    $"'{(StatusCatalog.Next(load.Status) is { } next ? StatusCatalog.DescribeStatus(next) : "none — the load is delivered")}'.");
            }

            Stop? stop = StopFor(load, status);
            string eventCity = Coalesce(city, stop?.Location.City);
            string eventState = Coalesce(state, stop?.Location.State);
            string eventCountry = Coalesce(null, stop?.Location.Country, "US");

            DateTime happenedAt = occurredAt ?? _clock();
            string statusCode = StatusCatalog.StatusCodeFor(status);

            Edi214Result result = _writer.Write(
                load,
                statusCode,
                reasonCode,
                happenedAt,
                eventCity,
                eventState,
                eventCountry,
                _clock());

            var statusEvent = new StatusEvent
            {
                Status = status,
                StatusCode = statusCode,
                ReasonCode = reasonCode,
                OccurredAt = happenedAt,
                City = eventCity,
                State = eventState,
                Country = eventCountry,
                Note = note ?? string.Empty,
                Edi214 = result.Edi,
                InterchangeControlNumber = result.InterchangeControlNumber,
                TransactionControlNumber = result.TransactionControlNumber,
                RoundTripDiagnostics = result.Diagnostics,
            };

            load.Events.Add(statusEvent);
            load.Status = status;

            return statusEvent;
        }
    }

    /// <summary>
    /// Which stop a status happened at.
    /// </summary>
    /// <remarks>
    /// Dispatched, at-shipper, loaded and departed all happen at the first pickup; arrival
    /// and delivery at the last drop. Multi-stop loads make this a simplification — a truck
    /// arriving at its third of five stops is "at consignee" on this board and the MS1 will
    /// name the final drop rather than the one it is actually sitting at. Reporting per
    /// stop means tracking a current-stop pointer, which is the next thing this needs and
    /// is listed as such rather than pretended away.
    /// </remarks>
    private static Stop? StopFor(Load load, LoadStatus status) => status switch
    {
        LoadStatus.Dispatched or LoadStatus.AtShipper or LoadStatus.Loaded => load.Origin,
        LoadStatus.InTransit => load.Origin,
        LoadStatus.AtConsignee or LoadStatus.Delivered => load.Destination,
        _ => null,
    };

    private static string Coalesce(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;
}
