using One4All.Core.Common;

namespace One4All.Application.Abstractions.SalesOrders;

/// <summary>A sales-order line to drop-ship.</summary>
/// <param name="SalesOrderLineId">The order line.</param>
/// <param name="Quantity">The quantity.</param>
/// <param name="UnitCost">The supplier's price.</param>
public readonly record struct DropShipLine(long SalesOrderLineId, decimal Quantity, decimal UnitCost);

/// <summary>A confirmed drop-shipment.</summary>
/// <param name="Cost">The cost of the goods charged to cost of sales.</param>
/// <param name="SupplierInvoiceJournal">The supplier invoice's journal.</param>
public readonly record struct DropShipDelivered(decimal Cost, string SupplierInvoiceJournal);

/// <summary>
/// Drop-shipment (brief §4.13): the supplier delivers sales-order lines straight to the customer. Ordering records what the supplier is
/// to send (never more than is still to be despatched); confirming delivery posts the supplier's invoice to cost of sales, records a
/// delivery note and counts the lines as despatched - no stock moves - so the order is invoiced to the customer as usual.
/// </summary>
public interface IDropShipService
{
    /// <summary>
    /// Places a drop-shipment for order lines; refused beyond what is still to be despatched less what is already drop-shipped
    /// (<c>Sop.DropShipExceeded</c>) or for a line not on the order.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="salesOrderId">The sales order.</param>
    /// <param name="supplierId">The supplier delivering.</param>
    /// <param name="reference">The reference.</param>
    /// <param name="date">The order date.</param>
    /// <param name="lines">The lines.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The drop-shipment id.</returns>
    Task<Result<long>> OrderAsync(long companyId, long salesOrderId, long supplierId, string reference, DateOnly date, IReadOnlyList<DropShipLine> lines, CancellationToken ct = default);

    /// <summary>
    /// Confirms the supplier delivered to the customer: the supplier's invoice (Dr cost of sales, Cr AP), a delivery note with the same
    /// reference, and the order lines despatched. Once only (<c>Sop.DropShipDelivered</c>).
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="reference">The drop-shipment.</param>
    /// <param name="date">The delivery date.</param>
    /// <param name="supplierInvoiceNumber">The supplier's invoice number.</param>
    /// <param name="costOfSalesAccountCode">The cost of sales account.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The cost and journal.</returns>
    Task<Result<DropShipDelivered>> ConfirmDeliveredAsync(long companyId, string reference, DateOnly date, string supplierInvoiceNumber, string costOfSalesAccountCode, CancellationToken ct = default);
}
