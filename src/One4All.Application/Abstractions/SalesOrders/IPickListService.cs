using One4All.Application.SalesOrders;
using One4All.Core.Common;

namespace One4All.Application.Abstractions.SalesOrders;

/// <summary>Builds and renders warehouse pick lists for sales orders (brief §4.13 despatch).</summary>
public interface IPickListService
{
    /// <summary>
    /// Builds the pick list for an order from one warehouse: every line with quantity still to despatch,
    /// against that warehouse's on-hand. Refuses a cancelled order; an order with nothing left to despatch
    /// yields an empty list.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="salesOrderId">The sales order.</param>
    /// <param name="warehouseId">The picking warehouse.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The pick list data.</returns>
    Task<Result<PickListDocument>> BuildAsync(long companyId, long salesOrderId, long warehouseId, CancellationToken ct = default);

    /// <summary>Renders the pick list as fixed-width text.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="salesOrderId">The sales order.</param>
    /// <param name="warehouseId">The picking warehouse.</param>
    /// <param name="width">The character width.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The pick list text.</returns>
    Task<Result<string>> RenderTextAsync(long companyId, long salesOrderId, long warehouseId, int width = PickListFormatter.DefaultWidth, CancellationToken ct = default);
}
