using System.Collections.Concurrent;
using System.Globalization;
using FreightDispatch.Core.Model;

namespace FreightDispatch.Core.Tms;

/// <summary>
/// An <see cref="ITmsAdapter"/> that stands in for a real system.
/// </summary>
/// <remarks>
/// <para>It accepts loads, assigns them identifiers in a shape that is not this board's, and
/// reports status back using a vocabulary that is not X12's — because that is what the
/// boundary actually has to absorb. The translation table below is the entire reason the
/// interface exists, and it is the piece that would be rewritten, and only that piece, for
/// each system it was pointed at.</para>
/// <para>It also refuses things, which a mock that only ever succeeds never teaches anyone
/// anything about. A load with no shipment identification number is refused, and so is a
/// second push of one already held: duplicate load numbers are the most common reason a
/// real push comes back rejected, and a board that treats a refusal as an exception will
/// crash on an ordinary Tuesday.</para>
/// </remarks>
public sealed class MockTmsAdapter : ITmsAdapter
{
    /// <summary>
    /// The far system's status vocabulary, mapped onto the board's.
    /// </summary>
    /// <remarks>
    /// Invented, and deliberately not X12 and not this board's enum, because every real one
    /// is a third thing. The direction of this table matters: it maps <em>their</em> codes
    /// to <em>ours</em>, so the board never has to know a code exists that it cannot act on.
    /// </remarks>
    private static readonly Dictionary<string, LoadStatus> StatusVocabulary =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["COVERED"] = LoadStatus.Dispatched,
            ["AT_ORIGIN"] = LoadStatus.AtShipper,
            ["LOADED"] = LoadStatus.Loaded,
            ["ROLLING"] = LoadStatus.InTransit,
            ["AT_DEST"] = LoadStatus.AtConsignee,
            ["EMPTY"] = LoadStatus.Delivered,
        };

    private readonly ConcurrentDictionary<string, TmsHeldLoad> _held = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    private Func<TmsStatusCallback, CancellationToken, Task>? _onStatus;
    private int _sequence;

    /// <inheritdoc />
    public string Name => "mock-tms";

    /// <inheritdoc />
    public bool IsConnected => _onStatus is not null;

    /// <summary>Every load this adapter is holding, in the order they were pushed.</summary>
    public IReadOnlyList<TmsHeldLoad> Held =>
        _held.Values.OrderBy(l => l.Sequence).ToList();

    /// <summary>The status codes this adapter knows how to translate, for the console.</summary>
    public static IReadOnlyList<string> NativeStatusCodes => StatusVocabulary.Keys.ToList();

    /// <summary>Translates one of the far system's codes into board vocabulary.</summary>
    /// <param name="nativeCode">Their code.</param>
    /// <returns>The board status, or null when the code means nothing here.</returns>
    public static LoadStatus? Translate(string nativeCode) =>
        StatusVocabulary.TryGetValue(nativeCode ?? string.Empty, out LoadStatus status) ? status : null;

    /// <inheritdoc />
    public Task<TmsPushResult> PushLoadAsync(Load load, CancellationToken cancellationToken = default)
    {
        if (load is null)
        {
            throw new ArgumentNullException(nameof(load));
        }

        if (string.IsNullOrWhiteSpace(load.ShipmentId))
        {
            return Task.FromResult(TmsPushResult.Refused(
                "B204 Shipment Identification Number is empty. There is nothing to key the load on."));
        }

        lock (_gate)
        {
            if (_held.ContainsKey(load.ShipmentId))
            {
                return Task.FromResult(TmsPushResult.Refused(
                    $"Load {load.ShipmentId} is already open. A change would be a new tender with B2A01 = 04."));
            }

            int sequence = ++_sequence;
            string tmsLoadId = string.Create(CultureInfo.InvariantCulture, $"TMS-{sequence:0000}");

            _held[load.ShipmentId] = new TmsHeldLoad(
                tmsLoadId,
                load.ShipmentId,
                load.Id,
                load.Scac,
                load.Origin?.Location.CityState ?? string.Empty,
                load.Destination?.Location.CityState ?? string.Empty,
                load.Stops.Count,
                sequence,
                DateTimeOffset.UtcNow);

            return Task.FromResult(TmsPushResult.Ok(tmsLoadId));
        }
    }

    /// <inheritdoc />
    public Task SubscribeAsync(
        Func<TmsStatusCallback, CancellationToken, Task> onStatus,
        CancellationToken cancellationToken = default)
    {
        _onStatus = onStatus ?? throw new ArgumentNullException(nameof(onStatus));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UnsubscribeAsync(CancellationToken cancellationToken = default)
    {
        _onStatus = null;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Raises a status callback, as the far system would.
    /// </summary>
    /// <remarks>
    /// This is the only member that exists because it is a mock. A real adapter's callbacks
    /// arrive from a webhook or a poll; here they are driven from a test or from the demo's
    /// own endpoint, and the code path they land on is the same one.
    /// </remarks>
    /// <param name="shipmentId">B204 of the load being reported on.</param>
    /// <param name="nativeCode">A code in the far system's vocabulary.</param>
    /// <param name="occurredAt">When it happened, in local time at the location.</param>
    /// <param name="city">Where the truck was.</param>
    /// <param name="state">The state or province.</param>
    /// <param name="note">Anything worth carrying across.</param>
    /// <param name="cancellationToken">Cancels the delivery of the callback.</param>
    /// <returns>False when the load is not held here or the code means nothing to this adapter.</returns>
    public async Task<bool> RaiseStatusAsync(
        string shipmentId,
        string nativeCode,
        DateTime occurredAt,
        string city = "",
        string state = "",
        string note = "",
        CancellationToken cancellationToken = default)
    {
        if (_onStatus is not { } handler)
        {
            return false;
        }

        if (!_held.TryGetValue(shipmentId ?? string.Empty, out TmsHeldLoad? held))
        {
            return false;
        }

        if (Translate(nativeCode) is not { } status)
        {
            return false;
        }

        await handler(
            new TmsStatusCallback(
                held.TmsLoadId,
                held.ShipmentId,
                status,
                nativeCode,
                occurredAt,
                city,
                state,
                note),
            cancellationToken).ConfigureAwait(false);

        return true;
    }

    /// <summary>Forgets everything, for the demo's reset button.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _held.Clear();
            _sequence = 0;
        }
    }
}

/// <summary>A load the mock adapter is holding.</summary>
/// <param name="TmsLoadId">The identifier the far system assigned. Its key, not the board's.</param>
/// <param name="ShipmentId">B204, which is what both sides say on the phone.</param>
/// <param name="BoardLoadId">The board's own identifier for the same load.</param>
/// <param name="Scac">The carrier the load was tendered to.</param>
/// <param name="Origin">First pickup, as a city and state.</param>
/// <param name="Destination">Final delivery, as a city and state.</param>
/// <param name="StopCount">How many stops the run has.</param>
/// <param name="Sequence">Push order, so the list is stable.</param>
/// <param name="PushedAt">When it was pushed.</param>
public sealed record TmsHeldLoad(
    string TmsLoadId,
    string ShipmentId,
    Guid BoardLoadId,
    string Scac,
    string Origin,
    string Destination,
    int StopCount,
    int Sequence,
    DateTimeOffset PushedAt);
