# The EdiX12.Core dependency

This board does not parse X12 envelopes itself. It uses **`EdiX12.Core`** from the
[edi-x12-toolkit](../../edi-x12-toolkit) repository, which reads the delimiters out of the
ISA segment, walks ISA/GS/ST into a typed tree and validates the nine envelope rules a
receiving partner checks before it looks at any business data.

## How it is wired today

`EdiX12.Core` is not on NuGet yet, so it is referenced as a project in the sibling
repository. The path is set once, in `Directory.Build.props`:

```xml
<EdiX12CoreProject Condition="'$(EdiX12CoreProject)' == ''">
  $(MSBuildThisFileDirectory)..\edi-x12-toolkit\src\EdiX12.Core\EdiX12.Core.csproj
</EdiX12CoreProject>
```

The default assumes the two repositories are checked out side by side:

```
D:\repo\
  edi-x12-toolkit\
  freight-dispatch-board\
```

Override it if they are not:

```bash
dotnet build -p:EdiX12CoreProject=C:\src\EdiX12.Core\EdiX12.Core.csproj
```

If the project is not found, `FreightDispatch.Core` fails the build with a message saying so
rather than emitting a hundred lines of "the type or namespace `EdiX12` could not be found".

## Why it is also in the solution file

`EdiX12.Core.csproj` is listed in `FreightDispatch.sln` under an `external` solution folder.
That is not decoration. A project reference to a project **outside** the solution does not
inherit the solution's configuration: `dotnet build -c Release` builds the referenced project
as **Debug**, and the Release output then links a Debug assembly. Adding it to the solution
makes the configuration map correctly:

```
EdiX12.Core -> ..\edi-x12-toolkit\src\EdiX12.Core\bin\Release\net8.0\EdiX12.Core.dll
```

The cost is that the `.sln` contains a `..\` path and will not open without the sibling
checkout. That is already true of the build, so nothing is lost.

## Switching to the package

Once `EdiX12.Core` is published, the change is one file. In
`src/FreightDispatch.Core/FreightDispatch.Core.csproj`, replace the conditional
`ProjectReference` and the `CheckEdiX12Core` target with:

```xml
<ItemGroup>
  <PackageReference Include="EdiX12.Core" Version="0.1.0" />
</ItemGroup>
```

Then delete the `EdiX12CoreProject` property from `Directory.Build.props` and remove the
project from the solution:

```bash
dotnet sln remove ../edi-x12-toolkit/src/EdiX12.Core/EdiX12.Core.csproj
```

Nothing else refers to it. `EdiX12.Core` targets `netstandard2.0` and `net8.0`; this project
resolves the `net8.0` asset.

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
toolkit, because the toolkit is at v0.1 and generation is on its roadmap for v0.3. If it
lands there, this project deletes `X12Writer` and keeps the two transaction-set writers.
