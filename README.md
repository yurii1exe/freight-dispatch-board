# Freight Dispatch Board

[![CI](https://github.com/yurii1exe/freight-dispatch-board/actions/workflows/ci.yml/badge.svg)](https://github.com/yurii1exe/freight-dispatch-board/actions/workflows/ci.yml)

**Load tender in, acknowledgment straight back, dispatch board, status updates out, invoice
at the end.** An EDI 204 arrives in a watched directory, is answered with a 997 within
seconds, becomes a row a dispatcher can work, generates a 214 for every status change on
that row, and closes with a 210 when the freight is delivered.

That loop is the core of every freight brokerage in the country, and it is normally hidden
inside a TMS costing five figures a year. This is the smallest honest version of it: .NET 8
minimal API, Angular, and a screen you can read.

A 204 pasted in, the 997 that answers it, the load landing on the board, six status changes
walking it through a four-stop run, and the invoice at the end — with the generated file
alongside at every step:

![204 in, 997 back, board, 214s out, 210 at the end](docs/board-demo.gif)

And the board itself — twelve loads, one of them open, showing the 997 it was answered with,
its stops, the 214s already sent against it and the generated file underneath. The console
under the panel has one tab per document in the load's life:

![The board](docs/board.png)

## The loop

```
204 load tender ──► parse ──► 997 functional acknowledgment ──► back within seconds
                      │
                      ▼
                  board row
                      │
           dispatcher moves the load
                      │
                      ▼
        214 status message ──► parsed straight back
                      │
                 delivered
                      │
                      ▼
        210 freight invoice ──► the loop closes at the money
```

Both ends matter more than they look. The 997 is the first thing a real trading partner
expects and the first thing they complain about not getting; the 210 is the only document in
the whole exchange that ends up in somebody's accounts payable system.

**Every interchange this generates is immediately re-parsed by the same library that reads
inbound files, and its envelope validated** — the 997 and the 210 as well as the 214. The
panel says `✓ re-parsed clean` or lists what is wrong. A generator that has never been read
by a parser is a generator that works until the first partner tries it.

## What the board shows

| Board status | X12 element 1650 | Meaning on the wire |
|---|---|---|
| Tendered | — | Nothing sent. The 204 arrived, was acknowledged, and nobody has acted on it |
| Dispatched | `XB` | Shipment Acknowledged |
| At shipper | `X3` | Arrived at Pickup Location |
| Loaded | `CP` | Completed Loading at Pickup Location |
| In transit | `AF` | Carrier Departed Pickup Location with Shipment |
| At consignee | `X1` | Arrived at Delivery Location |
| Delivered | `D1` | Completed Unloading at Delivery Location — and the 210 |

Loads move forward one step at a time. There is no path back, because a 214 is a statement
about something that happened and you cannot un-send it — the correction is a further 214,
not a rewrite.

**The codes depend on the stop, not only on the state.** Arriving somewhere is `X3` at a
pickup and `X1` at a delivery; finishing there is `CP` against `D1`; leaving is `AF` against
`CD`. So the table above is the two-stop case, and a load with more stops than that cycles.

Partners do vary. Some want `AF` for departure and nothing for loading, some want `X6` pings
every four hours in transit, some reject `XB` outright because they treat the 990 as the
acknowledgment. That variation is why the mapping is a table in `StatusCatalog` and not a
switch statement buried in the 214 writer.

## Running it

**Clone with `--recurse-submodules`.** X12 envelope parsing comes from
[edi-x12-toolkit](https://github.com/yurii1exe/edi-x12-toolkit), which is not on NuGet yet and
is pinned here as a submodule under `external/`:

```bash
git clone --recurse-submodules https://github.com/yurii1exe/freight-dispatch-board.git
cd freight-dispatch-board
dotnet build -c Release
dotnet test  -c Release      # 106 tests
```

Already cloned without it? `git submodule update --init --recursive`. That is also exactly
what the build tells you to run if the submodule is missing — it fails with one line naming
the command, not a wall of "the type or namespace `EdiX12` could not be found". When the
package is published this becomes a `PackageReference` and the submodule goes away; the steps
are written down in [docs/edi-toolkit-dependency.md](docs/edi-toolkit-dependency.md).

Then:

```bash
# API + the compiled client on one origin
cd src/FreightDispatch.Api
dotnet run
# http://localhost:5000

# or, for client work, the Angular dev server proxying to the API
cd web
npm install
npm start          # http://localhost:4200, /api proxied to :5199
```

The board seeds itself on startup with four sample tenders and eight invented lanes, moved
to a spread of statuses. Everything arrives through the same `Receive()` path as a file
dropped by a partner, so the seed is also a smoke test of the reader every time the process
starts. Seeding runs with sending suppressed — replaying eighty interchanges at a partner on
every restart would be a bug in service and is noise in a demo.

To watch the whole thing run for real, drop a file:

```bash
cp samples/204-dry-van-2-stop.edi src/FreightDispatch.Api/edi-drop/in/
ls   src/FreightDispatch.Api/edi-drop/out/     # the 997, seconds later
```

```bash
cd web && npm ci && npm run build      # writes to src/FreightDispatch.Api/wwwroot
```

The .NET 8 SDK is the minimum and it is enough: no `global.json` pins anything newer, and the
solution is in the classic `.sln` format rather than `.slnx`, which needs a 9.0.200 SDK to
open. Node 20.19+, 22.12+ or 24 for the client (Angular 21). `Directory.Build.props` sets
`RollForward=Major` so the `net8.0` app also starts on a machine that only has a 10.x runtime.

## Where the files come from and go

A watched directory in, a watched directory out — and behind an interface, so AS2 or SFTP
slot in without anything above them changing. Full detail in
[docs/transport.md](docs/transport.md).

```
edi-drop/
  in/          a partner drops tenders here
  out/         generated 997s, 214s and 210s land here
  processed/   inbound files that were dealt with
  error/       inbound files nobody could make sense of
```

Two things about reading a directory that are not obvious until the first time a partner
sends you half a file:

1. **It polls rather than using `FileSystemWatcher`.** The watcher silently drops events when
   its buffer overflows, does not fire at all on a good number of network shares, and tells
   you a file *appeared* — which is not the same as a file having finished arriving.
2. **A file appearing is not a file being complete.** An upload shows up as a zero-byte entry
   and grows. So a file is only read once its length and last-write time have been unchanged
   for a full poll, and it is then opened with `FileShare.None` so a writer still holding the
   handle turns a race into a retry. Outbound files get the same courtesy in reverse: written
   to a `.tmp` name and renamed into place, because a rename within a volume is atomic and
   the partner's watcher never sees half a 214.

There is also a generic `ITmsAdapter` with a mock implementation — push a load, receive
status callbacks — which is the other boundary a real integration lives on. The adapter owns
the vocabulary translation, so the board never sees a status code it cannot act on. See
[docs/tms-adapter.md](docs/tms-adapter.md). There is no connector to any commercial TMS here
and there is not going to be one.

## What of the 204 it reads

The 204 body is a header area followed by a repeating S5 stop-off loop. There is no explicit
loop terminator in X12 — a loop ends when the next loop's trigger segment appears, or when
the transaction set does — so the reader is a small state machine: everything before the
first S5 belongs to the load, everything after belongs to the stop most recently opened.

| Segment | Read as |
|---|---|
| `B2` | SCAC (B202), shipment identification number (B204), method of payment (B206) |
| `B2A` | Transaction set purpose (00 original, 04 change, 01 cancellation) |
| `L11` | Reference numbers, scoped to the load or to the open stop |
| `N7` | Equipment: trailer (N702), description code (N711), temperature (N713), length (N715) |
| `L3` | Total weight and qualifier |
| `S5` | Stop sequence, reason code, weight, units |
| `G62` | The stop's window — see below |
| `N1`/`N3`/`N4`/`G61` | Party, address, contact for the open loop |
| `L5` | Lading description |
| `OID` | Purchase order (OID02), folded into the stop's references |
| `NTE` | Notes, scoped to the load or to the open stop |

Anything else is ignored rather than rejected. A tender carrying thirty segments this reader
has no use for is still a tender, and refusing it would be the wrong answer to "the shipper
added a segment".

### Windows are a pair of segments, not a field

A 204 does not carry "the appointment". It carries G62 segments whose qualifiers say which
end of the window each one is:

```
pickup     G62*37  Ship Not Before Date        G62*38  Ship Not Later Than Date
delivery   G62*53  Deliver Not Before Date     G62*54  Deliver No Later Than Date
```

A single `G62*10` with no partner is an open-ended request, not a defect, and the board shows
it as such instead of inventing a closing time nobody agreed to. The times carry no zone —
X12 has none, and element 623 (`LT`) says the sender meant local time at the stop — so they
are held and displayed as wall clock values and never converted.

## The 997 that goes back

Six segments acknowledges a clean file, and it is the message partners chase you for:

```
ST*997*4001~
AK1*SM*4417*005010~      the group being acknowledged: its GS01, GS06, GS08
AK2*204*0001~            one per transaction set in it: ST01, ST02
AK5*A~                   its verdict, plus up to five element 718 error codes
AK9*A*1*1*1~             the group's verdict, declared / received / accepted
SE*6*4001~
```

Three things about it are worth more than the segment layout.

**It acknowledges functional groups, not interchanges and not business content.** GS01 of the
997 itself is `FA`; the code of the group being acknowledged goes in AK101. Putting the
acknowledged group's code in GS01 produces a file the partner routes into its load tender
application.

**A clean 997 does not mean the load was accepted.** It means the syntax survived. Accepting
or declining the load is a 990, and treating one as the other is how a broker ends up
believing a truck is covered.

**Interchange-level defects are outside its scope entirely.** `samples/204-bad-se-count.edi`
has two problems: SE01 declares 21 segments where there are 22, and IEA02 does not echo
ISA13. The 997 can only report the first:

```
AK5*R*4~     element 718 code 4 — number of included segments does not match actual count
AK9*R*1*1*0~ nothing in the group was accepted
```

The IEA02 problem needs a **TA1**, which rides in the ISA/IEA envelope rather than in a
functional group. The board records it and says so rather than dropping it, because a sender
whose IEA02 is wrong will otherwise get a clean 997 and conclude their file was perfect.

### Why `E` only ever appears at the group level

Every element 718 code is a structural defect. A document whose declared segment count is
wrong has not been read correctly and cannot be acted on, so at transaction level the verdict
is `A` or `R` and never "accepted but errors were noted". `E` belongs to content problems
reported through AK3/AK4 — a code value outside the agreed list, a date that is not a date —
which an envelope validator has no way to find.

Where it genuinely does arise is one level up: a `GE01` declaring two transaction sets in a
group containing one. The envelope is wrong, the documents inside it are perfectly usable,
and the answer is `AK9*E*2*1*1*5`. A mixed group — one good 204 and one transaction set this
board does not read — is `P`, partially accepted.

### A rejected tender still lands on the board

This is a decision, not an oversight. A translator's job is to reject a defective document; a
dispatch board's job is to move freight, and those are not the same job. The partner is told
inside seconds, and meanwhile there is a real truck expected at a real dock tomorrow morning,
tendered by somebody who will resend a corrected file. Hiding the row would leave the
dispatcher with nothing to work and no idea why.

So the row appears, marked `997 R` on the grid and in the detail panel. That is the thing a
real operation escalates on.

## What the 214 it writes looks like

5010 puts the N1 party loop in the **detail** area, inside the LX loop and after the
AT7/MS1/MS2 group. Putting it in the heading next to B10 is the intuitive order and the
wrong one.

```
ISA*00*          *00*          *ZZ*DEMOCARRIER    *ZZ*DEMOBROKER     *260815*1540*^*00501*000004070*0*T*:~
GS*QM*DEMOCARRIER*DEMOBROKER*20260815*1540*4070*X*005010~
ST*214*4070~
B10*LD10041972*LD10041972*DEMO~          B1002 is what the partner matches on
LX*1~
L11*LD10041972*OQ~                       the tender's references, echoed back
L11*BOL8842484*BM~
AT7*AF*NS***20260815*1540*LT~            the status itself
MS1*NASHVILLE*TN*US~                     where the truck was
N1*SH*CUMBERLAND PRECAST*93*CP-NAS~
N3*3900 CENTRAL PIKE~
N4*NASHVILLE*TN*37214*US~
...
SE*16*4070~
GE*1*4070~
IEA*1*000004070~
```

Three things get written wrong more often than everything else combined, and all three are
handled by the writer rather than left to the caller:

1. **The ISA is fixed-width** — 105 characters plus a terminator, every element a defined
   length. Receivers read the delimiters out of it by byte offset, so an ISA one character
   short is not a formatting nit, it is a file the other end cannot tokenize.
2. **SE01 counts the ST and the SE themselves.** Counting only the body is the single most
   common reason a structurally fine document is rejected.
3. **Trailers echo headers**: IEA02 = ISA13, GE02 = GS06, SE02 = ST02.

Everything outbound also travels back the way the 204 came, so sender and receiver swap.
Getting that backwards produces a file the partner routes into its own inbound queue and then
reports as an unknown sender.

## The 210 at the end

Raised when the load delivers, because before the `D1` there is nothing to invoice and a 210
that arrives before the freight does is a 210 that gets held.

```
ST*210*4008~
B3**INV-LD10041872*LD10041872*PP*L*20260820*323758****DEMO~
C3*USD~
ITD*01*3*****30~                         net 30 from the invoice date
N9*BM*BOL8842190~                        qualifier first — the opposite of L11
G62*11*20260818~                         shipped on this date
G62*35*20260819~                         delivered on this date
N1*BT … N1*SH … N1*CN~                   who pays, who shipped, who received
LX*1~
L5*1*CANNED VEGETABLES PALLETIZED~
L0*1***42150*G***24*PLT**L~
L1*1*2.5*CW*265375****LHS****LINEHAUL~
LX*2~
L1*2***58383****405****FUEL SURCHARGE 22 PERCENT OF LINEHAUL~
L3*42150*G***323758******24*L~
SE*23*4008~
```

**A 204 does not carry a rate.** The tender says what to move, where, and by when; what it
pays was agreed separately — on the phone, in a rate confirmation, or against a contracted
lane table — and none of that travels in the tender. This is the single most common surprise
for a developer wiring up a freight integration for the first time: the document that starts
the job says nothing about the money. So the charges come from a rate card in
`InvoiceRates`, and the numbers in it are invented. Do not read them as market pricing.

What does come off the load is the shape of the invoice. Linehaul is rated on the `L3`
weight; **every stop beyond the first two becomes a stop-off line**, read straight off the S5
loops, so the board's stop count and the invoice's stop-off count cannot drift apart. The
four-stop reefer bills two of them.

Two things about the file itself:

**The money elements are N2.** B307 Net Amount Due, L104 Amount Charged and L305 all carry an
implied decimal point and no explicit one. $3,237.58 is written `323758`. Sending `3237.58`
is not a formatting preference — it is an invoice some receivers read as $32.38 and others
reject outright. Amounts are held as integer cents all the way through so the conversion
happens once, at the edge. The rate element beside them, L102, is type R and *does* take a
decimal point, so `L1*1*2.5*CW*265375` is one segment containing two number formats and both
are correct.

**The reference segment is N9, not L11.** Both carry a reference number and a qualifier, and
they carry them in opposite order: N901 is the qualifier and N902 the value, while L1101 is
the value and L1102 the qualifier. A mapper that copies the 214's L11 handling into the 210
emits `N9*BOL8842190*BM` — a reference of type "BOL8842190" — and fails on a code list the
error message will not name.

## Multi-stop loads

A truckload move is a sequence of stops, not a pickup and a delivery. Three pickups and a
drop is ordinary; so is one pickup and four drops. The board carries a **current-stop
pointer** and every status message is reported against it, so a 214 sent from stop two of
four says stop two.

The pointer moves on *departure*, not arrival — that is the moment the truck stops being at
one place and starts running to the next. And leaving a stop it had arrived at produces
**two** status messages from one click, because the work finishing and the truck leaving are
different codes and a partner tracking a multi-drop load needs both. The `D1` is the proof of
delivery for that stop's freight.

`samples/204-reefer-multi-stop.edi` — load in Fresno, part-unload in Reno, part-unload in
Salt Lake City, complete unload in Denver — runs like this, and there is a test asserting
exactly this sequence:

```
XB  FRESNO           acknowledged, heading to the pickup
X3  FRESNO           arrived at pickup
CP  FRESNO           loading complete
AF  FRESNO           departed the pickup with the freight
X1  RENO             arrived at drop one
D1  RENO             part unload complete
CD  RENO             departed drop one
X1  SALT LAKE CITY
D1  SALT LAKE CITY
CD  SALT LAKE CITY
X1  DENVER           arrived at the final drop
D1  DENVER           complete unload — the load is done
```

Twelve 214s, one 997 before them and one 210 after, all from one file dropped in a directory.
The board shows `2/4` on the row and marks the stop the truck is at in the detail panel, with
the stops behind it ticked off. A two-stop load walks straight through and never sees the
cycle.

## Delimiters are declared, not assumed

`samples/204-flatbed-pipe-delimited.edi` uses `|` as the element separator, `>` as the
component separator and a newline as the segment terminator. All of it legal, all of it
declared inside the ISA, and all of it fatal to a parser that starts with `text.Split('~')`.
It parses to the same shape of load as the conventional samples, and the detail panel reports
what it read: `element '|' · terminator '\n' · 29 segments`.

Inbound delimiters are the sender's choice; outbound are always the conventional `* : ~ ^`.
A partner who tenders with pipes gets conventional delimiters back. That is a decision, not
a bug, but it is a decision a real partner would have an opinion about.

## Sample tenders

| File | What it is for | Acknowledged |
|---|---|---|
| `204-dry-van-2-stop.edi` | The ordinary case — one pickup, one delivery, windows on both ends | `AK5*A` |
| `204-reefer-multi-stop.edi` | Four stops, temperature control in N713, partial unloads, and a stop whose PO arrives in an `OID` rather than an `L11` | `AK5*A` |
| `204-flatbed-pipe-delimited.edi` | The delimiter case above | `AK5*A` |
| `204-bad-se-count.edi` | SE01 declares 21 where there are 22, and IEA02 does not echo ISA13. The tender still loads; the 997 says what was wrong and the diagnostics say the rest | `AK5*R*4` |

The last one is the point of showing diagnostics rather than throwing. A parser that refuses
to show you a bad file is useless for the one job you actually need it for, which is working
out why the partner rejected it — and meanwhile the truck still has to be dispatched.

The row for it, the two diagnostics, the `AK5*R*4` that went back, and the IEA02 finding the
997 could not carry — all on one screen:

![A tender with a defective envelope, and the 997 that refused it](docs/diagnostics.png)

## Layout

```
src/FreightDispatch.Core     model, 204 reader, 204/214/997/210 writers, the board,
                             the transport and TMS boundaries
src/FreightDispatch.Api      minimal API, DTOs, seed data, the integration host
tests/FreightDispatch.Tests  106 tests, including a full 204 round trip, the twelve-message
                             walk of the four-stop reefer, and an end-to-end run of the whole
                             lifecycle over a real watched directory
web/                         Angular client
samples/                     the four sample tenders
docs/transport.md            ITransport, the file drop, and how AS2 or SFTP would slot in
docs/tms-adapter.md          the TMS boundary and why the adapter owns the translation
external/edi-x12-toolkit     submodule — EdiX12.Core, until it is on NuGet
```

Envelope parsing is [`EdiX12.Core`](https://github.com/yurii1exe/edi-x12-toolkit) — it reads
the delimiters out of the ISA and hands back ST/SE transaction sets, which is why nothing in
this repository splits on a tilde. It is pinned as a submodule rather than assumed to be
checked out somewhere; see [docs/edi-toolkit-dependency.md](docs/edi-toolkit-dependency.md).

## Provenance

Everything here is written from the published ANSI X12 specification. Every shipper,
consignee, address, contact name, telephone number, SCAC, load number, bill of lading and
purchase order is invented. Nothing derives from any real trading partner, any partner's
implementation guide, or any production interchange.

`DEMOBROKER`, `DEMOCARRIER` and the SCACs `DEMO`, `SMPL` and `TEST` are not real trading
partners. ISA15 is `T` in every sample, because a sample file that says `P` is a sample file
somebody will eventually send into a production endpoint.

The TMS boundary is an interface and a mock. There is no connector to any commercial
transportation management system in this repository, and the mock's status vocabulary is
invented rather than borrowed.

This project is not affiliated with, endorsed by, or derived from the Accredited Standards
Committee X12. "X12" is used here only to name the standard it reads.

## Not built yet

State is in memory, one process, and so is the outbox. A real board needs the loads, the
status events, the control number sequence and the queue of things waiting to be sent in one
durable transaction — those going out of step is how a partner ends up with two interchanges
numbered `000000417` and no way to tell which one was the delivery. The gateway logs a failed
send and moves on, which is honest and is not enough.

There are no exception statuses yet: the board can report a load going right and not a load
going wrong. `SD` Shipment Delayed with a real element 1651 reason — `AO` weather, `AI`
mechanical breakdown, `B1` consignee closed — is the next thing it needs, because delays are
most of what a dispatcher actually keys.

There is no **990** Response to a Load Tender, so the board acknowledges the *syntax* of a
tender without ever accepting or declining the *load*. And there is no **TA1**, so
interchange-level defects are reported in the UI and not on the wire.

The 997 reports what the envelope validator can prove. AK3 and AK4 — segment and element
notes — would need a segment-level model of the 204 to populate, and guessing at them would
be worse than the silence.

## License

MIT.
