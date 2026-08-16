# The transport

**How files get in and out, and why that is behind an interface.**

Every partner connection in freight is one of about four things — a watched directory,
SFTP, AS2, or an HTTP endpoint belonging to a VAN — and every one of them is the same two
verbs: *something arrived*, *send something back*. What differs between them is
authentication, how you find out a delivery failed, and what counts as a receipt. None of
that is the board's business.

So the board does not know. It raises an event carrying a generated interchange and who it
is for, and something else decides where that goes:

```
LoadBoard ──DocumentGenerated──► TransportGateway ──► ITransport ──► a partner
          ◄─────Receive()────────                ◄────
```

## ITransport

[`src/FreightDispatch.Core/Transport/ITransport.cs`](../src/FreightDispatch.Core/Transport/ITransport.cs)

```csharp
Task StartAsync(Func<InboundDocument, CancellationToken, Task<InboundResult>> handler, ...);
Task StopAsync(CancellationToken cancellationToken = default);
Task<string> SendAsync(OutboundDocument document, CancellationToken cancellationToken = default);
```

Four members, and the shape of them is the argument. An implementation is told *here is a
file, send it* and *call me when one arrives*; it is never told what a 214 is.

One thing is worth insisting on, because it is where people get burned: **`SendAsync`
completing means the document has left. It does not mean the partner has it**, and no
transport can tell you that. AS2 gets closest with an MDN, and an MDN is a receipt for the
*transmission*, not for the document — a partner can MDN a file and then fail to process
it. The thing that tells you the partner has read your file is the 997 coming back.

### What an AS2 or SFTP implementation would add

Nothing above this interface. Specifically:

| | AS2 | SFTP |
|---|---|---|
| Configuration | certificate pair, partner AS2 ID, URL | host key, credentials, remote paths |
| On send | HTTP POST, signed and encrypted; wait for the MDN | upload to a temp name, rename |
| On receive | an HTTP endpoint, MDN back synchronously or asynchronously | poll the remote directory |
| New failure mode | MDN never arrives, or arrives with a disposition error | connection drops mid-transfer |

Both implement the same four members. `LoadBoard`, the writers and the client do not change.

## FileDropTransport

[`src/FreightDispatch.Core/Transport/FileDropTransport.cs`](../src/FreightDispatch.Core/Transport/FileDropTransport.cs)

Two directories: one a partner writes into, one this board writes into. This is not a toy —
a watched directory is exactly what an SFTP mount, a VAN's download folder and most managed
file transfer products look like from the application's side, and a large share of freight
EDI in production is a directory somebody else's process writes into.

```
edi-drop/
  in/          a partner drops tenders here
  out/         generated 997s, 214s and 210s land here
  processed/   inbound files that were dealt with
  error/       inbound files nobody could make sense of
```

### It polls, and that is on purpose

`FileSystemWatcher` is the obvious choice and the wrong one. It silently drops events when
its internal buffer overflows, it does not fire at all on a good number of network shares,
and — worst — it tells you a file *appeared*, which is not the same as a file having
finished arriving. Listing a directory once a second is unglamorous and has never lost a
tender.

### A file appearing is not a file being complete

A partner's upload shows up as a zero-byte entry and grows for however long the transfer
takes. Read it on sight and you get the first eight kilobytes of a load tender and a parse
error that blames the sender. Two defences, both of which a real integration uses:

1. A file is only read once its length and last-write time have been **unchanged for a full
   poll**. A file still being written is skipped until it settles.
2. It is then opened with `FileShare.None`. On Windows that fails outright while the writer
   still holds the handle, which turns a race into a retry.

Outbound gets the same courtesy in reverse: every file is written to a `.tmp` name and
renamed into place, because a rename within a volume is atomic and the partner's watcher
therefore never sees half a 214. `.tmp` and `.filepart` are both ignored on the way in, for
the same reason.

### File names

```
20260820-140509.606-000004055-210.edi
└─ generated ────┘ └─ ISA13 ─┘ └ ST01
```

Timestamp first so a plain directory listing is in the order the documents were generated,
which is the order they have to reach the partner in.

**ISA13 comes second because the timestamp is not enough.** Two documents can be generated
inside the same millisecond — leaving a stop emits a pair of 214s, and a delivery emits a
214 and then a 210 — and when the timestamps tie, whatever comes next decides the sort.
With the transaction set in that position, `210` sorts ahead of `214` and the invoice
appears to have been issued before the delivery notice. ISA13 ascends, so putting it there
makes the listing right by construction.

Partners do specify naming conventions and they all differ. This is a default, not a
standard.

## The three inbound outcomes

`InboundResult.Handled` is not "was it clean". These are different things and they go to
three different places:

| Outcome | 997 | File goes to |
|---|---|---|
| Loads on the board | accepted | `processed/` |
| A 204 the syntax check refused | rejected, naming the error | `processed/` |
| Text that is not an interchange at all | none is possible | `error/` |

The middle row is the one worth arguing about. The partner has their answer, so
reprocessing the file would only send the same rejection again — it has been *handled*,
even though it was refused. Only the last row needs a human.

And the last row is the only case in the whole loop where silence is correct. There is no
readable ISA, so there is no sender to answer and no control number to quote. A TA1 needs an
interchange header just as much as a 997 does.

## TransportGateway

[`src/FreightDispatch.Core/Transport/TransportGateway.cs`](../src/FreightDispatch.Core/Transport/TransportGateway.cs)

Wires the two together. Outbound documents go through a channel and a single pump rather
than being sent inline, because generating a document is fast and sending one is not, and a
dispatcher clicking a status button should not be waiting on a file handle. One reader, so
the order is preserved — a partner that receives the departure before the arrival has to
work out which one is stale.

What it deliberately does not do is retry. A demo that pretends to have a durable outbox is
worse than one that says plainly it has not got one: the gateway records the failure in its
log and moves on, and a real deployment needs the outbox in the same transaction as the
control number sequence. That is the same paragraph as the one about in-memory state in the
README, and it is the same fix.

## Running it

The drop is on by default and can be pointed anywhere:

```bash
cd src/FreightDispatch.Api
dotnet run                                    # ./edi-drop
dotnet run --FileDrop:Root=/srv/partner-x     # somebody else's mount
dotnet run --FileDrop:Enabled=false           # paste box only
```

`GET /api/transport` reports which directories are being watched and the last two hundred
things that moved. Drop `samples/204-dry-van-2-stop.edi` into `in/` and a 997 appears in
`out/` within a second or two.
