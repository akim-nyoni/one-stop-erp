using One4All.Core.Accounting;
using One4All.Core.Common;

namespace One4All.Core.SalesOrders;

/// <summary>The status of a sales order (brief §4.13).</summary>
public enum SalesOrderStatus
{
    /// <summary>Open - nothing despatched.</summary>
    Open = 0,

    /// <summary>Partially despatched.</summary>
    PartDespatched = 1,

    /// <summary>Fully despatched.</summary>
    Despatched = 2,

    /// <summary>Fully invoiced.</summary>
    Invoiced = 3,

    /// <summary>Cancelled.</summary>
    Cancelled = 4,
}

/// <summary>
/// A sales order (brief §4.13, <c>sop.sales_order</c>): quotation → order → despatch → invoice. Lines track
/// ordered, despatched and invoiced quantities so back-orders and part-despatch are visible; despatch issues
/// stock (cost of sales), and invoicing raises the AR invoice for the despatched-but-uninvoiced quantity.
/// </summary>
public sealed class SalesOrder : AuditableEntity, ICompanyScoped
{
    private readonly List<SalesOrderLine> _lines = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private SalesOrder() => OrderNumber = string.Empty;

    /// <summary>Creates a sales order.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="customerId">The customer.</param>
    /// <param name="orderNumber">The order number.</param>
    /// <param name="orderDate">The order date.</param>
    /// <param name="currency">The currency.</param>
    public SalesOrder(long companyId, long customerId, string orderNumber, DateOnly orderDate, CurrencyCode currency)
    {
        CompanyId = companyId;
        CustomerId = customerId;
        OrderNumber = orderNumber;
        OrderDate = orderDate;
        Currency = currency;
        Status = SalesOrderStatus.Open;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The customer.</summary>
    public long CustomerId { get; private set; }

    /// <summary>The order number.</summary>
    public string OrderNumber { get; private set; }

    /// <summary>The order date.</summary>
    public DateOnly OrderDate { get; private set; }

    /// <summary>The currency.</summary>
    public CurrencyCode Currency { get; private set; }

    /// <summary>The status.</summary>
    public SalesOrderStatus Status { get; private set; }

    /// <summary>Gets or sets the date the customer asked to receive the goods (for on-time-in-full measurement).</summary>
    public DateOnly? RequestedDeliveryDate { get; set; }

    /// <summary>Gets a value indicating whether the order waits for its discount to be approved (nothing is despatched until it is).</summary>
    public bool AwaitingDiscountApproval { get; private set; }

    /// <summary>Gets who authorised a discount beyond the company's limit.</summary>
    public long? DiscountAuthorisedByUserId { get; private set; }

    /// <summary>Holds the order until its discount is approved.</summary>
    public void HoldForDiscount() => AwaitingDiscountApproval = true;

    /// <summary>Records the discount's approval and releases the order.</summary>
    /// <param name="userId">Who approved it.</param>
    public void ApproveDiscount(long userId)
    {
        AwaitingDiscountApproval = false;
        DiscountAuthorisedByUserId = userId;
    }

    /// <summary>The order lines.</summary>
    public IReadOnlyList<SalesOrderLine> Lines => _lines.AsReadOnly();

    /// <summary>The amendment version (1 as first entered; each amendment adds one, the earlier version kept as a revision).</summary>
    public int Version { get; private set; } = 1;

    /// <summary>Who last amended it.</summary>
    public long? LastAmendedByUserId { get; private set; }

    /// <summary>The order's value net of tax.</summary>
    public decimal TotalNet => _lines.Sum(l => l.OrderedQuantity * l.UnitPrice);

    /// <summary>
    /// Amends a line: its quantity can never go below what has been despatched or invoiced, and its price cannot change once anything on it
    /// has been invoiced (brief §4.13 order amendment history).
    /// </summary>
    /// <param name="lineId">The line.</param>
    /// <param name="quantity">The new ordered quantity.</param>
    /// <param name="unitPrice">The new unit price.</param>
    /// <returns>Success or the rule broken.</returns>
    public Result AmendLine(long lineId, decimal quantity, decimal unitPrice)
    {
        Result allowed = CheckAmendment(lineId, quantity, unitPrice);
        if (allowed.IsFailure)
        {
            return allowed;
        }

        _lines.First(l => l.Id == lineId).Amend(quantity, unitPrice);
        return Result.Success();
    }

    /// <summary>Whether a line may be amended so, without changing it (lets a multi-line amendment be checked whole before any line moves).</summary>
    /// <param name="lineId">The line.</param>
    /// <param name="quantity">The new ordered quantity.</param>
    /// <param name="unitPrice">The new unit price.</param>
    /// <returns>Success or the rule broken.</returns>
    public Result CheckAmendment(long lineId, decimal quantity, decimal unitPrice)
    {
        SalesOrderLine? line = _lines.FirstOrDefault(l => l.Id == lineId);
        if (line is null)
        {
            return Result.Failure(Error.NotFound("Sop.LineNotFound", $"Order line {lineId} not found."));
        }

        decimal floor = Math.Max(line.DespatchedQuantity, line.InvoicedQuantity);
        if (quantity <= 0m || quantity < floor || unitPrice < 0m)
        {
            return Result.Failure(Error.Rule("Sop.AmendQuantity", $"Order line '{line.Description}' cannot go below the {floor:0.###} already despatched or invoiced."));
        }

        if (unitPrice != line.UnitPrice && line.InvoicedQuantity > 0m)
        {
            return Result.Failure(Error.Rule("Sop.AmendPrice", $"Order line '{line.Description}' has been invoiced, so its price cannot change."));
        }

        return Result.Success();
    }

    /// <summary>Records an amendment: the next version.</summary>
    /// <param name="userId">Who amended it.</param>
    public void MarkAmended(long userId)
    {
        Version++;
        LastAmendedByUserId = userId;
    }

    /// <summary>Adds an order line.</summary>
    /// <param name="itemId">The item.</param>
    /// <param name="description">The description.</param>
    /// <param name="quantity">The quantity ordered.</param>
    /// <param name="unitPrice">The unit price.</param>
    /// <param name="taxCode">The tax code.</param>
    /// <param name="revenueAccountCode">The revenue account.</param>
    /// <returns>The created line.</returns>
    public SalesOrderLine AddLine(long itemId, string description, decimal quantity, decimal unitPrice, string? taxCode, string revenueAccountCode)
    {
        SalesOrderLine line = new(itemId, description, quantity, unitPrice, taxCode, revenueAccountCode);
        _lines.Add(line);
        return line;
    }

    /// <summary>
    /// Cancels the order. Refused once anything has been despatched (the goods have moved; return them
    /// first) or the order is already cancelled.
    /// </summary>
    /// <returns>Success or the rule violated.</returns>
    public Result Cancel()
    {
        if (Status == SalesOrderStatus.Cancelled)
        {
            return Result.Failure(Error.Rule("Sop.AlreadyCancelled", $"Sales order {OrderNumber} is already cancelled."));
        }

        if (_lines.Any(l => l.DespatchedQuantity > 0m))
        {
            return Result.Failure(Error.Rule("Sop.Despatched", $"Sales order {OrderNumber} has despatched lines and cannot be cancelled."));
        }

        Status = SalesOrderStatus.Cancelled;
        foreach (SalesOrderLine line in _lines)
        {
            line.ReleaseReservation();
        }

        return Result.Success();
    }

    /// <summary>Recomputes the order status from its lines.</summary>
    public void RecomputeStatus()
    {
        if (Status == SalesOrderStatus.Cancelled)
        {
            return;
        }

        if (_lines.All(l => l.InvoicedQuantity >= l.OrderedQuantity))
        {
            Status = SalesOrderStatus.Invoiced;
        }
        else if (_lines.All(l => l.DespatchedQuantity >= l.OrderedQuantity))
        {
            Status = SalesOrderStatus.Despatched;
        }
        else if (_lines.Any(l => l.DespatchedQuantity > 0m))
        {
            Status = SalesOrderStatus.PartDespatched;
        }
        else
        {
            Status = SalesOrderStatus.Open;
        }
    }
}

/// <summary>A line of a <see cref="SalesOrder"/> (<c>sop.sales_order_line</c>).</summary>
public sealed class SalesOrderLine : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private SalesOrderLine()
    {
        Description = string.Empty;
        RevenueAccountCode = string.Empty;
    }

    /// <summary>Creates an order line.</summary>
    /// <param name="itemId">The item.</param>
    /// <param name="description">The description.</param>
    /// <param name="orderedQuantity">The ordered quantity.</param>
    /// <param name="unitPrice">The unit price.</param>
    /// <param name="taxCode">The tax code.</param>
    /// <param name="revenueAccountCode">The revenue account.</param>
    public SalesOrderLine(long itemId, string description, decimal orderedQuantity, decimal unitPrice, string? taxCode, string revenueAccountCode)
    {
        ItemId = itemId;
        Description = description;
        OrderedQuantity = orderedQuantity;
        UnitPrice = unitPrice;
        TaxCode = taxCode;
        RevenueAccountCode = revenueAccountCode;
    }

    /// <summary>The owning order id.</summary>
    public long SalesOrderId { get; private set; }

    /// <summary>The item.</summary>
    public long ItemId { get; private set; }

    /// <summary>The description.</summary>
    public string Description { get; private set; }

    /// <summary>The ordered quantity.</summary>
    public decimal OrderedQuantity { get; private set; }

    /// <summary>The unit price.</summary>
    public decimal UnitPrice { get; private set; }

    /// <summary>Gets or sets the price-list price when the line was keyed below it.</summary>
    public decimal? ListUnitPrice { get; set; }

    /// <summary>The tax code.</summary>
    public string? TaxCode { get; private set; }

    /// <summary>The revenue account.</summary>
    public string RevenueAccountCode { get; private set; }

    /// <summary>The quantity despatched so far.</summary>
    public decimal DespatchedQuantity { get; private set; }

    /// <summary>The quantity invoiced so far.</summary>
    public decimal InvoicedQuantity { get; private set; }

    /// <summary>Records a despatched quantity; it comes out of the line's reservation first.</summary>
    /// <param name="quantity">The quantity.</param>
    public void Despatch(decimal quantity)
    {
        DespatchedQuantity += quantity;
        ReservedQuantity = Math.Max(0m, ReservedQuantity - quantity);
        if (ReservedQuantity == 0m)
        {
            ReservedWarehouseId = null;
        }
    }

    /// <summary>Gets the stock held for the line in <see cref="ReservedWarehouseId"/>, which no other issue may take.</summary>
    public decimal ReservedQuantity { get; private set; }

    /// <summary>Gets the warehouse the reservation is held in.</summary>
    public long? ReservedWarehouseId { get; private set; }

    /// <summary>The quantity still to despatch that is not yet reserved.</summary>
    public decimal UnreservedQuantity => Math.Max(0m, OrderedQuantity - DespatchedQuantity - ReservedQuantity);

    /// <summary>Holds more stock for the line in a warehouse (a line reserves in one warehouse).</summary>
    /// <param name="warehouseId">The warehouse.</param>
    /// <param name="quantity">The quantity.</param>
    public void Reserve(long warehouseId, decimal quantity)
    {
        ReservedWarehouseId = warehouseId;
        ReservedQuantity += quantity;
    }

    /// <summary>Releases the line's reservation.</summary>
    public void ReleaseReservation()
    {
        ReservedQuantity = 0m;
        ReservedWarehouseId = null;
    }

    /// <summary>Changes the ordered quantity and price (checked by the order).</summary>
    /// <param name="quantity">The quantity.</param>
    /// <param name="unitPrice">The unit price.</param>
    internal void Amend(decimal quantity, decimal unitPrice)
    {
        OrderedQuantity = quantity;
        UnitPrice = unitPrice;
    }

    /// <summary>Returns a despatched (not-yet-invoiced) quantity, reducing the despatched total.</summary>
    /// <param name="quantity">The quantity returned.</param>
    public void ReturnDespatch(decimal quantity) => DespatchedQuantity -= quantity;

    /// <summary>Records an invoiced quantity.</summary>
    /// <param name="quantity">The quantity.</param>
    public void Invoice(decimal quantity) => InvoicedQuantity += quantity;

    /// <summary>Gets the quantity the customer returned after it was invoiced (credited and restocked).</summary>
    public decimal ReturnedQuantity { get; private set; }

    /// <summary>Records goods returned after invoicing.</summary>
    /// <param name="quantity">The quantity.</param>
    public void ReturnInvoiced(decimal quantity) => ReturnedQuantity += quantity;

    /// <summary>The unit the line was ordered in (null: the item's own unit); quantities and prices on the line are in the item's own unit.</summary>
    public string? UnitCode { get; private set; }

    /// <summary>How many of the item's own unit one ordering unit holds (1 for the item's own unit).</summary>
    public decimal UnitFactor { get; private set; } = 1m;

    /// <summary>The quantity in the ordering unit.</summary>
    public decimal UnitQuantity => UnitFactor == 0m ? OrderedQuantity : OrderedQuantity / UnitFactor;

    /// <summary>The price per ordering unit.</summary>
    public decimal PricePerUnit => UnitPrice * UnitFactor;

    /// <summary>Records the unit the line was ordered in.</summary>
    /// <param name="unitCode">The unit (null: the item's own).</param>
    /// <param name="factor">The item's own units in one of it.</param>
    public void OrderedIn(string? unitCode, decimal factor)
    {
        UnitCode = unitCode;
        UnitFactor = factor <= 0m ? 1m : factor;
    }
}
