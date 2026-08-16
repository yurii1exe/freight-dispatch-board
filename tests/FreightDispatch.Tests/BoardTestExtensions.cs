using FreightDispatch.Core;
using FreightDispatch.Core.Model;

namespace FreightDispatch.Tests;

internal static class BoardTestExtensions
{
    /// <summary>
    /// Advances a load and returns the last event.
    /// </summary>
    /// <remarks>
    /// <see cref="LoadBoard.Advance"/> returns a list because leaving an intermediate stop
    /// emits two status messages — the work finishing and the truck leaving. Most tests care
    /// about one transition on a two-stop load, where the list always has one item;
    /// the multi-stop tests assert on the list directly.
    /// </remarks>
    internal static StatusEvent AdvanceOne(
        this LoadBoard board,
        Guid id,
        LoadStatus status,
        DateTime? occurredAt = null,
        string reasonCode = "NS",
        string? city = null,
        string? state = null,
        string? note = null) =>
        board.Advance(id, status, occurredAt, reasonCode, city, state, note)[^1];
}
