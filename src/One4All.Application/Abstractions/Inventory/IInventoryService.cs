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

    /// <summary>Produces the stock valuation for a company (optionally a single warehouse).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="warehouseId">Optional warehouse filter.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The valuation lines and total.</returns>
    Task<IReadOnlyList<StockValuationLine>> StockValuationAsync(long companyId, long? warehouseId = null, CancellationToken ct = default);
}
