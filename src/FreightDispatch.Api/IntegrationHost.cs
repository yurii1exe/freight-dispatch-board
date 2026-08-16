using FreightDispatch.Core;
using FreightDispatch.Core.Model;
using FreightDispatch.Core.Tms;
using FreightDispatch.Core.Transport;

namespace FreightDispatch.Api;

/// <summary>
/// Starts and stops the two integration seams with the process.
/// </summary>
/// <remarks>
/// <para>Both are started here rather than at the first request because a file drop that
/// only begins watching once somebody opens the web page is not an integration, it is a
/// button. A partner's tenders arrive at four in the morning whether or not anyone has the
/// board open.</para>
/// <para>Ordering matters on the way in: the board is seeded before the host starts, so the
/// twelve demonstration loads do not put eighty interchanges into the outbound directory on
/// every restart. See <c>LoadBoard.WithoutSending</c>.</para>
/// </remarks>
public sealed class IntegrationHost : IHostedService
{
    private readonly TransportGateway? _gateway;
    private readonly TmsBridge _tms;
    private readonly LoadBoard _board;
    private readonly ILogger<IntegrationHost> _logger;

    /// <summary>Creates the host.</summary>
    /// <param name="board">The board, so that loads seeded before start-up are not missed.</param>
    /// <param name="tms">The TMS bridge, which is always on — the adapter is a mock.</param>
    /// <param name="logger">Where start-up is reported.</param>
    /// <param name="gateway">The transport gateway, absent when the file drop is switched off.</param>
    public IntegrationHost(
        LoadBoard board,
        TmsBridge tms,
        ILogger<IntegrationHost> logger,
        TransportGateway? gateway = null)
    {
        _board = board;
        _tms = tms;
        _logger = logger;
        _gateway = gateway;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _tms.StartAsync(cancellationToken).ConfigureAwait(false);

        // The board was seeded before this ran, so those loads never raised the event the
        // bridge listens for. A system that only hears about loads tendered after it
        // connected is a system that is permanently one restart behind.
        foreach (Load load in _board.Loads)
        {
            await _tms.PushAsync(load, cancellationToken).ConfigureAwait(false);
        }

        if (_gateway is null)
        {
            _logger.LogInformation("File drop is disabled. Tenders can still be pasted into the board.");
            return;
        }

        await _gateway.StartAsync(cancellationToken).ConfigureAwait(false);

        var transport = (FileDropTransport)_gateway.Transport;

        _logger.LogInformation(
            "Watching {Inbound} for tenders. Generated 997s, 214s and 210s land in {Outbound}.",
            transport.InboundDirectory,
            transport.OutboundDirectory);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_gateway is not null)
        {
            await _gateway.StopAsync(cancellationToken).ConfigureAwait(false);
        }

        await _tms.StopAsync(cancellationToken).ConfigureAwait(false);
    }
}
