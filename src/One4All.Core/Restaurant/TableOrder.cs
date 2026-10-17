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

    /// <summary>Gets or sets the table's column on the floor plan grid (null: not placed).</summary>
    public int? PlanX { get; set; }

    /// <summary>Gets or sets the table's row on the floor plan grid (null: not placed).</summary>
    public int? PlanY { get; set; }

    /// <summary>Gets or sets the table's shape on the floor plan.</summary>
    public TableShape Shape { get; set; }

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

/// <summary>How an order is fulfilled (Restaurant module).</summary>
public enum OrderType
{
    /// <summary>Dine-in at a table.</summary>
    DineIn = 1,

    /// <summary>Takeaway / collection — no table.</summary>
    Takeaway = 2,

    /// <summary>Delivery to the guest — no table.</summary>
    Delivery = 3,
}

/// <summary>The dispatch state of a delivery order (Restaurant module).</summary>
public enum DeliveryStatus
{
    /// <summary>Awaiting a driver.</summary>
    Pending = 1,

    /// <summary>A driver has been assigned.</summary>
    Assigned = 2,

    /// <summary>Out for delivery.</summary>
    OutForDelivery = 3,

    /// <summary>Delivered to the customer.</summary>
    Delivered = 4,

    /// <summary>Delivery failed (customer unreachable, etc.).</summary>
    Failed = 5,
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
    /// <param name="partySize">The number of covers at the table.</param>
    public TableOrder(long companyId, long tableId, string orderNumber, long waiterUserId, DateOnly openedOn, int partySize = 1)
    {
        CompanyId = companyId;
        TableId = tableId;
        OrderNumber = orderNumber;
        WaiterUserId = waiterUserId;
        OpenedOn = openedOn;
        PartySize = partySize < 1 ? 1 : partySize;
        OrderType = OrderType.DineIn;
        Status = TableOrderStatus.Open;
    }

    /// <summary>Opens an off-premise (takeaway or delivery) order with no table.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="orderNumber">The order number.</param>
    /// <param name="orderType">The fulfilment type (takeaway or delivery).</param>
    /// <param name="waiterUserId">The staff member taking the order.</param>
    /// <param name="openedOn">The open date.</param>
    /// <param name="customerName">The customer's name, if any.</param>
    /// <param name="customerPhone">The customer's contact number, if any.</param>
    /// <param name="deliveryAddress">The delivery address (delivery orders), if any.</param>
    public TableOrder(long companyId, string orderNumber, OrderType orderType, long waiterUserId, DateOnly openedOn, string? customerName = null, string? customerPhone = null, string? deliveryAddress = null)
    {
        CompanyId = companyId;
        TableId = null;
        OrderNumber = orderNumber;
        WaiterUserId = waiterUserId;
        OpenedOn = openedOn;
        PartySize = 1;
        OrderType = orderType;
        CustomerName = customerName;
        CustomerPhone = customerPhone;
        DeliveryAddress = deliveryAddress;
        DeliveryStatus = orderType == OrderType.Delivery ? Restaurant.DeliveryStatus.Pending : null;
        Status = TableOrderStatus.Open;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The table, or null for an off-premise (takeaway/delivery) order.</summary>
    public long? TableId { get; private set; }

    /// <summary>How the order is fulfilled.</summary>
    public OrderType OrderType { get; private set; }

    /// <summary>The customer's name (off-premise orders), if any.</summary>
    public string? CustomerName { get; private set; }

    /// <summary>The customer's contact number (off-premise orders), if any.</summary>
    public string? CustomerPhone { get; private set; }

    /// <summary>The delivery address (delivery orders), if any.</summary>
    public string? DeliveryAddress { get; private set; }

    /// <summary>The driver assigned to a delivery order, if any.</summary>
    public long? DriverUserId { get; private set; }

    /// <summary>The dispatch state of a delivery order (null for non-delivery orders).</summary>
    public DeliveryStatus? DeliveryStatus { get; private set; }

    /// <summary>Assigns a driver to the delivery order (moving it from Pending to Assigned).</summary>
    /// <param name="driverUserId">The driver.</param>
    public void AssignDriver(long driverUserId)
    {
        DriverUserId = driverUserId;
        if (DeliveryStatus == Restaurant.DeliveryStatus.Pending)
        {
            DeliveryStatus = Restaurant.DeliveryStatus.Assigned;
        }
    }

    /// <summary>Advances the delivery dispatch state (forward only; Failed allowed from any active state).</summary>
    /// <param name="status">The new dispatch state.</param>
    /// <returns>True when applied; false when it would move backwards or the order is not a live delivery.</returns>
    public bool AdvanceDelivery(DeliveryStatus status)
    {
        if (DeliveryStatus is not { } current || current is Restaurant.DeliveryStatus.Delivered or Restaurant.DeliveryStatus.Failed)
        {
            return false;
        }

        if (status != Restaurant.DeliveryStatus.Failed && (int)status <= (int)current)
        {
            return false;
        }

        DeliveryStatus = status;
        return true;
    }

    /// <summary>The order number.</summary>
    public string OrderNumber { get; private set; }

    /// <summary>The waiter.</summary>
    public long WaiterUserId { get; private set; }

    /// <summary>The number of covers at the table.</summary>
    public int PartySize { get; private set; }

    /// <summary>The open date.</summary>
    public DateOnly OpenedOn { get; private set; }

    /// <summary>The status.</summary>
    public TableOrderStatus Status { get; private set; }

    /// <summary>The GL journal number for the bill (revenue).</summary>
    public string? JournalNumber { get; set; }

    /// <summary>An optional pre-authorised spend limit (bar tab); items that would exceed the net limit are refused.</summary>
    public decimal? SpendLimit { get; private set; }

    /// <summary>Sets or clears the pre-authorised spend limit (bar tab).</summary>
    /// <param name="limit">The net spend limit, or null to clear it.</param>
    public void SetSpendLimit(decimal? limit) => SpendLimit = limit;

    /// <summary>The order lines.</summary>
    public IReadOnlyList<TableOrderLine> Lines => _lines.AsReadOnly();

    /// <summary>The net total (tax-exclusive), excluding voided and comped lines.</summary>
    public decimal NetTotal => _lines.Where(l => !l.IsVoided && !l.IsComped).Sum(l => l.LineNet);

    /// <summary>The tax total, excluding voided and comped lines.</summary>
    public decimal TaxTotal => _lines.Where(l => !l.IsVoided && !l.IsComped).Sum(l => l.TaxAmount);

    /// <summary>The gross total.</summary>
    public decimal GrossTotal => NetTotal + TaxTotal;

    /// <summary>True when every non-voided line has been fully billed (nothing left to split off).</summary>
    public bool IsFullyBilled => _lines.Where(l => !l.IsVoided).All(l => l.RemainingQuantity <= 0m);

    /// <summary>Adds an ordered menu item.</summary>
    /// <param name="menuItemId">The menu item.</param>
    /// <param name="description">The description.</param>
    /// <param name="quantity">The quantity.</param>
    /// <param name="unitPrice">The unit price.</param>
    /// <param name="taxAmount">The tax on the line.</param>
    /// <param name="revenueAccountCode">The revenue account.</param>
    /// <param name="costOfSalesAccountCode">The cost-of-sales account.</param>
    /// <param name="course">The dining course.</param>
    /// <param name="seatNumber">The seat that ordered the item, if seat-tracked.</param>
    /// <returns>The created line.</returns>
    public TableOrderLine AddLine(long menuItemId, string description, decimal quantity, decimal unitPrice, decimal taxAmount, string revenueAccountCode, string costOfSalesAccountCode, MenuCourse course = MenuCourse.Main, int? seatNumber = null)
    {
        TableOrderLine line = new(menuItemId, description, quantity, unitPrice, taxAmount, revenueAccountCode, costOfSalesAccountCode, course, seatNumber);
        _lines.Add(line);
        return line;
    }

    /// <summary>
    /// Takes a quantity of another tab's line onto this one (brief §4.1: "transfer lines between tabs"): the same item, price, accounts, course and
    /// modifiers, its share of the tax, and whether it has gone to the kitchen — so it is not fired again.
    /// </summary>
    /// <param name="source">The line on the other tab.</param>
    /// <param name="quantity">How much of it moves (at most what is unbilled).</param>
    /// <returns>The line created here.</returns>
    public TableOrderLine TakeFrom(TableOrderLine source, decimal quantity)
    {
        ArgumentNullException.ThrowIfNull(source);
        decimal tax = source.MoveOut(quantity);
        TableOrderLine moved = AddLine(source.MenuItemId, source.Description, quantity, source.UnitPrice, tax, source.RevenueAccountCode, source.CostOfSalesAccountCode, source.Course,
            source.SeatNumber);
        foreach (OrderLineModifier modifier in source.Modifiers)
        {
            moved.AddModifier(modifier.Description, modifier.PriceDelta, modifier.IngredientItemId, modifier.IngredientQuantity);
        }

        if (source.Fired)
        {
            moved.MarkFired();
        }

        return moved;
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

    /// <summary>Reassigns the order to another waiter (shift change or section handover).</summary>
    /// <param name="waiterUserId">The new waiter.</param>
    public void Reassign(long waiterUserId) => WaiterUserId = waiterUserId;
}

/// <summary>
/// A customization applied to an order line (<c>res.order_line_modifier</c>) — e.g. "extra cheese" (+price)
/// or "no onions" (0). May consume extra stock of an ingredient on billing.
/// </summary>
public sealed class OrderLineModifier : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private OrderLineModifier() => Description = string.Empty;

    /// <summary>Creates a modifier.</summary>
    /// <param name="description">The modifier description.</param>
    /// <param name="priceDelta">The price change per unit of the line (may be negative or zero).</param>
    /// <param name="ingredientItemId">An extra stock ingredient consumed, if any.</param>
    /// <param name="ingredientQuantity">The extra ingredient quantity per unit of the line.</param>
    public OrderLineModifier(string description, decimal priceDelta, long? ingredientItemId = null, decimal ingredientQuantity = 0m)
    {
        Description = description;
        PriceDelta = priceDelta;
        IngredientItemId = ingredientItemId;
        IngredientQuantity = ingredientQuantity;
    }

    /// <summary>The owning order line id.</summary>
    public long TableOrderLineId { get; private set; }

    /// <summary>The modifier description.</summary>
    public string Description { get; private set; }

    /// <summary>The price change per unit of the line (may be negative or zero).</summary>
    public decimal PriceDelta { get; private set; }

    /// <summary>An extra stock ingredient consumed, if any.</summary>
    public long? IngredientItemId { get; private set; }

    /// <summary>The extra ingredient quantity per unit of the line.</summary>
    public decimal IngredientQuantity { get; private set; }
}

/// <summary>One ordered menu item on a table order (<c>res.order_line</c>).</summary>
public sealed class TableOrderLine : Entity
{
    private readonly List<OrderLineModifier> _modifiers = [];

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
    public TableOrderLine(long menuItemId, string description, decimal quantity, decimal unitPrice, decimal taxAmount, string revenueAccountCode, string costOfSalesAccountCode, MenuCourse course = MenuCourse.Main, int? seatNumber = null)
    {
        MenuItemId = menuItemId;
        Description = description;
        Quantity = quantity;
        UnitPrice = unitPrice;
        TaxAmount = taxAmount;
        RevenueAccountCode = revenueAccountCode;
        CostOfSalesAccountCode = costOfSalesAccountCode;
        Course = course;
        SeatNumber = seatNumber;
    }

    /// <summary>The owning order id.</summary>
    public long TableOrderId { get; private set; }

    /// <summary>A note for the kitchen (no onions, well done), printed under the dish on the kitchen ticket.</summary>
    public string? Note { get; set; }

    /// <summary>The menu item.</summary>
    public long MenuItemId { get; private set; }

    /// <summary>The dining course this item belongs to (for course-by-course firing).</summary>
    public MenuCourse Course { get; private set; }

    /// <summary>The seat that ordered the item, if seat-tracked (for split-by-seat billing).</summary>
    public int? SeatNumber { get; private set; }

    /// <summary>Assigns the line to a seat (or clears it with null).</summary>
    /// <param name="seatNumber">The seat, or null to clear.</param>
    public void AssignSeat(int? seatNumber) => SeatNumber = seatNumber;

    /// <summary>The description.</summary>
    public string Description { get; private set; }

    /// <summary>The quantity.</summary>
    public decimal Quantity { get; private set; }

    /// <summary>The unit price.</summary>
    public decimal UnitPrice { get; private set; }

    /// <summary>The tax on the line.</summary>
    public decimal TaxAmount { get; private set; }

    /// <summary>The modifiers applied to this line.</summary>
    public IReadOnlyList<OrderLineModifier> Modifiers => _modifiers.AsReadOnly();

    /// <summary>The total per-unit price change from modifiers.</summary>
    public decimal ModifierDelta => _modifiers.Sum(m => m.PriceDelta);

    /// <summary>The effective unit price including modifiers.</summary>
    public decimal EffectiveUnitPrice => UnitPrice + ModifierDelta;

    /// <summary>The net line value (quantity × effective unit price, including modifiers).</summary>
    public decimal LineNet => decimal.Round(Quantity * EffectiveUnitPrice, 4, MidpointRounding.ToEven);

    /// <summary>Adds a modifier to the line.</summary>
    /// <param name="description">The modifier description.</param>
    /// <param name="priceDelta">The price change per unit of the line.</param>
    /// <param name="ingredientItemId">An extra stock ingredient consumed, if any.</param>
    /// <param name="ingredientQuantity">The extra ingredient quantity per unit of the line.</param>
    /// <returns>The created modifier.</returns>
    public OrderLineModifier AddModifier(string description, decimal priceDelta, long? ingredientItemId = null, decimal ingredientQuantity = 0m)
    {
        OrderLineModifier modifier = new(description, priceDelta, ingredientItemId, ingredientQuantity);
        _modifiers.Add(modifier);
        return modifier;
    }

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

    /// <summary>True when the line has been voided (removed before billing — never sold).</summary>
    public bool IsVoided { get; private set; }

    /// <summary>The void reason, if voided.</summary>
    public string? VoidReason { get; private set; }

    /// <summary>True when the line is comped (given free — still made, so it consumes stock, but no revenue).</summary>
    public bool IsComped { get; private set; }

    /// <summary>The comp reason, if comped.</summary>
    public string? CompReason { get; private set; }

    /// <summary>The manager/user who authorised the void or comp, if recorded.</summary>
    public long? AuthorizedByUserId { get; private set; }

    /// <summary>Voids the line (it will not be billed and does not consume stock).</summary>
    /// <param name="reason">The void reason.</param>
    /// <param name="authorizedByUserId">The authorising manager, if recorded.</param>
    public void Void(string reason, long? authorizedByUserId = null)
    {
        IsVoided = true;
        VoidReason = reason;
        AuthorizedByUserId = authorizedByUserId;
    }

    /// <summary>Comps the line (free to the guest — still made and served, so cost of sales is recognised).</summary>
    /// <param name="reason">The comp reason.</param>
    /// <param name="authorizedByUserId">The authorising manager, if recorded.</param>
    public void Comp(string reason, long? authorizedByUserId = null)
    {
        IsComped = true;
        CompReason = reason;
        AuthorizedByUserId = authorizedByUserId;
    }

    /// <summary>Takes a quantity off this line for another tab, with its share of the tax.</summary>
    /// <param name="quantity">The quantity moving (above zero, at most what is unbilled).</param>
    /// <returns>The tax that moves with it.</returns>
    /// <exception cref="InvalidOperationException">Thrown for a voided or comped line, or more than is unbilled.</exception>
    public decimal MoveOut(decimal quantity)
    {
        if (IsVoided || IsComped || quantity <= 0m || quantity > RemainingQuantity + 0.0001m)
        {
            throw new InvalidOperationException($"Cannot move {quantity} of '{Description}': it is voided or comped, or only {RemainingQuantity} is unbilled.");
        }

        decimal tax = Quantity == 0m ? 0m : decimal.Round(TaxAmount * quantity / Quantity, 4, MidpointRounding.ToEven);
        Quantity = decimal.Round(Quantity - quantity, 4, MidpointRounding.ToEven);
        TaxAmount -= tax;
        return tax;
    }

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

/// <summary>How a table is drawn on the floor plan.</summary>
public enum TableShape
{
    /// <summary>A square or rectangular table.</summary>
    Square = 0,

    /// <summary>A round table.</summary>
    Round = 1,

    /// <summary>A booth.</summary>
    Booth = 2,

    /// <summary>A bar counter seat.</summary>
    BarSeat = 3,
}
