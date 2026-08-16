using System.Globalization;

namespace FreightDispatch.Core.Transport;

/// <summary>
/// A generated interchange on its way out, with the routing facts a transport needs and
/// nothing else.
/// </summary>
/// <remarks>
/// This is the whole contract between the board and whatever moves files. A transport is
/// not told what a 214 is, and the board is not told whether the file left over AS2, SFTP
/// or a directory on the same disk. That separation is the point: the interesting part of a
/// freight integration is almost never the protocol, and a design where the two are tangled
/// makes changing the protocol a change to the business logic.
/// </remarks>
/// <param name="TransactionSet">ST01 of what is inside: <c>997</c>, <c>214</c> or <c>210</c>.</param>
/// <param name="InterchangeControlNumber">ISA13, which is what either side quotes when a file goes missing.</param>
/// <param name="SenderId">ISA06 of the generated interchange.</param>
/// <param name="ReceiverId">ISA08 — who this is going to. A routing key for a real transport.</param>
/// <param name="Edi">The complete interchange text.</param>
/// <param name="LoadId">The board load this came from, when there is one.</param>
/// <param name="ShipmentId">B204 of the load, for anything that has to be read by a human.</param>
/// <param name="GeneratedAt">When the board produced it.</param>
public sealed record OutboundDocument(
    string TransactionSet,
    string InterchangeControlNumber,
    string SenderId,
    string ReceiverId,
    string Edi,
    Guid? LoadId,
    string ShipmentId,
    DateTimeOffset GeneratedAt)
{
    /// <summary>
    /// A file name a person can sort and search.
    /// </summary>
    /// <remarks>
    /// <para>Timestamp, then ISA13, then the transaction set. Partners do specify naming
    /// conventions and they all differ, so this is a default rather than a standard — but
    /// the order of the three parts is not arbitrary.</para>
    /// <para>The timestamp is first so that a plain directory listing is in the order the
    /// documents were generated, which is the order they have to reach the partner in.
    /// <b>ISA13 comes second because the timestamp is not enough.</b> Two documents can be
    /// generated inside the same millisecond — leaving a stop emits a pair of 214s, and a
    /// delivery emits a 214 and then a 210 — and when the timestamps tie, whatever comes
    /// next decides the sort. With the transaction set in that position, <c>210</c> sorts
    /// ahead of <c>214</c> and the invoice appears to have been issued before the delivery
    /// notice. ISA13 ascends, so putting it there makes the listing right by construction.</para>
    /// </remarks>
    public string SuggestedFileName =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{GeneratedAt.UtcDateTime:yyyyMMdd-HHmmss.fff}-{InterchangeControlNumber}-{TransactionSet}.edi");
}

/// <summary>A file that arrived, before anybody has tried to parse it.</summary>
/// <param name="Source">Where it came from — a path, a message id, whatever the transport can say.</param>
/// <param name="Edi">The raw text, exactly as received.</param>
/// <param name="ReceivedAt">When the transport picked it up.</param>
public sealed record InboundDocument(string Source, string Edi, DateTimeOffset ReceivedAt);

/// <summary>
/// What the board made of an inbound document, so the transport can decide where to put the
/// file it came from.
/// </summary>
/// <remarks>
/// <see cref="Handled"/> is not "was it clean". A 204 that was rejected by a 997 has still
/// been handled — the partner has their answer and the file must not be reprocessed. Only a
/// file nobody could make sense of at all is unhandled, and that is the one a human has to
/// look at.
/// </remarks>
/// <param name="Handled">True when the board processed the document and answered it.</param>
/// <param name="Summary">One line for the log: what it was and what happened to it.</param>
public sealed record InboundResult(bool Handled, string Summary)
{
    /// <summary>A document the board could not make sense of.</summary>
    /// <param name="reason">Why. This ends up in the log and, in service, in somebody's inbox.</param>
    public static InboundResult Failed(string reason) => new(false, reason);
}
