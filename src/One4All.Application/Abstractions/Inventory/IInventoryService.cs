using One4All.Core.Common;

namespace One4All.Application.Abstractions.Inventory;

/// <summary>A request to receive stock into a warehouse.</summary>
public sealed class ReceiveStockRequest
{
    /// <summary>The company.</summary>
    public long CompanyId { get; init; }

    /// <summary>The item.</summary>
    public long ItemId { get; init; }

    /// <summary>The warehouse.</summary>
    public long WarehouseId { get; init; }

    /// <summary>The movement date.</summary>
    public DateOnly Date { get; init; }

    /// <summary>The quantity received (positive).</summary>
    public decimal Quantity { get; init; }

    /// <summary>The unit cost.</summary>
    public decimal UnitCost { get; init; }

    /// <summary>The offsetting GL account credited (e.g. GRNI, AP clearing, opening balances).</summary>
    public string CreditAccountCode { get; init; } = string.Empty;

    /// <summary>An optional reference.</summary>
    public string? Reference { get; init; }

    /// <summary>The batches / serials received (required for a tracked item when the company enforces lot tracking).</summary>
    public IReadOnlyList<One4All.Application.Inventory.LotQuantity> Lots { get; init; } = [];

    /// <summary>The weight received or issued, for a catch-weight item (required for one).</summary>
    public decimal? Weight { get; init; }

    /// <summary>The cost per weight unit, for a catch-weight item bought by weight (instead of <see cref="UnitCost"/> per piece).</summary>
    public decimal? CostPerWeightUnit { get; init; }
}

/// <summary>A request to issue stock out of a warehouse (at moving-average cost).</summary>
public sealed class IssueStockRequest
{
    /// <summary>The company.</summary>
    public long CompanyId { get; init; }

    /// <summary>The item.</summary>
    public long ItemId { get; init; }

    /// <summary>The warehouse.</summary>
    public long WarehouseId { get; init; }

    /// <summary>The movement date.</summary>
    public DateOnly Date { get; init; }

    /// <summary>The quantity issued (positive).</summary>
    public decimal Quantity { get; init; }

    /// <summary>An optional reference.</summary>
    public string? Reference { get; init; }

    /// <summary>When true, allows the issue to take the balance negative (backorder); otherwise an over-issue is refused.</summary>
    public bool AllowNegative { get; init; }

    /// <summary>Why the issue may take the balance negative (required under the company's negative-stock control).</summary>
    public string? NegativeReason { get; init; }

    /// <summary>The supervisor allowing it (needs Inventory › Stock Issue authorise under the control; ignored for till sales).</summary>
    public long? NegativeAuthorisedByUserId { get; init; }

    /// <summary>Whether this is a till sale (let through under the control, recorded for review).</summary>
    public bool FromTill { get; init; }

    /// <summary>The sales-order line despatching the stock (its own reservation may be used); every other issue leaves reserved stock alone.</summary>
    public long? SalesOrderLineId { get; init; }

    /// <summary>The batches / serials issued (none: the earliest-expiry batches on hand are taken when the company enforces lot tracking).</summary>
    public IReadOnlyList<One4All.Application.Inventory.LotQuantity> Lots { get; init; } = [];

    /// <summary>The weight received or issued, for a catch-weight item (required for one).</summary>
    public decimal? Weight { get; init; }

    /// <summary>
    /// The account the issue is charged to, for internal consumption (a department's supplies expense, say); null to
    /// charge the item's cost of sales.
    /// </summary>
    public string? ChargeAccountCode { get; init; }

    /// <summary>The cost centre the charge is analysed to, if any.</summary>
    public string? CostCentre { get; init; }
}

/// <summary>The result of a stock movement.</summary>
/// <param name="MovementId">The created movement id.</param>
/// <param name="JournalNumber">The GL journal number.</param>
/// <param name="Value">The value of the movement.</param>
/// <param name="AverageCostAfter">The item's moving-average cost after the movement.</param>
public readonly record struct StockMovementResult(long MovementId, string JournalNumber, decimal Value, decimal AverageCostAfter);

/// <summary>One line of a stock valuation.</summary>
/// <param name="ItemCode">The item code.</param>
/// <param name="Description">The item description.</param>
/// <param name="WarehouseCode">The warehouse.</param>
/// <param name="Quantity">The quantity on hand.</param>
/// <param name="AverageCost">The moving-average unit cost.</param>
/// <param name="Value">The value on hand.</param>
public readonly record struct StockValuationLine(string ItemCode, string Description, string WarehouseCode, decimal Quantity, decimal AverageCost, decimal Value);

/// <summary>
/// Inventory service (brief §4.2/§3.6): receives and issues stock with moving weighted-average costing,
/// posting to the GL stock control and cost-of-sales accounts, and produces the stock valuation.
/// </summary>
public interface IInventoryService
{
    /// <summary>Receives stock at a unit cost: Dr stock control, Cr the offset account; updates the moving average.</summary>
    /// <param name="request">The receive request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The movement result, or an error.</returns>
    Task<Result<StockMovementResult>> ReceiveAsync(ReceiveStockRequest request, CancellationToken ct = default);

    /// <summary>Issues stock at moving-average cost: Dr cost of sales, Cr stock control.</summary>
    /// <param name="request">The issue request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The movement result, or an error.</returns>
    Task<Result<StockMovementResult>> IssueAsync(IssueStockRequest request, CancellationToken ct = default);

    /// <summary>
    /// Reverses a prior stock issue: returns the issued quantity to the warehouse at the <b>original</b> unit
    /// cost applied and posts the exact inverse journal (Dr stock control, Cr cost of sales). Refuses a movement
    /// that is not an issue or has already been reversed. Used to undo an issue (e.g. a reversed bill).
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="movementId">The stock movement (an issue) to reverse.</param>
    /// <param name="date">The reversal date.</param>
    /// <param name="reference">An optional reference for the reversal movement.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The reversal movement result, or an error.</returns>
    Task<Result<StockMovementResult>> ReverseIssueAsync(long companyId, long movementId, DateOnly date, string? reference = null, CancellationToken ct = default);

    /// <summary>
    /// Transfers stock between two warehouses of the same company at the source's moving-average cost. Because
    /// both warehouses share one stock-control account, the transfer is GL-neutral (no journal); it moves the
    /// quantity and value between the warehouse balances and records paired transfer movements.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="itemId">The item.</param>
    /// <param name="fromWarehouseId">The source warehouse (must hold enough stock).</param>
    /// <param name="toWarehouseId">The destination warehouse.</param>
    /// <param name="date">The transfer date.</param>
    /// <param name="quantity">The quantity to move (positive).</param>
    /// <param name="reference">An optional reference.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The value transferred (at the source's average cost), or an error.</returns>
    Task<Result<decimal>> TransferAsync(long companyId, long itemId, long fromWarehouseId, long toWarehouseId, DateOnly date, decimal quantity, string? reference = null, CancellationToken ct = default);

    /// <summary>
    /// Adjusts stock to a physical count (stocktake): computes the variance against the on-hand quantity and
    /// posts it at moving-average cost against a stock-adjustment account — a surplus increases stock
    /// (Dr stock / Cr adjustment), a shortage decreases it (Dr adjustment / Cr stock). A nil variance is a
    /// no-op. Records an Adjustment movement and returns the signed variance value (+ surplus, − shortage).
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="itemId">The item.</param>
    /// <param name="warehouseId">The warehouse counted.</param>
    /// <param name="date">The stocktake date.</param>
    /// <param name="countedQuantity">The physically counted quantity (the new on-hand).</param>
    /// <param name="adjustmentAccountCode">The stock-adjustment / shrinkage account for the variance.</param>
    /// <param name="reference">An optional reference (e.g. the stocktake number).</param>
    /// <param name="reasonCode">An optional active reason code (damaged, expired, theft…) the adjustment is analysed by.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The signed variance value (+ surplus, − shortage), or an error.</returns>
    Task<Result<decimal>> AdjustAsync(long companyId, long itemId, long warehouseId, DateOnly date, decimal countedQuantity, string adjustmentAccountCode, string? reference = null, string? reasonCode = null,
        CancellationToken ct = default);

    /// <summary>Produces the stock valuation for a company (optionally a single warehouse).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="warehouseId">Optional warehouse filter.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The valuation lines and total.</returns>
    Task<IReadOnlyList<StockValuationLine>> StockValuationAsync(long companyId, long? warehouseId = null, CancellationToken ct = default);

    /// <summary>
    /// Changes a standard-costed item's standard cost, revaluing its stock on hand in every warehouse to the new standard:
    /// Dr / Cr stock control against the revaluation account, in one unit.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="itemId">The item.</param>
    /// <param name="newStandardCost">The new standard unit cost.</param>
    /// <param name="date">The revaluation date.</param>
    /// <param name="revaluationAccountCode">The account taking the revaluation.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The net revaluation (+ stock up, − stock down), or why it was refused.</returns>
    Task<Result<decimal>> RevalueStandardCostAsync(long companyId, long itemId, decimal newStandardCost, DateOnly date, string revaluationAccountCode, CancellationToken ct = default);

    /// <summary>Makes an item catch-weight (a weight unit; null makes it ordinary again), with its nominal weight per piece and the tolerance a receipt's average may stray from it.</summary>
    Task<Result> SetCatchWeightAsync(long companyId, long itemId, string? weightUnit, decimal? nominalWeight, decimal? tolerancePercent, CancellationToken ct = default);
}
