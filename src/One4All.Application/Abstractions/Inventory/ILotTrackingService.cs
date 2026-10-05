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
}
