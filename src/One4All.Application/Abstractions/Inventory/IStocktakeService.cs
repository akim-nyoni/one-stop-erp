using One4All.Core.Common;

namespace One4All.Application.Abstractions.Inventory;

/// <summary>The result of posting a stocktake.</summary>
/// <param name="AdjustmentsPosted">The number of counted lines whose variance was posted.</param>
/// <param name="TotalVarianceValue">The net variance value posted (+ surplus, − shortage), at moving-average cost.</param>
public readonly record struct StocktakePostResult(int AdjustmentsPosted, decimal TotalVarianceValue);

/// <summary>A line of the blind count sheet: what to count, never what the system holds.</summary>
/// <param name="ItemId">The item.</param>
/// <param name="ItemCode">Its code.</param>
/// <param name="Description">Its description.</param>
/// <param name="Counted">Whether it has been counted.</param>
/// <param name="RecountDue">Whether the count is outside tolerance and must be recounted by someone else.</param>
/// <param name="Recounted">Whether it was recounted.</param>
public sealed record CountSheetLine(long ItemId, string ItemCode, string Description, bool Counted, bool RecountDue, bool Recounted);

/// <summary>
/// Manages a physical stocktake session (brief §4.2): freeze the warehouse's on-hand quantities, capture
/// the counts, then post the variance of each counted line into stock at moving-average cost.
/// </summary>
public interface IStocktakeService
{
    /// <summary>
    /// Starts a stocktake for a warehouse, freezing a line for every item that currently has a stock balance
    /// there (its on-hand quantity and average cost).
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="warehouseId">The warehouse to count.</param>
    /// <param name="date">The stocktake date.</param>
    /// <param name="reference">The stocktake reference.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The new stocktake session id.</returns>
    /// <param name="recountTolerancePercent">Variance (percent of the frozen quantity) beyond which a recount is required; null for none.</param>
    Task<Result<long>> StartAsync(long companyId, long warehouseId, DateOnly date, string reference, CancellationToken ct = default, decimal? recountTolerancePercent = null);

    /// <summary>Records the physical count for one item on a draft stocktake.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="stocktakeId">The stocktake session.</param>
    /// <param name="itemId">The item counted.</param>
    /// <param name="countedQuantity">The counted quantity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or NotFound/Rule.</returns>
    /// <param name="countedByUserId">Who counted (a recount must be by someone else).</param>
    Task<Result> RecordCountAsync(long companyId, long stocktakeId, long itemId, decimal countedQuantity, CancellationToken ct = default, long? countedByUserId = null);

    /// <summary>
    /// Records a recount of a line whose count was outside the tolerance; it replaces the count (the first is kept). The recounter must
    /// not be the first counter (<c>Stocktake.SameCounter</c>), and only lines due a recount take one (<c>Stocktake.NoRecountDue</c>).
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="stocktakeId">The stocktake.</param>
    /// <param name="itemId">The item.</param>
    /// <param name="quantity">The recount.</param>
    /// <param name="userId">Who recounted.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    Task<Result> RecordRecountAsync(long companyId, long stocktakeId, long itemId, decimal quantity, long userId, CancellationToken ct = default);

    /// <summary>The blind count sheet: items to count with their count and recount state, without the system quantities.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="stocktakeId">The stocktake.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The sheet in item-code order.</returns>
    Task<Result<IReadOnlyList<CountSheetLine>>> CountSheetAsync(long companyId, long stocktakeId, CancellationToken ct = default);

    /// <summary>
    /// Posts a draft stocktake: adjusts each counted line's variance into stock at moving-average cost
    /// (surplus increases stock, shortage decreases it) against the adjustment account, then closes the
    /// session. Uncounted lines are left untouched. Refused while recounts are due (<c>Stocktake.RecountsDue</c>); one unit of work.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="stocktakeId">The stocktake session.</param>
    /// <param name="date">The posting date.</param>
    /// <param name="adjustmentAccountCode">The stock-adjustment account for the variances.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The post result, or an error.</returns>
    Task<Result<StocktakePostResult>> PostAsync(long companyId, long stocktakeId, DateOnly date, string adjustmentAccountCode, CancellationToken ct = default);
}
