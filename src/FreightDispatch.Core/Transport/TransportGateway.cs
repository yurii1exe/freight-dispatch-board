using System.Collections.Concurrent;
using System.Threading.Channels;
using EdiX12.Core;
using FreightDispatch.Core.Model;

namespace FreightDispatch.Core.Transport;

/// <summary>
/// Wires a <see cref="LoadBoard"/> to an <see cref="ITransport"/>, in both directions.
/// </summary>
/// <remarks>
/// <para>Inbound: whatever the transport picks up goes to <see cref="LoadBoard.Receive"/>,
/// which parses it, puts any loads on the board and produces the 997. Outbound: every
/// interchange the board generates — the 997 for a tender, a 214 for each status change,
/// the 210 on delivery — is queued here and sent.</para>
/// <para>The queue is the reason this class exists rather than the board simply calling the
/// transport. Generating a document is synchronous and fast; sending one is neither, and a
/// dispatcher clicking a status button should not be waiting on a file handle. So the board
/// raises an event, the gateway takes it into a channel, and a single pump drains it in
/// order. In-order matters: a partner that receives the departure before the arrival has to
/// work out which one is stale.</para>
/// <para>What this deliberately does not do is retry. A demo that pretends to have a
/// durable outbox is worse than one that says plainly it has not got one — see the log
/// below, which records the failure and moves on.</para>
/// </remarks>
public sealed class TransportGateway : IAsyncDisposable
{
    private readonly LoadBoard _board;
    private readonly Channel<OutboundDocument> _outbound;
    private readonly ConcurrentQueue<TransportLogEntry> _log = new();

    private CancellationTokenSource? _stopping;
    private Task? _pump;
    private int _queued;

    /// <summary>Creates a gateway. Nothing moves until <see cref="StartAsync"/> is called.</summary>
    /// <param name="board">The board to feed and to listen to.</param>
    /// <param name="transport">The transport to move files over.</param>
    public TransportGateway(LoadBoard board, ITransport transport)
    {
        _board = board ?? throw new ArgumentNullException(nameof(board));
        Transport = transport ?? throw new ArgumentNullException(nameof(transport));

        _outbound = Channel.CreateUnbounded<OutboundDocument>(new UnboundedChannelOptions
        {
            SingleReader = true,
        });
    }

    /// <summary>The transport in use.</summary>
    public ITransport Transport { get; }

    /// <summary>
    /// The last two hundred things that moved, newest last. Enough to show an operator that
    /// the loop is alive without pretending to be an audit trail.
    /// </summary>
    public IReadOnlyList<TransportLogEntry> Log => _log.ToArray();

    /// <summary>Starts the outbound pump and the inbound watcher.</summary>
    /// <param name="cancellationToken">Cancels start-up.</param>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_pump is not null)
        {
            return;
        }

        _stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _board.DocumentGenerated += OnDocumentGenerated;
        _pump = Task.Run(() => PumpAsync(_stopping.Token), CancellationToken.None);

        await Transport.StartAsync(HandleInboundAsync, cancellationToken).ConfigureAwait(false);

        Record(new TransportLogEntry(
            TransportDirection.System,
            string.Empty,
            Transport.Endpoint,
            $"{Transport.Name} started",
            true,
            DateTimeOffset.UtcNow));
    }

    /// <summary>Stops the watcher and drains what is already queued.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _board.DocumentGenerated -= OnDocumentGenerated;

        await Transport.StopAsync(cancellationToken).ConfigureAwait(false);

        _outbound.Writer.TryComplete();

        if (_pump is { } pump)
        {
            try
            {
                await pump.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Shutting down. Whatever is still queued is lost, and that is exactly the
                // gap a durable outbox would close.
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

    /// <summary>Stops the gateway.</summary>
    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    /// <summary>
    /// Waits until everything queued so far has been sent.
    /// </summary>
    /// <remarks>
    /// Only useful to a test or to a shutdown path. Production code has no business knowing
    /// when the outbox is empty, because in production it never is for long.
    /// </remarks>
    /// <param name="timeout">How long to wait before giving up.</param>
    /// <returns>True when the queue drained inside the timeout.</returns>
    public async Task<bool> WaitForIdleAsync(TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (Volatile.Read(ref _queued) == 0)
            {
                return true;
            }

            await Task.Delay(10).ConfigureAwait(false);
        }

        return Volatile.Read(ref _queued) == 0;
    }

    /// <summary>
    /// Takes one inbound file and gives it to the board.
    /// </summary>
    /// <remarks>
    /// The three outcomes are all different and all have to be distinguished, because they
    /// go to three different places:
    /// <list type="bullet">
    /// <item><description>Loads on the board and a 997 back — handled.</description></item>
    /// <item><description>No loads, but a 997 back saying why — still handled. The partner
    /// has their answer and reprocessing the file would only send it again.</description></item>
    /// <item><description>The text could not be tokenized at all, so not even a 997 is
    /// possible — not handled, and a human has to look at it.</description></item>
    /// </list>
    /// </remarks>
    private async Task<InboundResult> HandleInboundAsync(InboundDocument document, CancellationToken cancellationToken)
    {
        await Task.Yield();

        string source = Path.GetFileName(document.Source);

        try
        {
            TenderReceipt receipt = _board.Receive(document.Edi);

            string verdict = receipt.Acknowledgment is { } ack
                ? $"997 {ack.Verdict} ({ack.VerdictLabel})"
                : "no 997 possible";

            string summary = receipt.Loads.Count > 0
                ? $"{receipt.Loads.Count} load(s) on the board · {verdict}"
                : $"no loads · {verdict}";

            Record(new TransportLogEntry(
                TransportDirection.Inbound, "204", source, summary, true, document.ReceivedAt));

            return new InboundResult(true, summary);
        }
        catch (X12ParseException ex)
        {
            // Not even the ISA could be read, so there is no sender to acknowledge to and
            // no control number to quote. This is the one case where silence is correct and
            // the file goes to a human.
            string summary = $"unreadable interchange — {ex.Message}";

            Record(new TransportLogEntry(
                TransportDirection.Inbound, string.Empty, source, summary, false, document.ReceivedAt));

            return InboundResult.Failed(summary);
        }
    }

    private void OnDocumentGenerated(object? sender, OutboundDocument document)
    {
        Interlocked.Increment(ref _queued);

        if (!_outbound.Writer.TryWrite(document))
        {
            Interlocked.Decrement(ref _queued);
        }
    }

    /// <summary>Drains the outbound queue, one document at a time, in order.</summary>
    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (OutboundDocument document in _outbound.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    string destination = await Transport
                        .SendAsync(document, cancellationToken)
                        .ConfigureAwait(false);

                    Record(new TransportLogEntry(
                        TransportDirection.Outbound,
                        document.TransactionSet,
                        Path.GetFileName(destination),
                        $"{document.TransactionSet} for {document.ShipmentId} → {document.ReceiverId}",
                        true,
                        document.GeneratedAt));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Record(new TransportLogEntry(
                        TransportDirection.Outbound,
                        document.TransactionSet,
                        document.SuggestedFileName,
                        $"send failed — {ex.Message}",
                        false,
                        document.GeneratedAt));
                }
                finally
                {
                    Interlocked.Decrement(ref _queued);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    private void Record(TransportLogEntry entry)
    {
        _log.Enqueue(entry);

        while (_log.Count > 200 && _log.TryDequeue(out _))
        {
            // Keep the tail only.
        }
    }
}

/// <summary>Which way a logged document was going.</summary>
public enum TransportDirection
{
    /// <summary>The gateway itself: started, stopped.</summary>
    System = 0,

    /// <summary>A file that arrived.</summary>
    Inbound = 1,

    /// <summary>A file that was sent.</summary>
    Outbound = 2,
}

/// <summary>One line of the gateway's log.</summary>
/// <param name="Direction">Which way it was going.</param>
/// <param name="TransactionSet">ST01, when it is known.</param>
/// <param name="File">The file name, without the directory.</param>
/// <param name="Summary">What happened, in a sentence.</param>
/// <param name="Ok">False when something failed and somebody has to look.</param>
/// <param name="At">When.</param>
public sealed record TransportLogEntry(
    TransportDirection Direction,
    string TransactionSet,
    string File,
    string Summary,
    bool Ok,
    DateTimeOffset At);
