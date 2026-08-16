# Client

Angular 21, standalone components, signals, no router — the board is one screen.

```bash
npm install
npm start        # http://localhost:4200, /api proxied to the API on :5199
npm run build    # writes to ../src/FreightDispatch.Api/wwwroot
```

`npm start` expects `FreightDispatch.Api` to already be running on port 5199
(`ASPNETCORE_URLS=http://localhost:5199 dotnet run` from `src/FreightDispatch.Api`). The
proxy is in `proxy.conf.json`.

For a production build the client is compiled into the API's `wwwroot` and served from the
same origin, so `dotnet run` alone serves everything and the CORS policy in `Program.cs`
never applies.

## Layout

```
src/app/api/          typed models mirroring FreightDispatch.Api/Contracts.cs, and the
                      one service that owns all board state
src/app/board/        the grid, the detail panel, the tender dialog
src/app/ui/           the EDI viewer
src/app/format.ts     dispatch-grid formatting — wall clock times, military time,
                      windows, overdue flags
src/styles.css        the design system; component styles are only what is local
```

## Two things worth knowing

**Times have no time zone.** X12 does not carry one; element 623 (`LT`) says the sender
meant local time at the stop. `format.ts` parses them as wall clock values and never
converts. Treating them as UTC would move a 07:00 Pacific appointment to midnight and
nobody would notice until a truck was late.

**The EDI viewer reads delimiters the way a parser does** — element separator at ISA offset
3, segment terminator at offset 105 — rather than assuming `*` and `~`. That is what lets
the pipe-delimited sample render identically to the conventional ones.
