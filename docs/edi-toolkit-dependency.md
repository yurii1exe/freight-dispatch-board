# The EdiX12.Core dependency

This board does not parse X12 envelopes itself. It uses **`EdiX12.Core`** from the
[edi-x12-toolkit](https://github.com/yurii1exe/edi-x12-toolkit) repository, which reads the
delimiters out of the ISA segment, walks ISA/GS/ST into a typed tree and validates the
envelope rules a receiving partner checks before it looks at any business data.

`EdiX12.Core` is not on NuGet yet. Until it is, this repository pins it as a **git
submodule** rather than assuming you happen to have it checked out somewhere.

## How it is wired today

```
freight-dispatch-board/
  external/
    edi-x12-toolkit/          ← submodule, pinned to a specific commit
    Directory.Build.props     ← deliberately empty; see below
```

Clone it with the submodule and everything builds:

```bash
git clone --recurse-submodules https://github.com/yurii1exe/freight-dispatch-board.git
cd freight-dispatch-board
dotnet build -c Release
```

If you already cloned without it:

```bash
git submodule update --init --recursive
```

### The path is resolved once, in `Directory.Build.props`

```
1. -p:EdiX12CoreProject=<path>    an explicit override wins
2. external/edi-x12-toolkit/...   the submodule — the normal case
3. ../edi-x12-toolkit/...         a sibling checkout, for working on both at once
```

So if you are editing both repositories side by side and do not want the pinned copy,
either delete `external/edi-x12-toolkit`'s contents or pass the path:

```bash
dotnet build -p:EdiX12CoreProject=..\edi-x12-toolkit\src\EdiX12.Core\EdiX12.Core.csproj
```

If nothing resolves, `FreightDispatch.Core` fails with a single message naming the
`git submodule update --init` command, rather than a hundred lines of "the type or namespace
`EdiX12` could not be found".

### Why `external/Directory.Build.props` is empty

MSBuild walks up from a project directory until it finds a `Directory.Build.props` and stops
at the first one. Without an empty file at `external/`, `EdiX12.Core` would inherit *this*
repository's root props — which pins `TargetFramework` to `net8.0` and would quietly collapse
the toolkit's `netstandard2.0;net8.0` multi-targeting. The submodule is a separate repository
and builds with its own settings.

### Why it is also in the solution file

`EdiX12.Core.csproj` is listed in `FreightDispatch.sln` under an `external` solution folder.
That is not decoration. A project reference to a project **outside** the solution does not
inherit the solution's configuration: `dotnet build -c Release` builds the referenced project
as **Debug**, and the Release output then links a Debug assembly. Adding it to the solution
makes the configuration map correctly:

```
EdiX12.Core -> external\edi-x12-toolkit\src\EdiX12.Core\bin\Release\net8.0\EdiX12.Core.dll
```

## Switching to the package

**This is the end state.** The submodule is scaffolding for the window between this repository
being public and `EdiX12.Core` being on nuget.org, and it should be removed the day the package
lands. In order:

1. In `src/FreightDispatch.Core/FreightDispatch.Core.csproj`, replace the conditional
   `ProjectReference` and the `CheckEdiX12Core` target with a package reference — the version
   is whatever was published; the project currently builds against `0.1.0-alpha`:

   ```xml
   <ItemGroup>
     <PackageReference Include="EdiX12.Core" Version="0.1.0" />
   </ItemGroup>
   ```

2. Delete the `EdiX12CoreProject`, `EdiX12CoreSubmodule` and `EdiX12CoreSibling` properties
   from `Directory.Build.props`.

3. Remove the project from the solution:

   ```bash
   dotnet sln remove external/edi-x12-toolkit/src/EdiX12.Core/EdiX12.Core.csproj
   ```

4. Remove the submodule and the props file that shielded it:

   ```bash
   git submodule deinit -f external/edi-x12-toolkit
   git rm -f external/edi-x12-toolkit
   rm -rf .git/modules/external/edi-x12-toolkit
   git rm -f external/Directory.Build.props
   ```

5. Drop `submodules: recursive` from `.github/workflows/ci.yml`, and drop the
   `--recurse-submodules` from the clone line in `README.md` and from this file.

6. `dotnet build -c Release && dotnet test -c Release` — 52 tests, and the round-trip tests
   are the ones that would notice a behavioural difference between the pinned commit and the
   published package.

Nothing else refers to it. `EdiX12.Core` targets `netstandard2.0` and `net8.0`; this project
resolves the `net8.0` asset either way.

## What this project uses from it

| API | Used for |
|---|---|
| `X12Parser.Parse` | Inbound 204, and re-parsing every generated 214 |
| `Interchange.Validate` | The diagnostics shown on a tender and on a generated file |
| `Interchange.Transactions` | Finding the 204 transaction sets in an interchange |
| `TransactionSet.Segments` | The body walk in `Edi204Reader` |
| `Segment` indexer and `Position` | Element access by spec number, and error locations |
| `X12Tokenizer.ReadDelimiters` | Reporting what an inbound file declared about itself |
| `X12Delimiters` | Choosing what outbound files are written with |
| `X12ParseException.SegmentPosition` | Naming the offending segment in an API error |

The writer side — `X12Writer`, `Edi204Writer`, `Edi214Writer` — lives here rather than in the
toolkit, because the toolkit is at v0.1 and generation is on its roadmap. If it lands there,
this project deletes `X12Writer` and keeps the two transaction-set writers.
