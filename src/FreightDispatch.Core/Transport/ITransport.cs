namespace FreightDispatch.Core.Transport;

/// <summary>
/// Moves interchanges between this board and a trading partner.
/// </summary>
/// <remarks>
/// <para>Every partner connection in freight is one of about four things — a watched
/// directory, SFTP, AS2, or an HTTP endpoint belonging to a VAN — and every one of them is
/// the same two verbs: something arrived, send something back. What differs is
/// authentication, acknowledgment semantics and how you find out a delivery failed, none of
/// which the board should know about.</para>
/// <para>So this interface is deliberately small. <see cref="FileDropTransport"/> is the
/// implementation that ships, because a watched directory is what a partner's SFTP mount
/// looks like from the inside anyway. An AS2 implementation would add certificate
/// configuration and an MDN — which is a receipt for the transmission, not for the
/// document, and is not a substitute for the 997 — and an SFTP one would add a host key and
/// a retry policy. Both would implement these four members and nothing above them would
/// change.</para>
/// <para>The one thing worth insisting on: <see cref="SendAsync"/> completing means the
/// document has left. It does not mean the partner has it, and no transport can tell you
/// that. The thing that tells you the partner has it is the 997 coming back.</para>
/// </remarks>
public interface ITransport
{
    /// <summary>What this transport is, for logs and for the console: <c>file-drop</c>.</summary>
    string Name { get; }

    /// <summary>
    /// Where it is connected, in whatever form makes sense — a directory pair, a host name,
    /// an AS2 URL. Shown to an operator; never parsed.
    /// </summary>
    string Endpoint { get; }

    /// <summary>True between <see cref="StartAsync"/> and <see cref="StopAsync"/>.</summary>
    bool IsRunning { get; }

    /// <summary>
    /// Begins watching for inbound documents.
    /// </summary>
    /// <param name="handler">
    /// Called once per document that arrives. Its <see cref="InboundResult"/> decides
    /// whether the transport treats the document as dealt with.
    /// </param>
    /// <param name="cancellationToken">Cancels the start-up work, not the watching.</param>
    Task StartAsync(
        Func<InboundDocument, CancellationToken, Task<InboundResult>> handler,
        CancellationToken cancellationToken = default);

    /// <summary>Stops watching and waits for anything in flight to finish.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends one generated interchange.
    /// </summary>
    /// <param name="document">What to send.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    /// <returns>Where it went, in a form an operator can act on.</returns>
    Task<string> SendAsync(OutboundDocument document, CancellationToken cancellationToken = default);
}
