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

    /// <summary>Gets or sets the framework agreement the order is called off against.</summary>
    public long? FrameworkAgreementId { get; set; }

    /// <summary>The version: 1 as raised, one more for each amendment.</summary>
    public int Version { get; private set; } = 1;

    /// <summary>Whether an amendment is waiting for re-approval; nothing can be received against it meanwhile.</summary>
    public bool AwaitingReapproval { get; private set; }

    /// <summary>Who made the latest amendment.</summary>
    public long? LastAmendedByUserId { get; private set; }

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

    /// <summary>
    /// Amends a line's quantity and price. The quantity cannot fall below what has been received, and the price cannot
    /// change once anything on the line has been invoiced.
    /// </summary>
    /// <param name="lineId">The line.</param>
    /// <param name="quantity">The new ordered quantity.</param>
    /// <param name="unitPrice">The new unit price.</param>
    /// <returns>Success or the rule broken.</returns>
    public Result AmendLine(long lineId, decimal quantity, decimal unitPrice)
    {
        PurchaseOrderLine? line = _lines.FirstOrDefault(l => l.Id == lineId);
        if (line is null)
        {
            return Result.Failure(Error.NotFound("Pop.LineNotFound", $"PO line {lineId} not found."));
        }

        if (quantity < line.ReceivedQuantity || quantity <= 0m || unitPrice < 0m)
        {
            return Result.Failure(Error.Rule("Pop.AmendQuantity", $"PO line {lineId} cannot be ordered below the {line.ReceivedQuantity} already received."));
        }

        if (unitPrice != line.UnitPrice && line.InvoicedQuantity > 0m)
        {
            return Result.Failure(Error.Rule("Pop.AmendPrice", $"PO line {lineId} has been invoiced, so its price cannot change."));
        }

        line.Amend(quantity, unitPrice);
        return Result.Success();
    }

    /// <summary>Records an amendment: a new version, held for re-approval when it needs one.</summary>
    /// <param name="userId">Who amended it.</param>
    /// <param name="needsReapproval">Whether the change needs re-approval before more goods are received.</param>
    public void MarkAmended(long userId, bool needsReapproval)
    {
        Version++;
        LastAmendedByUserId = userId;
        AwaitingReapproval = AwaitingReapproval || needsReapproval;
        RecomputeStatus();
    }

    /// <summary>Re-approves the amended order; never by the person who amended it.</summary>
    /// <param name="userId">The approver.</param>
    /// <returns>Success or the rule broken.</returns>
    public Result Reapprove(long userId)
    {
        if (!AwaitingReapproval)
        {
            return Result.Failure(Error.Rule("Pop.NotAwaitingReapproval", $"Purchase order {OrderNumber} is not awaiting re-approval."));
        }

        if (userId == LastAmendedByUserId)
        {
            return Result.Failure(Error.Rule("Pop.SelfApproval", "A different user must re-approve an amendment than the one who made it."));
        }

        AwaitingReapproval = false;
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

    /// <summary>Gets or sets the expense account the line is budgeted (and committed) against, if any.</summary>
    public string? ExpenseAccountCode { get; set; }

    /// <summary>Records a received quantity.</summary>
    /// <param name="quantity">The quantity received.</param>
    public void Receive(decimal quantity) => ReceivedQuantity += quantity;

    /// <summary>Records an invoiced quantity.</summary>
    /// <param name="quantity">The quantity invoiced.</param>
    public void Invoice(decimal quantity) => InvoicedQuantity += quantity;

    /// <summary>Changes the ordered quantity and price (through <see cref="PurchaseOrder.AmendLine"/>).</summary>
    /// <param name="quantity">The new quantity.</param>
    /// <param name="unitPrice">The new price.</param>
    internal void Amend(decimal quantity, decimal unitPrice)
    {
        OrderedQuantity = quantity;
        UnitPrice = unitPrice;
    }
}

/// <summary>
/// One scheduled delivery of a purchase order line (<c>pur.po_delivery</c>): the line's quantity spread over due dates. Receipts are
/// matched to the deliveries earliest-due first.
/// </summary>
public sealed class PurchaseOrderDelivery : Entity, ICompanyScoped
{
    private PurchaseOrderDelivery()
    {
    }

    /// <summary>Creates the delivery.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="purchaseOrderLineId">The order line.</param>
    /// <param name="dueDate">When it is due.</param>
    /// <param name="quantity">How much is due.</param>
    public PurchaseOrderDelivery(long companyId, long purchaseOrderLineId, DateOnly dueDate, decimal quantity)
    {
        CompanyId = companyId;
        PurchaseOrderLineId = purchaseOrderLineId;
        DueDate = dueDate;
        Quantity = quantity;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the order line.</summary>
    public long PurchaseOrderLineId { get; private set; }

    /// <summary>Gets when it is due.</summary>
    public DateOnly DueDate { get; private set; }

    /// <summary>Gets how much is due.</summary>
    public decimal Quantity { get; private set; }
}
