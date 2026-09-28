using One4All.Core.Common;

namespace One4All.Application.Abstractions.Inventory;

/// <summary>The result of posting a stocktake.</summary>
/// <param name="AdjustmentsPosted">The number of counted lines whose variance was posted.</param>
/// <param name="TotalVarianceValue">The net variance value posted (+ surplus, − shortage), at moving-average cost.</param>
public readonly record struct StocktakePostResult(int AdjustmentsPosted, decimal TotalVarianceValue);

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
    Task<Result<long>> StartAsync(long companyId, long warehouseId, DateOnly date, string reference, CancellationToken ct = default);

    /// <summary>Records the physical count for one item on a draft stocktake.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="stocktakeId">The stocktake session.</param>
    /// <param name="itemId">The item counted.</param>
    /// <param name="countedQuantity">The counted quantity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or NotFound/Rule.</returns>
    Task<Result> RecordCountAsync(long companyId, long stocktakeId, long itemId, decimal countedQuantity, CancellationToken ct = default);

    /// <summary>
    /// Posts a draft stocktake: adjusts each counted line's variance into stock at moving-average cost
    /// (surplus increases stock, shortage decreases it) against the adjustment account, then closes the
    /// session. Uncounted lines are left untouched.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="stocktakeId">The stocktake session.</param>
    /// <param name="date">The posting date.</param>
    /// <param name="adjustmentAccountCode">The stock-adjustment account for the variances.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The post result, or an error.</returns>
    Task<Result<StocktakePostResult>> PostAsync(long companyId, long stocktakeId, DateOnly date, string adjustmentAccountCode, CancellationToken ct = default);
}
