using One4All.Core.Common;

namespace One4All.Core.Restaurant;

/// <summary>The state of a restaurant table (Restaurant module).</summary>
public enum TableStatus
{
    /// <summary>Free.</summary>
    Free = 1,

    /// <summary>Occupied — has an open order.</summary>
    Occupied = 2,
}

/// <summary>A restaurant table (<c>res.table</c>).</summary>
public sealed class RestaurantTable : SoftDeletableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private RestaurantTable() => TableNumber = string.Empty;

    /// <summary>Creates a table.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="tableNumber">The table number/label.</param>
    /// <param name="seats">The number of seats.</param>
    public RestaurantTable(long companyId, string tableNumber, int seats)
    {
        CompanyId = companyId;
        TableNumber = tableNumber.Trim().ToUpperInvariant();
        Seats = seats;
        Status = TableStatus.Free;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The table number/label.</summary>
    public string TableNumber { get; private set; }

    /// <summary>The number of seats.</summary>
    public int Seats { get; set; }

    /// <summary>The status.</summary>
    public TableStatus Status { get; private set; }

    /// <summary>The active table group this table is combined into, if any.</summary>
    public long? GroupId { get; private set; }

    /// <summary>The floor-plan section this table sits in, if any.</summary>
    public long? SectionId { get; private set; }

    /// <summary>Assigns the table to a floor-plan section (or clears it with null).</summary>
    /// <param name="sectionId">The section, or null to unassign.</param>
    public void AssignToSection(long? sectionId) => SectionId = sectionId;

    /// <summary>Marks the table occupied.</summary>
    public void Occupy() => Status = TableStatus.Occupied;

    /// <summary>Frees the table.</summary>
    public void Free() => Status = TableStatus.Free;

    /// <summary>Combines the table into a group (physically pushed together with others).</summary>
    /// <param name="groupId">The table group.</param>
    public void JoinGroup(long groupId) => GroupId = groupId;

    /// <summary>Removes the table from its group.</summary>
    public void LeaveGroup() => GroupId = null;
}

/// <summary>A dining course (Restaurant module) — the order items are fired to the kitchen course by course.</summary>
public enum MenuCourse
{
    /// <summary>Drinks — usually fired first.</summary>
    Drinks = 1,

    /// <summary>Starter / appetiser.</summary>
    Starter = 2,

    /// <summary>Main course.</summary>
    Main = 3,

    /// <summary>Dessert.</summary>
    Dessert = 4,
}

/// <summary>The lifecycle of a table order (Restaurant module).</summary>
public enum TableOrderStatus
{
    /// <summary>Open — items being added.</summary>
    Open = 1,

    /// <summary>Billed &amp; settled — revenue and cost of sales posted.</summary>
    Billed = 2,

    /// <summary>Cancelled.</summary>
    Cancelled = 3,
}

/// <summary>
/// A table order (<c>res.order</c>): the guest's running tab of menu items at a table. On billing, revenue
/// is recognised at the menu prices and cost of sales by exploding each item's recipe into stock issues.
/// </summary>
public sealed class TableOrder : AuditableEntity, ICompanyScoped
{
    private readonly List<TableOrderLine> _lines = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private TableOrder() => OrderNumber = string.Empty;

    /// <summary>Opens a table order.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="tableId">The table.</param>
    /// <param name="orderNumber">The order number.</param>
    /// <param name="waiterUserId">The waiter.</param>
    /// <param name="openedOn">The open date.</param>
    public TableOrder(long companyId, long tableId, string orderNumber, long waiterUserId, DateOnly openedOn)
    {
        CompanyId = companyId;
        TableId = tableId;
        OrderNumber = orderNumber;
        WaiterUserId = waiterUserId;
        OpenedOn = openedOn;
        Status = TableOrderStatus.Open;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The table.</summary>
    public long TableId { get; private set; }

    /// <summary>The order number.</summary>
    public string OrderNumber { get; private set; }

    /// <summary>The waiter.</summary>
    public long WaiterUserId { get; private set; }

    /// <summary>The open date.</summary>
    public DateOnly OpenedOn { get; private set; }

    /// <summary>The status.</summary>
    public TableOrderStatus Status { get; private set; }

    /// <summary>The GL journal number for the bill (revenue).</summary>
    public string? JournalNumber { get; set; }

    /// <summary>The order lines.</summary>
    public IReadOnlyList<TableOrderLine> Lines => _lines.AsReadOnly();

    /// <summary>The net total (tax-exclusive).</summary>
    public decimal NetTotal => _lines.Sum(l => l.LineNet);

    /// <summary>The tax total.</summary>
    public decimal TaxTotal => _lines.Sum(l => l.TaxAmount);

    /// <summary>The gross total.</summary>
    public decimal GrossTotal => NetTotal + TaxTotal;

    /// <summary>True when every line has been fully billed (nothing left to split off).</summary>
    public bool IsFullyBilled => _lines.All(l => l.RemainingQuantity <= 0m);

    /// <summary>Adds an ordered menu item.</summary>
    /// <param name="menuItemId">The menu item.</param>
    /// <param name="description">The description.</param>
    /// <param name="quantity">The quantity.</param>
    /// <param name="unitPrice">The unit price.</param>
    /// <param name="taxAmount">The tax on the line.</param>
    /// <param name="revenueAccountCode">The revenue account.</param>
    /// <param name="costOfSalesAccountCode">The cost-of-sales account.</param>
    /// <param name="course">The dining course.</param>
    /// <returns>The created line.</returns>
    public TableOrderLine AddLine(long menuItemId, string description, decimal quantity, decimal unitPrice, decimal taxAmount, string revenueAccountCode, string costOfSalesAccountCode, MenuCourse course = MenuCourse.Main)
    {
        TableOrderLine line = new(menuItemId, description, quantity, unitPrice, taxAmount, revenueAccountCode, costOfSalesAccountCode, course);
        _lines.Add(line);
        return line;
    }

    /// <summary>Marks the order billed and records the revenue journal.</summary>
    /// <param name="journalNumber">The revenue journal number.</param>
    public void MarkBilled(string journalNumber)
    {
        Status = TableOrderStatus.Billed;
        JournalNumber = journalNumber;
    }

    /// <summary>Cancels the order (nothing was billed).</summary>
    public void Cancel() => Status = TableOrderStatus.Cancelled;

    /// <summary>Moves the order to another table.</summary>
    /// <param name="tableId">The new table.</param>
    public void MoveToTable(long tableId) => TableId = tableId;
}

/// <summary>One ordered menu item on a table order (<c>res.order_line</c>).</summary>
public sealed class TableOrderLine : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private TableOrderLine()
    {
        Description = string.Empty;
        RevenueAccountCode = string.Empty;
        CostOfSalesAccountCode = string.Empty;
    }

    /// <summary>Creates a line.</summary>
    /// <param name="menuItemId">The menu item.</param>
    /// <param name="description">The description.</param>
    /// <param name="quantity">The quantity.</param>
    /// <param name="unitPrice">The unit price.</param>
    /// <param name="taxAmount">The tax on the line.</param>
    /// <param name="revenueAccountCode">The revenue account.</param>
    /// <param name="costOfSalesAccountCode">The cost-of-sales account.</param>
    public TableOrderLine(long menuItemId, string description, decimal quantity, decimal unitPrice, decimal taxAmount, string revenueAccountCode, string costOfSalesAccountCode, MenuCourse course = MenuCourse.Main)
    {
        MenuItemId = menuItemId;
        Description = description;
        Quantity = quantity;
        UnitPrice = unitPrice;
        TaxAmount = taxAmount;
        RevenueAccountCode = revenueAccountCode;
        CostOfSalesAccountCode = costOfSalesAccountCode;
        Course = course;
    }

    /// <summary>The owning order id.</summary>
    public long TableOrderId { get; private set; }

    /// <summary>The menu item.</summary>
    public long MenuItemId { get; private set; }

    /// <summary>The dining course this item belongs to (for course-by-course firing).</summary>
    public MenuCourse Course { get; private set; }

    /// <summary>The description.</summary>
    public string Description { get; private set; }

    /// <summary>The quantity.</summary>
    public decimal Quantity { get; private set; }

    /// <summary>The unit price.</summary>
    public decimal UnitPrice { get; private set; }

    /// <summary>The tax on the line.</summary>
    public decimal TaxAmount { get; private set; }

    /// <summary>The net line value (quantity × unit price).</summary>
    public decimal LineNet => decimal.Round(Quantity * UnitPrice, 4, MidpointRounding.ToEven);

    /// <summary>Whether the line has been fired to the kitchen (on a kitchen order ticket).</summary>
    public bool Fired { get; private set; }

    /// <summary>Marks the line fired to the kitchen.</summary>
    public void MarkFired() => Fired = true;

    /// <summary>The quantity already billed (for split bills).</summary>
    public decimal BilledQuantity { get; private set; }

    /// <summary>The quantity not yet billed.</summary>
    public decimal RemainingQuantity => Quantity - BilledQuantity;

    /// <summary>The revenue account.</summary>
    public string RevenueAccountCode { get; private set; }

    /// <summary>The cost-of-sales account.</summary>
    public string CostOfSalesAccountCode { get; private set; }

    /// <summary>Bills a quantity of this line (split bills).</summary>
    /// <param name="quantity">The quantity billed now.</param>
    /// <exception cref="InvalidOperationException">Thrown when billing more than remains.</exception>
    public void Bill(decimal quantity)
    {
        if (quantity > RemainingQuantity + 0.0001m)
        {
            throw new InvalidOperationException($"Cannot bill {quantity}; only {RemainingQuantity} of '{Description}' remains.");
        }

        BilledQuantity = decimal.Round(BilledQuantity + quantity, 4, MidpointRounding.ToEven);
    }
}
