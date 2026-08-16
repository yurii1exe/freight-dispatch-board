# The TMS boundary

**An interface and a mock. There is no connector to any commercial product here, and there
is not going to be one.**

## Why an interface at all

Nobody replaces their TMS. A freight EDI integration is almost always a piece bolted onto
one that is already there and already paid for, and the work is two verbs: **push a load
into it**, and **receive status back out of it**.

Everything else differs. One speaks REST, one speaks SOAP, one wants you to insert into a
staging table and watch a flag column. One posts webhooks, one expects you to poll every
ninety seconds, one emails a CSV. Every one of them has its own status vocabulary and none
of them is X12's element 1650.

That is the case for a boundary, and it is the whole of it:

```csharp
Task<TmsPushResult> PushLoadAsync(Load load, CancellationToken cancellationToken = default);
Task SubscribeAsync(Func<TmsStatusCallback, CancellationToken, Task> onStatus, ...);
Task UnsubscribeAsync(CancellationToken cancellationToken = default);
```

[`src/FreightDispatch.Core/Tms/ITmsAdapter.cs`](../src/FreightDispatch.Core/Tms/ITmsAdapter.cs)

## The adapter owns the vocabulary translation

This is the part that matters and the part that gets built wrong.

`TmsStatusCallback` carries a `LoadStatus` — **the board's own vocabulary, already
translated** — and the far system's code rides alongside it in `NativeCode` purely so a
human debugging a mapping can see both sides at once. Nothing switches on it.

If that translation lives anywhere but the adapter it ends up duplicated in the board, in
the 214 writer and in the UI, and within a release the three of them disagree. Putting it
in the adapter means adding a second system is one class, and means the board can be read
without knowing which system is attached.

## MockTmsAdapter

[`src/FreightDispatch.Core/Tms/MockTmsAdapter.cs`](../src/FreightDispatch.Core/Tms/MockTmsAdapter.cs)

Its vocabulary is invented, and deliberately neither X12's nor the board's, because every
real one is a third thing:

| It says | The board means | Which emits |
|---|---|---|
| `COVERED` | Dispatched | `XB` |
| `AT_ORIGIN` | At shipper | `X3` |
| `LOADED` | Loaded | `CP` |
| `ROLLING` | In transit | `AF` |
| `AT_DEST` | At consignee | `X1` |
| `EMPTY` | Delivered | `D1` |

It assigns its own identifiers (`TMS-0001`) rather than echoing the board's, because a real
system's key is its own and every later call needs it.

**And it refuses things.** A mock that only ever succeeds teaches nobody anything. A load
with no B204 is refused because there is nothing to key on, and so is a second push of one
already open — duplicate load numbers are the commonest reason a real push comes back
rejected. A refusal is an answer, not an exception: `TmsPushResult.Accepted` is `false` and
a dispatcher gets told, which is what has to happen when a load is rejected for a customer
being on credit hold.

## TmsBridge

[`src/FreightDispatch.Core/Tms/TmsBridge.cs`](../src/FreightDispatch.Core/Tms/TmsBridge.cs)

The mirror image of `TransportGateway`. That one carries X12 to a trading partner; this one
carries the same loads to the customer's own system and takes status back. They are separate
because they fail independently — a TMS being down is not a reason to stop acknowledging
tenders, and a partner's SFTP being down is not a reason to stop dispatching.

Loads are pushed off the board's `LoadTendered` event, through a channel and a pump, for the
same reason outbound EDI is: receiving a tender is fast and calling somebody else's API is
not, and a partner's file drop should not be held open waiting on a system having a bad
afternoon.

### The awkward case, which is the common one

The far system reports `LOADED` while the board still has the load as tendered, because
nobody clicked anything here. The board's rule is one step at a time, so the callback cannot
simply be applied.

Refusing it would leave the two systems permanently out of step over a dispatcher's click.
So the bridge **walks the intervening states** and emits the 214 for each, because a partner
tracking this load needs the acknowledgment and the arrival as much as the loading.

The compromise is the timestamps: the backfilled events all carry the instant the callback
reported, which is not when they happened. That is visible in the event log rather than
hidden, and the honest fix is for the far system to send each status as it occurs — which is
what you ask for in the integration meeting and do not always get.

Going *backwards* is refused outright. A 214 is a statement about something that happened
and cannot be un-sent; the correction is a further 214, not a rewrite.

## Trying it

```bash
curl localhost:5000/api/tms

curl -X POST localhost:5000/api/tms/callback \
  -H 'content-type: application/json' \
  -d '{"shipmentId":"LD10041872","code":"LOADED","city":"JOLIET","state":"IL"}'
```

The endpoint stands in for the webhook a real adapter would receive. Three 214s come back —
`XB`, `X3`, `CP` — and land in the outbound drop directory like any others.

## Why there is no real connector

Functional equivalence with a commercial product is a different kind of risk from copied
code, and it is not one worth taking to make a portfolio piece look bigger. The interesting
engineering is the boundary, and the boundary is here in full. A connector would be
configuration and somebody else's SDK.
