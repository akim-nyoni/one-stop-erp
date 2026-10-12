using One4All.Core.Common;
using One4All.Core.Restaurant;

namespace One4All.Application.Abstractions.Restaurant;

/// <summary>
/// The restaurant floor plan (brief §4.1 table / tab management): where each table sits and its shape, what is on it now — the open tab,
/// its total and how long it has been open — and the warnings for tabs open too long or running too high.
/// </summary>
public interface IFloorPlanService
{
    /// <summary>Places a table on the floor plan grid (0–99 each way) with its shape.</summary>
    Task<Result> PlaceTableAsync(long companyId, string tableNumber, int x, int y, TableShape shape, CancellationToken ct = default);

    /// <summary>Sets when an open tab is flagged: after so many minutes, or over a total (0 turns either off).</summary>
    Task<Result> SetTabWarningsAsync(long companyId, int minutes, decimal amount, CancellationToken ct = default);

    /// <summary>The floor plan as at a moment: every table with its place, shape, open tab and warning.</summary>
    Task<IReadOnlyList<FloorTable>> PlanAsync(long companyId, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>The open tabs past the warning time or over the warning total.</summary>
    Task<IReadOnlyList<FloorTable>> WarningsAsync(long companyId, DateTimeOffset now, CancellationToken ct = default);
}

/// <summary>A table on the floor plan.</summary>
/// <param name="TableNumber">The table.</param>
/// <param name="Seats">Its seats.</param>
/// <param name="X">Its column (null: not placed).</param>
/// <param name="Y">Its row (null: not placed).</param>
/// <param name="Shape">Its shape.</param>
/// <param name="Status">Free or occupied.</param>
/// <param name="OrderNumber">The open tab on it, if any.</param>
/// <param name="TabTotal">The tab's total (gross).</param>
/// <param name="MinutesOpen">How long the tab has been open.</param>
/// <param name="Warning">Why the tab is flagged, or null.</param>
public sealed record FloorTable(string TableNumber, int Seats, int? X, int? Y, TableShape Shape, TableStatus Status, string? OrderNumber, decimal TabTotal, int MinutesOpen, string? Warning);
