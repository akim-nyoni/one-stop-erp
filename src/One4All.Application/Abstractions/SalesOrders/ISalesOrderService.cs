using One4All.Core.Accounting;
using One4All.Core.Common;

namespace One4All.Application.Abstractions.SalesOrders;

/// <summary>A line on a new sales order.</summary>
/// <param name="ItemId">The item.</param>
/// <param name="Description">The description.</param>
/// <param name="Quantity">The quantity.</param>
/// <param name="UnitPrice">The unit price; null resolves it from the customer's price list (then the default list) for the quantity.</param>
/// <param name="TaxCode">The tax code.</param>
/// <param name="RevenueAccountCode">The revenue account.</param>
public readonly record struct SalesOrderLineRequest(long ItemId, string Description, decimal Quantity, decimal? UnitPrice, string? TaxCode, string RevenueAccountCode);

/// <summary>A request to create a sales order.</summary>
public sealed class CreateSalesOrderRequest
{
    /// <summary>The company.</summary>
    public long CompanyId { get; init; }

    /// <summary>The customer.</summary>
    public long CustomerId { get; init; }

    /// <summary>The order number; blank allocates the next number from the company's <c>SO</c> sequence.</summary>
    public string OrderNumber { get; init; } = string.Empty;

    /// <summary>The order date.</summary>
    public DateOnly OrderDate { get; init; }

    /// <summary>The currency.</summary>
    public CurrencyCode Currency { get; init; }

    /// <summary>The order lines.</summary>
    public IReadOnlyList<SalesOrderLineRequest> Lines { get; init; } = [];

    /// <summary>Gets the date the customer wants the goods.</summary>
    public DateOnly? RequestedDeliveryDate { get; init; }
}

/// <summary>A despatched line.</summary>
/// <param name="SalesOrderLineId">The order line.</param>
/// <param name="Quantity">The quantity despatched.</param>
public readonly record struct DespatchLine(long SalesOrderLineId, decimal Quantity);

/// <summary>A request to despatch goods from a sales order.</summary>
public sealed class DespatchRequest
{
    /// <summary>The company.</summary>
    public long CompanyId { get; init; }

    /// <summary>The sales order.</summary>
    public long SalesOrderId { get; init; }

    /// <summary>The warehouse despatched from.</summary>
    public long WarehouseId { get; init; }

    /// <summary>The despatch date.</summary>
    public DateOnly Date { get; init; }

    /// <summary>The despatch reference.</summary>
    public string Reference { get; init; } = string.Empty;

    /// <summary>The despatched lines.</summary>
    public IReadOnlyList<DespatchLine> Lines { get; init; } = [];

    /// <summary>Gets the date the delivery is planned for.</summary>
    public DateOnly? PlannedFor { get; init; }

    /// <summary>Gets the vehicle.</summary>
    public string? Vehicle { get; init; }

    /// <summary>Gets the route.</summary>
    public string? Route { get; init; }

    /// <summary>Gets the driver.</summary>
    public string? Driver { get; init; }
}

/// <summary>An invoiced line.</summary>
/// <param name="SalesOrderLineId">The order line.</param>
/// <param name="Quantity">The quantity invoiced.</param>
/// <param name="TaxAmount">The tax on this quantity.</param>
public readonly record struct InvoiceOrderLine(long SalesOrderLineId, decimal Quantity, decimal TaxAmount);

/// <summary>A request to invoice a sales order (for the despatched-but-uninvoiced quantity).</summary>
public sealed class InvoiceOrderRequest
{
    /// <summary>The company.</summary>
    public long CompanyId { get; init; }

    /// <summary>The sales order.</summary>
    public long SalesOrderId { get; init; }

    /// <summary>The invoice number.</summary>
    public string InvoiceNumber { get; init; } = string.Empty;

    /// <summary>The invoice date.</summary>
    public DateOnly Date { get; init; }

    /// <summary>The output-tax account.</summary>
    public string? TaxAccountCode { get; init; }

    /// <summary>The invoiced lines.</summary>
    public IReadOnlyList<InvoiceOrderLine> Lines { get; init; } = [];
}

/// <summary>The result of creating a sales order.</summary>
/// <param name="SalesOrderId">The order id.</param>
/// <param name="OrderNumber">The order number.</param>
public readonly record struct SalesOrderResult(long SalesOrderId, string OrderNumber);

/// <summary>The result of a despatch.</summary>
/// <param name="CostOfSales">The cost of sales posted.</param>
public readonly record struct DespatchResult(decimal CostOfSales);

/// <summary>The result of invoicing a sales order.</summary>
/// <param name="OpenItemId">The AR open item id.</param>
/// <param name="JournalNumber">The GL journal number.</param>
/// <param name="InvoiceValue">The gross invoice value.</param>
public readonly record struct InvoiceOrderResult(long OpenItemId, string JournalNumber, decimal InvoiceValue);

/// <summary>Sales Order Processing / Distribution (brief §4.13): order → despatch (stock issue) → invoice (AR).</summary>
public interface ISalesOrderService
{
    /// <summary>Creates a sales order.</summary>
    /// <param name="request">The request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The order result.</returns>
    Task<Result<SalesOrderResult>> CreateOrderAsync(CreateSalesOrderRequest request, CancellationToken ct = default);

    /// <summary>Despatches goods: issues stock (cost of sales) for stock lines and records the despatched quantities.</summary>
    /// <param name="request">The request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The despatch result.</returns>
    Task<Result<DespatchResult>> DespatchAsync(DespatchRequest request, CancellationToken ct = default);

    /// <summary>
    /// Returns a despatched-but-not-yet-invoiced quantity of an order line back into stock (goods came back
    /// before invoicing): receives the quantity at the current average cost against the item's cost-of-sales
    /// account (Dr stock / Cr COGS) and reduces the line's despatched total. Refuses more than the
    /// despatched-uninvoiced balance. Returns the value returned to stock.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="salesOrderId">The sales order.</param>
    /// <param name="salesOrderLineId">The order line.</param>
    /// <param name="warehouseId">The warehouse the goods return to.</param>
    /// <param name="date">The return date.</param>
    /// <param name="quantity">The quantity returned (positive).</param>
    /// <param name="reason">The mandatory reason.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The value returned to stock.</returns>
    Task<Result<decimal>> ReturnDespatchAsync(long companyId, long salesOrderId, long salesOrderLineId, long warehouseId, DateOnly date, decimal quantity, string reason, CancellationToken ct = default);

    /// <summary>
    /// Cancels a sales order, releasing its stock commitment. Refused once anything has been despatched
    /// (<c>Sop.Despatched</c>) or when already cancelled; requires a reason.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="salesOrderId">The sales order.</param>
    /// <param name="reason">The mandatory reason.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or an error.</returns>
    Task<Result> CancelOrderAsync(long companyId, long salesOrderId, string reason, CancellationToken ct = default);

    /// <summary>Invoices the despatched-but-uninvoiced quantity, raising the AR invoice.</summary>
    /// <param name="request">The request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The invoice result.</returns>
    Task<Result<InvoiceOrderResult>> InvoiceAsync(InvoiceOrderRequest request, CancellationToken ct = default);

    /// <summary>Records proof of delivery on a delivery note: when it arrived and who signed (not before it was despatched, and only once).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="deliveryReference">The despatch reference.</param>
    /// <param name="deliveredOn">When it arrived.</param>
    /// <param name="receivedBy">Who signed.</param>
    /// <param name="podReference">The signed POD reference.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or an error.</returns>
    Task<Result> ConfirmDeliveryAsync(long companyId, string deliveryReference, DateOnly deliveredOn, string receivedBy, string? podReference, CancellationToken ct = default);
}
