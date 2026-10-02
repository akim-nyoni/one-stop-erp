using One4All.Core.Accounting;
using One4All.Core.Common;

namespace One4All.Core.SalesOrders;

/// <summary>The status of a sales order (brief §4.13).</summary>
public enum SalesOrderStatus
{
    /// <summary>Open — nothing despatched.</summary>
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

    /// <summary>The order lines.</summary>
    public IReadOnlyList<SalesOrderLine> Lines => _lines.AsReadOnly();

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

    /// <summary>The tax code.</summary>
    public string? TaxCode { get; private set; }

    /// <summary>The revenue account.</summary>
    public string RevenueAccountCode { get; private set; }

    /// <summary>The quantity despatched so far.</summary>
    public decimal DespatchedQuantity { get; private set; }

    /// <summary>The quantity invoiced so far.</summary>
    public decimal InvoicedQuantity { get; private set; }

    /// <summary>Records a despatched quantity.</summary>
    /// <param name="quantity">The quantity.</param>
    public void Despatch(decimal quantity) => DespatchedQuantity += quantity;

    /// <summary>Returns a despatched (not-yet-invoiced) quantity, reducing the despatched total.</summary>
    /// <param name="quantity">The quantity returned.</param>
    public void ReturnDespatch(decimal quantity) => DespatchedQuantity -= quantity;

    /// <summary>Records an invoiced quantity.</summary>
    /// <param name="quantity">The quantity.</param>
    public void Invoice(decimal quantity) => InvoicedQuantity += quantity;
}
