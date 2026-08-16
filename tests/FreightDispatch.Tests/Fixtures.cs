namespace FreightDispatch.Tests;

/// <summary>
/// Interchanges built for one test each, by surgery on a bundled sample.
/// </summary>
/// <remarks>
/// Every one of these is a real thing partners do. They are assembled here rather than
/// checked into <c>samples/</c> because the samples folder is a demonstration of what a
/// tender looks like, and a file whose only purpose is to be wrong in one specific way
/// belongs beside the test that asserts on it.
/// </remarks>
internal static class Fixtures
{
    /// <summary>
    /// A good 204 and a 990 in the same functional group.
    /// </summary>
    /// <remarks>
    /// The 990 is a Response to a Load Tender — a real transaction set, correctly enveloped,
    /// and not one this board reads. It is acknowledged with element 718 code 1, and the
    /// group ends up partially accepted rather than rejected, which is the distinction the
    /// P code exists to make.
    /// </remarks>
    internal static string TwoTransactionSets =>
        Samples.Read(Samples.DryVan).Replace(
            "GE*1*4417~",
            "ST*990*0002~B1*DEMO*LD10041872*20260817*A~SE*3*0002~GE*2*4417~",
            StringComparison.Ordinal);

    /// <summary>
    /// A correctly enveloped interchange in the right functional group that contains no
    /// load tender at all — only a 990 Response to a Load Tender.
    /// </summary>
    /// <remarks>
    /// This is the case that produces an empty board and a confused operator: the file
    /// parses, the envelope is sound, the 997 goes back, and nothing appears. The
    /// acknowledgment says why, and so should the board.
    /// </remarks>
    internal static string NoLoadTender
    {
        get
        {
            string[] lines = Samples.Read(Samples.DryVan).Split('\n');

            return string.Join(
                "\n",
                lines[0],                           // the ISA, verbatim: it is fixed width
                lines[1],                           // GS*SM*…
                "ST*990*0002~",
                "B1*DEMO*LD10041872*20260817*A~",
                "SE*3*0002~",
                "GE*1*4417~",
                "IEA*1*000004417~") + "\n";
        }
    }

    /// <summary>Two 204s in one group, both numbered 0001 — a resend that never advanced the counter.</summary>
    internal static string DuplicateControlNumbers =>
        Samples.Read(Samples.DryVan).Replace(
            "GE*1*4417~",
            "ST*204*0001~B2**DEMO**LD10041999**PP~B2A*00*LT~SE*4*0001~GE*2*4417~",
            StringComparison.Ordinal);
}
