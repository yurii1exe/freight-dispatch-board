namespace FreightDispatch.Tests;

/// <summary>Loads the bundled sample tenders from the test output folder.</summary>
internal static class Samples
{
    internal const string DryVan = "204-dry-van-2-stop";
    internal const string Reefer = "204-reefer-multi-stop";
    internal const string PipeDelimited = "204-flatbed-pipe-delimited";
    internal const string BadSeCount = "204-bad-se-count";

    internal static string Read(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "samples", name + ".edi"));
}
