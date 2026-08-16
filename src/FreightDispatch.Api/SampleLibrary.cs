using System.Collections.Immutable;

namespace FreightDispatch.Api;

/// <summary>
/// The sample 204 tenders that ship with the board.
/// </summary>
/// <remarks>
/// Every one of these is invented from the published X12 specification. The shippers,
/// consignees, addresses, contacts, SCACs, reference numbers and load numbers are all
/// fictional. Nothing here comes from a real trading partner, a real implementation guide
/// or a real interchange.
/// </remarks>
public sealed class SampleLibrary
{
    private readonly ImmutableArray<SampleTender> _samples;

    /// <summary>Loads the samples from the <c>samples</c> folder beside the executable.</summary>
    /// <param name="environment">Used to locate the content root.</param>
    /// <remarks>
    /// Two locations are probed because <c>dotnet run</c> and a published build disagree
    /// about where content lives: the content root is the project folder under the former
    /// and the publish folder under the latter, while the copied <c>samples</c> folder is
    /// always next to the assembly.
    /// </remarks>
    public SampleLibrary(IWebHostEnvironment environment)
    {
        string folder = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "samples"),
                Path.Combine(environment.ContentRootPath, "samples"),
            }
            .FirstOrDefault(Directory.Exists) ?? string.Empty;

        var descriptions = new Dictionary<string, (string Title, string Description)>(StringComparer.OrdinalIgnoreCase)
        {
            ["204-dry-van-2-stop"] = (
                "Dry van, Joliet to Memphis",
                "The ordinary case. One pickup, one delivery, 53-foot dry van, pickup and delivery windows on both ends."),
            ["204-reefer-multi-stop"] = (
                "Reefer, Fresno to Denver via Reno and Salt Lake",
                "Four stops, temperature control in N713, and a partial unload at each drop. The stop that carries its purchase order in an OID rather than an L11 is deliberate — both occur."),
            ["204-flatbed-pipe-delimited"] = (
                "Flatbed, Gary to Houston — pipe-delimited",
                "The same structure with '|' as the element separator, '>' as the component separator and a newline as the segment terminator. Entirely legal, and the file that breaks parsers which assume '*' and '~'."),
            ["204-bad-se-count"] = (
                "Dry van with a defective envelope",
                "SE01 declares 21 segments where there are 22, and IEA02 does not echo ISA13. Both are ordinary sender defects. The tender still loads; the diagnostics say what is wrong."),
        };

        _samples = Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*.edi")
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path =>
                {
                    string name = Path.GetFileNameWithoutExtension(path);
                    (string title, string description) = descriptions.TryGetValue(name, out var found)
                        ? found
                        : (name, string.Empty);

                    return new SampleTender(name, title, description, File.ReadAllText(path));
                })
                .ToImmutableArray()
            : ImmutableArray<SampleTender>.Empty;
    }

    /// <summary>Every bundled sample.</summary>
    public IReadOnlyList<SampleTender> All => _samples;

    /// <summary>Finds a sample by file name without extension, or null.</summary>
    /// <param name="name">e.g. <c>204-dry-van-2-stop</c>.</param>
    public SampleTender? Find(string name) =>
        _samples.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
}
