using One4All.Application.Inventory;
using One4All.Core.Common;

namespace One4All.Application.Abstractions.Inventory;

/// <summary>Batch, serial and expiry traceability on top of the costing movements (brief §14 E 88–90).</summary>
public interface ILotTrackingService
{
    /// <summary>
    /// Records which batches or serials a stock movement moved. The lots must account for the whole movement; serials are one unit
    /// each, cannot be received while already in stock and cannot leave a warehouse they are not in; batches cannot go below zero.
    /// A received batch without an expiry takes the receipt date plus the item's shelf life.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="stockMovementId">The movement (receipt, issue, transfer leg, adjustment or supplier return).</param>
    /// <param name="lots">The lots and quantities (unsigned; the movement gives the direction).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or an error.</returns>
    Task<Result> RecordAsync(long companyId, long stockMovementId, IReadOnlyList<LotQuantity> lots, CancellationToken ct = default);

    /// <summary>Suggests the lots to pick for a quantity, first-expiry-first-out, never picking expired stock.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="itemId">The item.</param>
    /// <param name="warehouseId">The warehouse.</param>
    /// <param name="quantity">The quantity needed.</param>
    /// <param name="asAt">The picking date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The picks and any shortfall.</returns>
    Task<(IReadOnlyList<LotQuantity> Picks, decimal Shortfall)> SuggestPicksAsync(long companyId, long itemId, long warehouseId, decimal quantity, DateOnly asAt, CancellationToken ct = default);

    /// <summary>Records batches for part of a movement (a till sale beyond the batches on hand), skipping the quantity check.</summary>
    Task<Result> RecordPartialAsync(long companyId, long stockMovementId, IReadOnlyList<LotQuantity> lots, CancellationToken ct = default);

    /// <summary>
    /// Traces a batch or serial for a recall (brief §4.2): back to where it came from (receipts, with the supplier) and forward to where it went
    /// (issues and sales, with the customer where known), with what is still on hand by warehouse.
    /// </summary>
    Task<Result<LotTrace>> TraceAsync(long companyId, long itemId, string lotNumber, CancellationToken ct = default);
}

/// <summary>One movement of a traced lot.</summary>
/// <param name="Date">When.</param>
/// <param name="Reference">The document (GRN, sale, issue).</param>
/// <param name="Quantity">The quantity (positive in, negative out).</param>
/// <param name="WarehouseId">The warehouse.</param>
/// <param name="Party">The supplier or customer, where known.</param>
public sealed record LotTraceMovement(DateOnly Date, string? Reference, decimal Quantity, long WarehouseId, string? Party);

/// <summary>A lot's trace for a recall.</summary>
/// <param name="ItemId">The item.</param>
/// <param name="LotNumber">The batch / serial.</param>
/// <param name="ExpiryDate">Its expiry.</param>
/// <param name="CameFrom">Where it came from (receipts and other movements in).</param>
/// <param name="WentTo">Where it went (issues and sales).</param>
/// <param name="OnHand">What is still on hand, by warehouse.</param>
public sealed record LotTrace(long ItemId, string LotNumber, DateOnly? ExpiryDate, IReadOnlyList<LotTraceMovement> CameFrom, IReadOnlyList<LotTraceMovement> WentTo,
    IReadOnlyDictionary<long, decimal> OnHand)
{
    /// <summary>The customers it reached (for the recall notices).</summary>
    public IReadOnlyList<string> Customers => [.. WentTo.Select(m => m.Party).OfType<string>().Distinct(StringComparer.Ordinal)];
}
