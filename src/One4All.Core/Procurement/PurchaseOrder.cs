using One4All.Core.Accounting;
using One4All.Core.Common;

namespace One4All.Core.Procurement;

/// <summary>The status of a purchase order (brief §4.3).</summary>
public enum PurchaseOrderStatus
{
    /// <summary>Open — awaiting receipt.</summary>
    Open = 0,

    /// <summary>Partially received.</summary>
    PartReceived = 1,

    /// <summary>Fully received.</summary>
    Received = 2,

    /// <summary>Closed (fully received and invoiced).</summary>
    Closed = 3,

    /// <summary>Cancelled.</summary>
    Cancelled = 4,
}

/// <summary>
/// A purchase order (brief §4.3, <c>pop.purchase_order</c>). Raised from an approved requisition or directly.
/// Its lines track ordered, received and invoiced quantities, which drive the goods-received accrual (GRNI)
/// and the three-way invoice match.
/// </summary>
public sealed class PurchaseOrder : AuditableEntity, ICompanyScoped
{
    private readonly List<PurchaseOrderLine> _lines = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private PurchaseOrder()
    {
        OrderNumber = string.Empty;
    }

    /// <summary>Creates a purchase order.</summary>
    /// <param name="companyId">The owning company.</param>
    /// <param name="supplierId">The supplier.</param>
    /// <param name="orderNumber">The order number.</param>
    /// <param name="orderDate">The order date.</param>
    /// <param name="currency">The currency.</param>
    public PurchaseOrder(long companyId, long supplierId, string orderNumber, DateOnly orderDate, CurrencyCode currency)
    {
        CompanyId = companyId;
        SupplierId = supplierId;
        OrderNumber = orderNumber;
        OrderDate = orderDate;
        Currency = currency;
        Status = PurchaseOrderStatus.Open;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The supplier.</summary>
    public long SupplierId { get; private set; }

    /// <summary>The order number.</summary>
    public string OrderNumber { get; private set; }

    /// <summary>The order date.</summary>
    public DateOnly OrderDate { get; private set; }

    /// <summary>When the goods are required; deliveries after it count as late on the supplier scorecard.</summary>
    public DateOnly? RequiredBy { get; set; }

    /// <summary>The currency.</summary>
    public CurrencyCode Currency { get; private set; }

    /// <summary>The status.</summary>
    public PurchaseOrderStatus Status { get; private set; }

    /// <summary>The order lines.</summary>
    public IReadOnlyList<PurchaseOrderLine> Lines => _lines.AsReadOnly();

    /// <summary>The total ordered value (net).</summary>
    public decimal TotalNet => _lines.Sum(l => l.OrderedQuantity * l.UnitPrice);

    /// <summary>Adds an order line.</summary>
    /// <param name="itemId">The item.</param>
    /// <param name="description">The line description.</param>
    /// <param name="quantity">The quantity ordered.</param>
    /// <param name="unitPrice">The unit price.</param>
    /// <returns>The created line.</returns>
    public PurchaseOrderLine AddLine(long itemId, string description, decimal quantity, decimal unitPrice)
    {
        PurchaseOrderLine line = new(itemId, description, quantity, unitPrice);
        _lines.Add(line);
        return line;
    }

    /// <summary>
    /// Cancels the order. Refused once anything has been received (the goods are in stock; return them to
    /// the supplier first) or the order is already cancelled.
    /// </summary>
    /// <returns>Success or the rule violated.</returns>
    public Result Cancel()
    {
        if (Status == PurchaseOrderStatus.Cancelled)
        {
            return Result.Failure(Error.Rule("Pop.AlreadyCancelled", $"Purchase order {OrderNumber} is already cancelled."));
        }

        if (_lines.Any(l => l.ReceivedQuantity > 0m))
        {
            return Result.Failure(Error.Rule("Pop.Received", $"Purchase order {OrderNumber} has received lines and cannot be cancelled."));
        }

        Status = PurchaseOrderStatus.Cancelled;
        return Result.Success();
    }

    /// <summary>Recomputes the order status from its lines' received/invoiced quantities.</summary>
    public void RecomputeStatus()
    {
        if (Status == PurchaseOrderStatus.Cancelled)
        {
            return;
        }

        if (_lines.All(l => l.InvoicedQuantity >= l.OrderedQuantity))
        {
            Status = PurchaseOrderStatus.Closed;
        }
        else if (_lines.All(l => l.ReceivedQuantity >= l.OrderedQuantity))
        {
            Status = PurchaseOrderStatus.Received;
        }
        else if (_lines.Any(l => l.ReceivedQuantity > 0m))
        {
            Status = PurchaseOrderStatus.PartReceived;
        }
        else
        {
            Status = PurchaseOrderStatus.Open;
        }
    }
}

/// <summary>A line of a <see cref="PurchaseOrder"/> (<c>pop.purchase_order_line</c>).</summary>
public sealed class PurchaseOrderLine : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private PurchaseOrderLine() => Description = string.Empty;

    /// <summary>Creates an order line.</summary>
    /// <param name="itemId">The item.</param>
    /// <param name="description">The description.</param>
    /// <param name="orderedQuantity">The ordered quantity.</param>
    /// <param name="unitPrice">The unit price.</param>
    public PurchaseOrderLine(long itemId, string description, decimal orderedQuantity, decimal unitPrice)
    {
        ItemId = itemId;
        Description = description;
        OrderedQuantity = orderedQuantity;
        UnitPrice = unitPrice;
    }

    /// <summary>The owning order id.</summary>
    public long PurchaseOrderId { get; private set; }

    /// <summary>The item.</summary>
    public long ItemId { get; private set; }

    /// <summary>The description.</summary>
    public string Description { get; private set; }

    /// <summary>The ordered quantity.</summary>
    public decimal OrderedQuantity { get; private set; }

    /// <summary>The unit price.</summary>
    public decimal UnitPrice { get; private set; }

    /// <summary>The quantity received so far.</summary>
    public decimal ReceivedQuantity { get; private set; }

    /// <summary>The quantity invoiced so far.</summary>
    public decimal InvoicedQuantity { get; private set; }

    /// <summary>Records a received quantity.</summary>
    /// <param name="quantity">The quantity received.</param>
    public void Receive(decimal quantity) => ReceivedQuantity += quantity;

    /// <summary>Records an invoiced quantity.</summary>
    /// <param name="quantity">The quantity invoiced.</param>
    public void Invoice(decimal quantity) => InvoicedQuantity += quantity;
}
