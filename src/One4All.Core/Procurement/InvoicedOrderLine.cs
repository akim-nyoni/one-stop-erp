using One4All.Core.Common;

namespace One4All.Core.Procurement;

/// <summary>
/// An order line matched to a supplier invoice (<c>pop.invoiced_order_line</c>): the quantity invoiced at the order price and at the invoice
/// price — the purchase price variance the supplier scorecard reports.
/// </summary>
public sealed class InvoicedOrderLine : Entity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private InvoicedOrderLine()
    {
        InvoiceNumber = string.Empty;
    }

    /// <summary>Records a matched line.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="supplierId">The supplier.</param>
    /// <param name="purchaseOrderLineId">The order line.</param>
    /// <param name="invoiceNumber">The supplier invoice.</param>
    /// <param name="date">The invoice date.</param>
    /// <param name="quantity">The quantity invoiced.</param>
    /// <param name="orderPrice">The order's unit price.</param>
    /// <param name="invoicePrice">The invoice's unit price.</param>
    public InvoicedOrderLine(long companyId, long supplierId, long purchaseOrderLineId, string invoiceNumber, DateOnly date, decimal quantity, decimal orderPrice, decimal invoicePrice)
    {
        CompanyId = companyId;
        SupplierId = supplierId;
        PurchaseOrderLineId = purchaseOrderLineId;
        InvoiceNumber = invoiceNumber;
        Date = date;
        Quantity = quantity;
        OrderPrice = orderPrice;
        InvoicePrice = invoicePrice;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>Gets the supplier.</summary>
    public long SupplierId { get; private set; }

    /// <summary>Gets the order line.</summary>
    public long PurchaseOrderLineId { get; private set; }

    /// <summary>Gets the supplier invoice.</summary>
    public string InvoiceNumber { get; private set; }

    /// <summary>Gets the invoice date.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>Gets the quantity invoiced.</summary>
    public decimal Quantity { get; private set; }

    /// <summary>Gets the order's unit price.</summary>
    public decimal OrderPrice { get; private set; }

    /// <summary>Gets the invoice's unit price.</summary>
    public decimal InvoicePrice { get; private set; }
}
