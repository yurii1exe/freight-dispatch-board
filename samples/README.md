# Sample load tenders

Four 204 Motor Carrier Load Tenders, written from the published ANSI X12 specification.

**Everything in these files is invented.** The shippers, consignees, street addresses,
contact names, telephone numbers, SCACs, load numbers, bills of lading and purchase orders
are fictional. Nothing here derives from any real trading partner, any partner's
implementation guide, or any production interchange. ISA15 is `T` — test — in all four.

| File | Delimiters | Stops | Shows | 997 back |
|---|---|---|---|---|
| `204-dry-van-2-stop.edi` | `* : ~ ^` | 2 | The ordinary case. 53' dry van, a window on each end, contacts and a PO | `AK5*A` |
| `204-reefer-multi-stop.edi` | `* : ~ ^` | 4 | Temperature control in N713, a pre-assigned trailer in N702, three drops with partial unloads, and one stop whose purchase order arrives in an `OID` rather than an `L11` | `AK5*A` |
| `204-flatbed-pipe-delimited.edi` | `\| > \n !` | 2 | The same structure with a pipe element separator and a newline segment terminator. Legal, declared in the ISA, and fatal to a parser that assumes `*` and `~` | `AK5*A` |
| `204-bad-se-count.edi` | `* : ~ ^` | 2 | Two ordinary sender defects: SE01 declares 21 segments where there are 22, and IEA02 does not echo ISA13 | `AK5*R*4` |

Drop any of them into `src/FreightDispatch.Api/edi-drop/in/` with the API running and the
acknowledgment appears in `edi-drop/out/` a second or two later.

## About the pipe-delimited file

It must keep LF line endings. The segment terminator **is** the newline — it is declared at
ISA offset 105, the character immediately after ISA16 — so rewriting the file to CRLF changes
what the terminator is. `.gitattributes` marks `*.edi` as binary to prevent that.

## About the defective one

It parses. It loads onto the board. That is deliberate: `Validate()` returns diagnostics
rather than throwing, because a parser that refuses to show you a bad file is useless for the
one job you actually need it for, which is working out why the partner rejected it. Meanwhile
the truck still has to be dispatched.

```
X12-SE01-COUNT at segment 24: SE01 (Number of Included Segments) declares '21',
  transaction set 204 0001 contains 22 segments counting ST and SE.
X12-IEA02-CONTROL at segment 26: IEA02 (Interchange Control Number) is '000004421'
  but ISA13 is '000004420'. The trailer must echo the header control number exactly.
```

Counting the ST and the SE is the part people get wrong, and the rejection a partner sends
back almost never says which number was wrong.

The board's own answer to this file is:

```
AK5*R*4~      element 718 code 4 — number of included segments does not match actual count
AK9*R*1*1*0~  one transaction set received, none accepted
```

Note what is **not** in it. The IEA02 problem is an interchange-level defect, and a 997
acknowledges functional groups; the message for that one is a TA1. The board records the
finding and says so rather than dropping it, because a sender whose IEA02 is wrong would
otherwise get a clean-looking acknowledgment and conclude their file was perfect.

The load appears on the board anyway, flagged `997 R`. That is deliberate — see the README.
