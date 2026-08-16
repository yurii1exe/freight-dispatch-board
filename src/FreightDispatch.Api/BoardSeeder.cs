using FreightDispatch.Core;
using FreightDispatch.Core.Model;

namespace FreightDispatch.Api;

/// <summary>
/// Fills a fresh board so that starting the process shows a working dispatch board rather
/// than an empty grid and a paste box.
/// </summary>
/// <remarks>
/// The four bundled sample files go on first, then the invented lanes from
/// <see cref="DemoTenders"/>. Everything arrives through <see cref="LoadBoard.Tender"/>,
/// which means the seeded board is also a smoke test of the reader every time the process
/// starts: if the 204 walk breaks, the board is empty and it is obvious immediately.
/// </remarks>
public static class BoardSeeder
{
    /// <summary>
    /// How far each bundled sample is moved along, so the board shows a spread of statuses.
    /// The defective one stays where it is — a tender you have not read the diagnostics on
    /// is a tender you have not accepted.
    /// </summary>
    private static readonly (string Sample, LoadStatus SeedTo)[] Progress =
    {
        ("204-dry-van-2-stop", LoadStatus.AtConsignee),
        ("204-reefer-multi-stop", LoadStatus.Dispatched),
        ("204-flatbed-pipe-delimited", LoadStatus.Loaded),
        ("204-bad-se-count", LoadStatus.Tendered),
    };

    /// <summary>Clears the board and repopulates it.</summary>
    /// <param name="board">The board.</param>
    /// <param name="samples">The bundled sample files.</param>
    /// <param name="now">Local "now", which the demo windows are laid out around.</param>
    public static void Seed(LoadBoard board, SampleLibrary samples, DateTime now)
    {
        board.Clear();

        foreach ((string name, LoadStatus seedTo) in Progress)
        {
            SampleTender? sample = samples.Find(name);
            if (sample is null)
            {
                continue;
            }

            Load load = board.Tender(sample.Edi).First();

            foreach (LoadStatus status in StatusCatalog.All.Skip(1))
            {
                if (status > seedTo)
                {
                    break;
                }

                int stepsBack = seedTo - status + 1;
                board.Advance(load.Id, status, now.AddHours(-3 * stepsBack));
            }
        }

        DemoTenders.Seed(board, now);
    }
}
