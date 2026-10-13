using One4All.Core.Common;

namespace One4All.Application.Abstractions.Procurement;

/// <summary>A line on a new purchase order.</summary>
/// <param name="ItemId">The item.</param>
/// <param name="Description">The description.</param>
/// <param name="Quantity">The quantity.</param>
/// <param name="UnitPrice">The unit price.</param>
public readonly record struct PurchaseOrderLineRequest(long ItemId, string Description, decimal Quantity, decimal UnitPrice, string? ExpenseAccountCode = null, long? JobContractId = null,
    string? JobCostHead = null);

/// <summary>A request to create a purchase order.</summary>
public sealed class CreatePurchaseOrderRequest
{
    /// <summary>The company.</summary>
    public long CompanyId { get; init; }

    /// <summary>The supplier.</summary>
    public long SupplierId { get; init; }

    /// <summary>The order number; blank allocates the next number from the company's <c>PO</c> sequence.</summary>
    public string OrderNumber { get; init; } = string.Empty;

    /// <summary>The order date.</summary>
    public DateOnly OrderDate { get; init; }

    /// <summary>When the goods are required, if a date is set; used to judge on-time delivery.</summary>
    public DateOnly? RequiredBy { get; init; }

    /// <summary>Gets the department the order is for: its budget is checked, and price differences post to it.</summary>
    public string? Department { get; init; }

    /// <summary>Gets the cost centre the order is for.</summary>
    public string? CostCentre { get; init; }

    /// <summary>Gets the project the order is for.</summary>
    public string? Project { get; init; }

    /// <summary>Gets who approved an overrun needing approval (they must be able to override budget blocks).</summary>
    public long? BudgetApprovedByUserId { get; init; }

    /// <summary>Gets the framework agreement the order is called off against: same supplier, within its term and its ceiling.</summary>
    public long? FrameworkAgreementId { get; init; }

    /// <summary>The currency.</summary>
    public Core.Accounting.CurrencyCode Currency { get; init; }

    /// <summary>The order lines.</summary>
    public IReadOnlyList<PurchaseOrderLineRequest> Lines { get; init; } = [];

    /// <summary>Who raises the order (its workflow's initiator, who cannot approve it).</summary>
    public long RaisedByUserId { get; init; }
}

/// <summary>A received line on a GRN.</summary>
/// <param name="PurchaseOrderLineId">The PO line.</param>
/// <param name="Quantity">The quantity received.</param>
public readonly record struct ReceiveLine(long PurchaseOrderLineId, decimal Quantity, IReadOnlyList<One4All.Application.Inventory.LotQuantity>? Lots = null);

/// <summary>A request to receive goods against a purchase order.</summary>
public sealed class ReceiveGoodsRequest
{
    /// <summary>The company.</summary>
    public long CompanyId { get; init; }

    /// <summary>The purchase order.</summary>
    public long PurchaseOrderId { get; init; }

    /// <summary>The warehouse goods are received into.</summary>
    public long WarehouseId { get; init; }

    /// <summary>The GRN number.</summary>
    public string GrnNumber { get; init; } = string.Empty;

    /// <summary>The receipt date.</summary>
    public DateOnly Date { get; init; }

    /// <summary>The received lines.</summary>
    public IReadOnlyList<ReceiveLine> Lines { get; init; } = [];

    /// <summary>The supervisor authorising a receipt beyond the over-receipt tolerance (needs Procurement › GRN authorise; not the receiver).</summary>
    public long? OverReceiptAuthorisedByUserId { get; init; }

    /// <summary>Who is receiving (refused as their own over-receipt authoriser).</summary>
    public long ReceivedByUserId { get; init; }
}

/// <summary>An invoiced line for a three-way match.</summary>
/// <param name="PurchaseOrderLineId">The PO line.</param>
/// <param name="Quantity">The invoiced quantity.</param>
/// <param name="UnitPrice">The invoiced unit price.</param>
public readonly record struct InvoiceMatchLine(long PurchaseOrderLineId, decimal Quantity, decimal UnitPrice);

/// <summary>A request to match a supplier invoice against a purchase order and its receipts.</summary>
public sealed class MatchInvoiceRequest
{
    /// <summary>The company.</summary>
    public long CompanyId { get; init; }

    /// <summary>The purchase order.</summary>
    public long PurchaseOrderId { get; init; }

    /// <summary>The supplier invoice number.</summary>
    public string InvoiceNumber { get; init; } = string.Empty;

    /// <summary>The invoice date.</summary>
    public DateOnly Date { get; init; }

    /// <summary>The allowed price tolerance as a percentage (e.g. 5 = 5%).</summary>
    public decimal TolerancePercent { get; init; } = 5m;

    /// <summary>Gets who approved a budget overrun on the invoice, if it needs approval.</summary>
    public long? BudgetApprovedByUserId { get; init; }

    /// <summary>
    /// Where the difference between the invoice and order prices goes (purchase price variance). Required when the
    /// invoice is priced differently from the order: GRNI clears at the order price and AP takes the invoice price.
    /// </summary>
    public string? PriceVarianceAccountCode { get; init; }

    /// <summary>The invoiced lines.</summary>
    public IReadOnlyList<InvoiceMatchLine> Lines { get; init; } = [];
}

/// <summary>The result of creating a purchase order.</summary>
/// <param name="PurchaseOrderId">The created order id.</param>
/// <param name="OrderNumber">The order number.</param>
public readonly record struct PurchaseOrderResult(long PurchaseOrderId, string OrderNumber);

/// <summary>The result of receiving goods.</summary>
/// <param name="GrnId">The created GRN id.</param>
/// <param name="AccruedValue">The value accrued to GRNI.</param>
public readonly record struct GrnResult(long GrnId, decimal AccruedValue);

/// <summary>The result of a three-way match.</summary>
/// <param name="OpenItemId">The created AP open item id.</param>
/// <param name="JournalNumber">The GL journal number.</param>
/// <param name="InvoiceValue">The invoice value posted to AP.</param>
public readonly record struct MatchInvoiceResult(long OpenItemId, string JournalNumber, decimal InvoiceValue);

/// <summary>The result of a goods return to a supplier.</summary>
/// <param name="OpenItemId">The created AP debit-note open item (reduces the payable).</param>
/// <param name="JournalNumber">The GL journal number.</param>
/// <param name="StockValue">The value removed from stock at moving-average cost.</param>
/// <param name="CreditValue">The credit raised against the supplier (quantity × return price).</param>
/// <param name="PriceVariance">Credit value − stock value; positive is a gain (credited to variance), negative a loss.</param>
public readonly record struct SupplierReturnResult(long OpenItemId, string JournalNumber, decimal StockValue, decimal CreditValue, decimal PriceVariance);

/// <summary>
/// Procurement service (brief §4.3): purchase orders, goods-received notes (GRNI accrual through the
/// inventory receipt), and the three-way invoice match (PO ↔ GRN ↔ invoice) that clears GRNI to accounts
/// payable within a price/quantity tolerance. Requisition approval routes through the generic workflow engine.
/// </summary>
public interface IProcurementService
{
    /// <summary>Creates a purchase order.</summary>
    /// <param name="request">The request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The order result.</returns>
    Task<Result<PurchaseOrderResult>> CreatePurchaseOrderAsync(CreatePurchaseOrderRequest request, CancellationToken ct = default);

    /// <summary>
    /// Cancels a purchase order, releasing its inbound (on-order) quantity. Refused once anything has been
    /// received (<c>Pop.Received</c>) or when already cancelled; requires a reason.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="purchaseOrderId">The purchase order.</param>
    /// <param name="reason">The mandatory reason.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or an error.</returns>
    Task<Result> CancelPurchaseOrderAsync(long companyId, long purchaseOrderId, string reason, CancellationToken ct = default);

    /// <summary>Receives goods against a purchase order, accruing GRNI and updating stock.</summary>
    /// <param name="request">The request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The GRN result.</returns>
    Task<Result<GrnResult>> ReceiveGoodsAsync(ReceiveGoodsRequest request, CancellationToken ct = default);

    /// <summary>Matches a supplier invoice (three-way) and clears GRNI to AP within tolerance.</summary>
    /// <param name="request">The request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The match result, or an error when the match is out of tolerance.</returns>
    Task<Result<MatchInvoiceResult>> MatchInvoiceAsync(MatchInvoiceRequest request, CancellationToken ct = default);

    /// <summary>
    /// Returns received goods to a supplier: removes the quantity from stock at the current moving-average cost
    /// (Cr stock control), raises an allocatable AP debit note at the agreed return price (Dr AP control), and
    /// books any difference between the return price and the average cost to a purchase-price-variance account.
    /// The debit note reduces the supplier balance and can be allocated against the original invoice. Refuses a
    /// non-positive quantity, a negative return price, an unknown supplier/item, or a return exceeding the
    /// quantity on hand.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="supplierId">The supplier the goods go back to.</param>
    /// <param name="itemId">The stock item.</param>
    /// <param name="warehouseId">The warehouse the goods leave.</param>
    /// <param name="quantity">The quantity returned (positive).</param>
    /// <param name="returnUnitPrice">The agreed per-unit credit price (usually the original PO price).</param>
    /// <param name="date">The return date.</param>
    /// <param name="documentNumber">The return document number.</param>
    /// <param name="priceVarianceAccountCode">The purchase-price-variance account for any divergence from average cost.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The return result.</returns>
    Task<Result<SupplierReturnResult>> ReturnToSupplierAsync(long companyId, long supplierId, long itemId, long warehouseId, decimal quantity, decimal returnUnitPrice, DateOnly date, string documentNumber, string priceVarianceAccountCode, CancellationToken ct = default);
}
