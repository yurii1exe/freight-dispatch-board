using FreightDispatch.Core.Model;

namespace FreightDispatch.Core.Tms;

/// <summary>
/// The boundary between this board and whatever transportation management system a customer
/// already runs.
/// </summary>
/// <remarks>
/// <para>Nobody replaces their TMS. A freight EDI integration is almost always a piece
/// bolted onto one that is already there and already paid for, and the work is two verbs:
/// <b>push a load into it</b>, and <b>receive status back out of it</b>. Everything else —
/// how it authenticates, whether it speaks REST or SOAP or a database view, whether status
/// arrives as a webhook or has to be polled for — is behind this interface, and is the part
/// that differs between one system and the next.</para>
/// <para><b>The adapter owns the vocabulary translation, and that is the whole point.</b>
/// Every one of these systems has its own status codes, and none of them is X12's element
/// 1650. If that translation lives anywhere but the adapter it ends up duplicated in the
/// board, in the 214 writer and in the UI, and the three of them drift. So
/// <see cref="TmsStatusCallback"/> carries a <see cref="LoadStatus"/> — the board's own
/// vocabulary, already translated — and the foreign code is carried alongside it only so a
/// human can see what it was.</para>
/// <para>This repository ships an interface and <see cref="MockTmsAdapter"/>. It ships no
/// connector to any commercial product, deliberately.</para>
/// </remarks>
public interface ITmsAdapter
{
    /// <summary>What this adapter connects to, for logs and for the console.</summary>
    string Name { get; }

    /// <summary>True once <see cref="SubscribeAsync"/> has been called and not yet stopped.</summary>
    bool IsConnected { get; }

    /// <summary>
    /// Pushes a load in.
    /// </summary>
    /// <param name="load">The load, in this board's vocabulary. The adapter maps it.</param>
    /// <param name="cancellationToken">Cancels the push.</param>
    /// <returns>
    /// What the system said. A refusal is a normal outcome, not an exception: a duplicate
    /// load number, a carrier that is not set up, a customer on credit hold. All three are
    /// answers, and all three need to reach a dispatcher rather than a log file.
    /// </returns>
    Task<TmsPushResult> PushLoadAsync(Load load, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts receiving status callbacks.
    /// </summary>
    /// <param name="onStatus">
    /// Called for each status the system reports, already translated into board vocabulary.
    /// </param>
    /// <param name="cancellationToken">Cancels the subscription.</param>
    Task SubscribeAsync(
        Func<TmsStatusCallback, CancellationToken, Task> onStatus,
        CancellationToken cancellationToken = default);

    /// <summary>Stops receiving callbacks.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    Task UnsubscribeAsync(CancellationToken cancellationToken = default);
}

/// <summary>What a system said when a load was pushed into it.</summary>
/// <param name="Accepted">False when it refused, which is a normal outcome.</param>
/// <param name="TmsLoadId">The identifier it assigned. Its key, not ours, and needed for every later call.</param>
/// <param name="Message">Why, when it refused. Empty when it did not.</param>
/// <param name="At">When the push was answered.</param>
public sealed record TmsPushResult(bool Accepted, string TmsLoadId, string Message, DateTimeOffset At)
{
    /// <summary>A successful push.</summary>
    /// <param name="tmsLoadId">The identifier the system assigned.</param>
    public static TmsPushResult Ok(string tmsLoadId) =>
        new(true, tmsLoadId, string.Empty, DateTimeOffset.UtcNow);

    /// <summary>A refused push.</summary>
    /// <param name="message">What the system said.</param>
    public static TmsPushResult Refused(string message) =>
        new(false, string.Empty, message, DateTimeOffset.UtcNow);
}

/// <summary>
/// A status the far system reported about a load it is holding.
/// </summary>
/// <param name="TmsLoadId">Its identifier for the load.</param>
/// <param name="ShipmentId">B204, so the board can find the load without keeping a second index.</param>
/// <param name="Status">
/// The status in board vocabulary. The adapter has already translated it — see the remarks
/// on <see cref="ITmsAdapter"/> for why that is the adapter's job and not the board's.
/// </param>
/// <param name="NativeCode">
/// The code the far system actually used, carried through untranslated so that a human
/// debugging a mapping can see both sides at once. Never switched on.
/// </param>
/// <param name="OccurredAt">When it happened, in local time at the location.</param>
/// <param name="City">Where the truck was, when the system says.</param>
/// <param name="State">The state or province.</param>
/// <param name="Note">Anything the system attached. Stays on the board; not sent.</param>
public sealed record TmsStatusCallback(
    string TmsLoadId,
    string ShipmentId,
    LoadStatus Status,
    string NativeCode,
    DateTime OccurredAt,
    string City = "",
    string State = "",
    string Note = "");
