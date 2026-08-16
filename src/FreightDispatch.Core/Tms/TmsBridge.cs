using System.Collections.Concurrent;
using System.Threading.Channels;
using FreightDispatch.Core.Model;

namespace FreightDispatch.Core.Tms;

/// <summary>
/// Connects a <see cref="LoadBoard"/> to an <see cref="ITmsAdapter"/>: loads out, status back.
/// </summary>
/// <remarks>
/// The mirror image of <see cref="Transport.TransportGateway"/>. That one carries X12 to a
/// trading partner; this one carries the same loads to whatever system the customer already
/// runs, and takes status back out of it. Keeping them separate matters because they fail
/// independently — a TMS being down is not a reason to stop acknowledging tenders, and a
/// partner's SFTP being down is not a reason to stop dispatching.
/// </remarks>
public sealed class TmsBridge : IAsyncDisposable
{
    private readonly LoadBoard _board;
    private readonly ConcurrentQueue<TmsBridgeLogEntry> _log = new();
    private readonly Channel<Load> _pending;

    private CancellationTokenSource? _stopping;
    private Task? _pump;

    /// <summary>Creates a bridge. Nothing is pushed until <see cref="StartAsync"/>.</summary>
    /// <param name="board">The board to push from and apply status to.</param>
    /// <param name="adapter">The system on the other side.</param>
    public TmsBridge(LoadBoard board, ITmsAdapter adapter)
    {
        _board = board ?? throw new ArgumentNullException(nameof(board));
        Adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));

        _pending = Channel.CreateUnbounded<Load>(new UnboundedChannelOptions { SingleReader = true });
    }

    /// <summary>The adapter in use.</summary>
    public ITmsAdapter Adapter { get; }

    /// <summary>The last two hundred things that crossed the boundary, newest last.</summary>
    public IReadOnlyList<TmsBridgeLogEntry> Log => _log.ToArray();

    /// <summary>
    /// Subscribes to status callbacks and starts pushing every load that reaches the board.
    /// </summary>
    /// <remarks>
    /// The push runs on its own pump for the same reason the outbound EDI does: receiving a
    /// tender is fast and calling somebody else's API is not, and a partner's file drop
    /// should not be held open waiting on a system that is having a bad afternoon.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the subscription.</param>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_pump is null)
        {
            _stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _board.LoadTendered += OnLoadTendered;
            _pump = Task.Run(() => PumpAsync(_stopping.Token), CancellationToken.None);
        }

        await Adapter.SubscribeAsync(ApplyAsync, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Stops receiving callbacks and stops pushing.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _board.LoadTendered -= OnLoadTendered;
        _pending.Writer.TryComplete();

        await Adapter.UnsubscribeAsync(cancellationToken).ConfigureAwait(false);

        if (_pump is { } pump)
        {
            try
            {
                await pump.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Shutting down.
            }
        }

        if (_stopping is { } stopping)
        {
            await stopping.CancelAsync().ConfigureAwait(false);
            stopping.Dispose();
            _stopping = null;
        }

        _pump = null;
    }

    private void OnLoadTendered(object? sender, Load load) => _pending.Writer.TryWrite(load);

    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (Load load in _pending.Reader.ReadAllAsync(cancellationToken))
            {
                await PushAsync(load, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    /// <summary>Stops the bridge.</summary>
    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    /// <summary>
    /// Pushes a load across and records what came back.
    /// </summary>
    /// <param name="load">The load.</param>
    /// <param name="cancellationToken">Cancels the push.</param>
    /// <returns>What the far system said, refusals included.</returns>
    public async Task<TmsPushResult> PushAsync(Load load, CancellationToken cancellationToken = default)
    {
        if (load is null)
        {
            throw new ArgumentNullException(nameof(load));
        }

        TmsPushResult result = await Adapter.PushLoadAsync(load, cancellationToken).ConfigureAwait(false);

        Record(new TmsBridgeLogEntry(
            "push",
            load.ShipmentId,
            result.Accepted ? result.TmsLoadId : string.Empty,
            result.Accepted ? "accepted" : result.Message,
            result.Accepted,
            result.At));

        return result;
    }

    /// <summary>
    /// Applies one status callback to the board.
    /// </summary>
    /// <remarks>
    /// <para>The awkward case is the one that happens most: the far system reports
    /// <c>LOADED</c> while the board still has the load as tendered, because nobody clicked
    /// anything here. The board's rule is one step at a time, so a callback that jumps
    /// ahead cannot simply be applied.</para>
    /// <para>Refusing it would leave the two systems permanently out of step over a
    /// dispatcher's click. So the bridge walks the intervening states instead and emits the
    /// 214 for each, because a partner tracking this load needs the acknowledgment and the
    /// arrival as much as the loading. <b>The compromise is the timestamps:</b> the
    /// backfilled events all carry the instant the callback reported, which is not when they
    /// happened. That is visible in the event log rather than hidden, and the honest fix is
    /// for the far system to send each status as it occurs — which is what you ask for in
    /// the integration meeting and do not always get.</para>
    /// </remarks>
    private Task ApplyAsync(TmsStatusCallback callback, CancellationToken cancellationToken)
    {
        Load? load = _board.Loads.FirstOrDefault(l =>
            string.Equals(l.ShipmentId, callback.ShipmentId, StringComparison.Ordinal));

        if (load is null)
        {
            Record(new TmsBridgeLogEntry(
                "status", callback.ShipmentId, callback.TmsLoadId,
                $"{callback.NativeCode}: no load with that shipment id on the board", false,
                DateTimeOffset.UtcNow));

            return Task.CompletedTask;
        }

        if (load.Status == callback.Status)
        {
            Record(new TmsBridgeLogEntry(
                "status", callback.ShipmentId, callback.TmsLoadId,
                $"{callback.NativeCode}: already {StatusCatalog.DescribeStatus(callback.Status)}", true,
                DateTimeOffset.UtcNow));

            return Task.CompletedTask;
        }

        if (load.Status > callback.Status)
        {
            // A 214 is a statement about something that happened and cannot be un-sent, so
            // the board has no path backwards and the bridge must not invent one.
            Record(new TmsBridgeLogEntry(
                "status", callback.ShipmentId, callback.TmsLoadId,
                $"{callback.NativeCode}: load is already " +
                $"{StatusCatalog.DescribeStatus(load.Status)} — a correction is a further 214, not a rewrite",
                false,
                DateTimeOffset.UtcNow));

            return Task.CompletedTask;
        }

        int emitted = 0;
        int guard = 0;

        while (load.Status != callback.Status &&
               StatusCatalog.Next(load.Status, load.StopsRemainAfterCurrent) is { } next &&
               guard++ < 24)
        {
            emitted += _board.Advance(
                load.Id,
                next,
                callback.OccurredAt,
                "NS",
                string.IsNullOrWhiteSpace(callback.City) ? null : callback.City,
                string.IsNullOrWhiteSpace(callback.State) ? null : callback.State,
                callback.Note).Count;
        }

        Record(new TmsBridgeLogEntry(
            "status",
            callback.ShipmentId,
            callback.TmsLoadId,
            $"{callback.NativeCode} → {StatusCatalog.DescribeStatus(callback.Status)}, {emitted} × 214",
            true,
            DateTimeOffset.UtcNow));

        return Task.CompletedTask;
    }

    private void Record(TmsBridgeLogEntry entry)
    {
        _log.Enqueue(entry);

        while (_log.Count > 200 && _log.TryDequeue(out _))
        {
            // Keep the tail only.
        }
    }
}

/// <summary>One line of the bridge's log.</summary>
/// <param name="Kind"><c>push</c> or <c>status</c>.</param>
/// <param name="ShipmentId">B204 of the load.</param>
/// <param name="TmsLoadId">The far system's identifier, when it has assigned one.</param>
/// <param name="Summary">What happened, in a sentence.</param>
/// <param name="Ok">False when the far system refused or the callback could not be applied.</param>
/// <param name="At">When.</param>
public sealed record TmsBridgeLogEntry(
    string Kind,
    string ShipmentId,
    string TmsLoadId,
    string Summary,
    bool Ok,
    DateTimeOffset At);
