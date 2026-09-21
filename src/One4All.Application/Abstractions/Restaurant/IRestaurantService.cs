using One4All.Core.Common;
using One4All.Core.Restaurant;

namespace One4All.Application.Abstractions.Restaurant;

/// <summary>An ingredient of a menu item's recipe.</summary>
/// <param name="IngredientItemId">The stock item consumed.</param>
/// <param name="Quantity">The quantity consumed per menu item sold.</param>
public readonly record struct RecipeIngredient(long IngredientItemId, decimal Quantity);

/// <summary>The result of billing a table order (or a split of it).</summary>
/// <param name="Net">The net (tax-exclusive) total billed.</param>
/// <param name="Tax">The tax total billed.</param>
/// <param name="ServiceCharge">The service charge / gratuity (income to the house) added.</param>
/// <param name="Tip">The tip (staff money — a liability, not income) added.</param>
/// <param name="Discount">The discount allowed (a contra-revenue) deducted from the bill.</param>
/// <param name="Gross">The gross total charged (net + tax + service charge + tip − discount).</param>
/// <param name="CostOfSales">The cost of sales released by exploding the recipes.</param>
/// <param name="FullyBilled">True when this bill settled the whole order (table freed).</param>
/// <param name="RevenueJournalNumber">The revenue GL journal number.</param>
public readonly record struct BillResult(decimal Net, decimal Tax, decimal ServiceCharge, decimal Tip, decimal Discount, decimal Gross, decimal CostOfSales, bool FullyBilled, string RevenueJournalNumber);

/// <summary>The automatic gratuity (service charge) suggested for an order under the company policy.</summary>
/// <param name="Applies">True when the party qualifies and a gratuity is suggested.</param>
/// <param name="Amount">The gratuity amount (net × policy percent), rounded to 2dp.</param>
/// <param name="ServiceChargeAccountCode">The income account the gratuity should book to.</param>
public readonly record struct AutoGratuityQuote(bool Applies, decimal Amount, string? ServiceChargeAccountCode);

/// <summary>A staff member's share when distributing the tip pool.</summary>
/// <param name="StaffUserId">The staff member.</param>
/// <param name="Weight">The relative weight of their share.</param>
public readonly record struct TipShare(long StaffUserId, decimal Weight);

/// <summary>A portion of an order to bill on a split.</summary>
/// <param name="OrderLineId">The order line.</param>
/// <param name="Quantity">The quantity of that line to bill now.</param>
public readonly record struct SplitPortion(long OrderLineId, decimal Quantity);

/// <summary>
/// The optional Restaurant / Hospitality module. Sells <b>menu items</b> (composites priced to the guest,
/// never stocked) through a table-order → bill flow: on billing, revenue is recognised at the menu prices
/// and cost of sales by exploding each item's recipe into moving-average stock issues of its ingredients.
/// </summary>
public interface IRestaurantService
{
    /// <summary>Creates a menu item with its recipe.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The menu code.</param>
    /// <param name="name">The menu name.</param>
    /// <param name="price">The selling price (net).</param>
    /// <param name="revenueAccountCode">The revenue account.</param>
    /// <param name="costOfSalesAccountCode">The cost-of-sales account.</param>
    /// <param name="ingredients">The recipe ingredients.</param>
    /// <param name="taxCode">The tax code, if any.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created menu item id.</returns>
    Task<Result<long>> CreateMenuItemAsync(long companyId, string code, string name, decimal price, string revenueAccountCode, string costOfSalesAccountCode, IReadOnlyList<RecipeIngredient> ingredients, string? taxCode = null, CancellationToken ct = default);

    /// <summary>Creates a restaurant table.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="tableNumber">The table number/label.</param>
    /// <param name="seats">The number of seats.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created table id.</returns>
    Task<Result<long>> CreateTableAsync(long companyId, string tableNumber, int seats, CancellationToken ct = default);

    /// <summary>Opens an order at a table (which must be free), marking it occupied.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="tableId">The table.</param>
    /// <param name="orderNumber">The order number.</param>
    /// <param name="waiterUserId">The waiter.</param>
    /// <param name="date">The open date.</param>
    /// <param name="partySize">The number of covers (drives auto-gratuity for large parties).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created order id.</returns>
    Task<Result<long>> OpenOrderAsync(long companyId, long tableId, string orderNumber, long waiterUserId, DateOnly date, int partySize = 1, CancellationToken ct = default);

    /// <summary>
    /// Opens an off-premise order (takeaway or delivery) with no table. Items are added and billed exactly as a
    /// dine-in order, but nothing occupies the floor. Refuses <see cref="OrderType.DineIn"/> (use a table).
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="orderNumber">The order number.</param>
    /// <param name="orderType">The fulfilment type (takeaway or delivery).</param>
    /// <param name="waiterUserId">The staff member taking the order.</param>
    /// <param name="date">The open date.</param>
    /// <param name="customerName">The customer's name, if any.</param>
    /// <param name="customerPhone">The customer's contact number, if any.</param>
    /// <param name="deliveryAddress">The delivery address (delivery orders), if any.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created order id.</returns>
    Task<Result<long>> OpenOffPremiseOrderAsync(long companyId, string orderNumber, OrderType orderType, long waiterUserId, DateOnly date, string? customerName = null, string? customerPhone = null, string? deliveryAddress = null, CancellationToken ct = default);

    /// <summary>Assigns a driver to a delivery order (moving it from Pending to Assigned). Delivery orders only.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="orderId">The delivery order.</param>
    /// <param name="driverUserId">The driver.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or an error.</returns>
    Task<Result> AssignDeliveryDriverAsync(long companyId, long orderId, long driverUserId, CancellationToken ct = default);

    /// <summary>
    /// Advances a delivery order's dispatch state (Pending → Assigned → OutForDelivery → Delivered; Failed from
    /// any active state). Refuses a backward move or a change once delivered/failed.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="orderId">The delivery order.</param>
    /// <param name="status">The new dispatch state.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or an error.</returns>
    Task<Result> AdvanceDeliveryAsync(long companyId, long orderId, DeliveryStatus status, CancellationToken ct = default);

    /// <summary>Adds a menu item to an open order at its menu price.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="orderId">The order.</param>
    /// <param name="menuItemId">The menu item.</param>
    /// <param name="quantity">The quantity.</param>
    /// <param name="taxAmount">The tax on the line, if any.</param>
    /// <param name="course">The dining course the item belongs to (for course-by-course firing).</param>
    /// <param name="seatNumber">The seat that ordered the item, for split-by-seat billing (optional).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The line net value.</returns>
    Task<Result<decimal>> AddItemAsync(long companyId, long orderId, long menuItemId, decimal quantity, decimal taxAmount = 0m, MenuCourse course = MenuCourse.Main, int? seatNumber = null, CancellationToken ct = default);

    /// <summary>
    /// Adds a modifier to an order line (e.g. "extra cheese" +2, "no onions" 0). The price delta changes the
    /// line's effective price; an optional ingredient consumes extra stock on billing. Order must be open and
    /// the line not yet billed.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="orderId">The order.</param>
    /// <param name="lineId">The order line to modify.</param>
    /// <param name="description">The modifier description.</param>
    /// <param name="priceDelta">The price change per unit of the line (may be negative or zero).</param>
    /// <param name="ingredientItemId">An extra stock ingredient consumed, if any.</param>
    /// <param name="ingredientQuantity">The extra ingredient quantity per unit of the line.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The line's new net value.</returns>
    Task<Result<decimal>> AddItemModifierAsync(long companyId, long orderId, long lineId, string description, decimal priceDelta, long? ingredientItemId = null, decimal ingredientQuantity = 0m, CancellationToken ct = default);

    /// <summary>
    /// Bills an open order: posts revenue (Dr cash/Cr revenue/Cr tax) and cost of sales by exploding each
    /// line's recipe into stock issues, marks the order billed and frees the table.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="orderId">The order.</param>
    /// <param name="warehouseId">The warehouse the ingredients are issued from.</param>
    /// <param name="date">The billing date.</param>
    /// <param name="cashAccountCode">The cash/tender account debited with the gross.</param>
    /// <param name="taxAccountCode">The output-tax account, when there is tax.</param>
    /// <param name="serviceChargeAmount">An optional service charge / gratuity (house income) added to the bill.</param>
    /// <param name="serviceChargeAccountCode">The service-charge income account (required when charging).</param>
    /// <param name="tipAmount">An optional tip (staff money — a liability) added to the bill.</param>
    /// <param name="tipsPayableAccountCode">The tips-payable liability account (required when tipping).</param>
    /// <param name="discountAmount">An optional discount allowed (a contra-revenue) deducted from the bill.</param>
    /// <param name="discountAccountCode">The discounts-allowed account (required when discounting).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The bill result.</returns>
    Task<Result<BillResult>> BillOrderAsync(long companyId, long orderId, long warehouseId, DateOnly date, string cashAccountCode, string? taxAccountCode = null, decimal serviceChargeAmount = 0m, string? serviceChargeAccountCode = null, decimal tipAmount = 0m, string? tipsPayableAccountCode = null, decimal discountAmount = 0m, string? discountAccountCode = null, CancellationToken ct = default);

    /// <summary>
    /// Bills part of an order (a split): only the specified line portions. Posts revenue and recipe cost of
    /// sales for that portion; when the last portion is billed the order is closed and the table freed.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="orderId">The order.</param>
    /// <param name="warehouseId">The warehouse the ingredients are issued from.</param>
    /// <param name="date">The billing date.</param>
    /// <param name="portions">The line portions to bill now.</param>
    /// <param name="cashAccountCode">The cash/tender account debited with the gross.</param>
    /// <param name="taxAccountCode">The output-tax account, when there is tax.</param>
    /// <param name="serviceChargeAmount">An optional service charge / gratuity (house income) added to this split.</param>
    /// <param name="serviceChargeAccountCode">The service-charge income account (required when charging).</param>
    /// <param name="tipAmount">An optional tip (staff money — a liability) added to this split.</param>
    /// <param name="tipsPayableAccountCode">The tips-payable liability account (required when tipping).</param>
    /// <param name="discountAmount">An optional discount allowed (a contra-revenue) deducted from this split.</param>
    /// <param name="discountAccountCode">The discounts-allowed account (required when discounting).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The bill result.</returns>
    Task<Result<BillResult>> BillPartialAsync(long companyId, long orderId, long warehouseId, DateOnly date, IReadOnlyList<SplitPortion> portions, string cashAccountCode, string? taxAccountCode = null, decimal serviceChargeAmount = 0m, string? serviceChargeAccountCode = null, decimal tipAmount = 0m, string? tipsPayableAccountCode = null, decimal discountAmount = 0m, string? discountAccountCode = null, CancellationToken ct = default);

    /// <summary>
    /// Pays out accumulated tips to staff (brief: tips are staff money, a liability): Dr tips payable, Cr the
    /// funding account. Clears the tips-payable balance.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="amount">The amount paid out.</param>
    /// <param name="date">The payout date.</param>
    /// <param name="tipsPayableAccountCode">The tips-payable liability account (debited).</param>
    /// <param name="fundingAccountCode">The cash/bank account the payout is made from (credited).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The GL journal number.</returns>
    Task<Result<string>> PayTipsAsync(long companyId, decimal amount, DateOnly date, string tipsPayableAccountCode, string fundingAccountCode, CancellationToken ct = default);

    /// <summary>
    /// Distributes an amount from the pooled tips among staff by weight (tip pooling). Assigns who within the
    /// tips-payable liability is owed what — no GL, since the liability already exists. The shares sum exactly
    /// to the amount (rounding remainder on the last share).
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="date">The distribution date.</param>
    /// <param name="totalAmount">The amount to distribute.</param>
    /// <param name="tipsPayableAccountCode">The tips-payable account the pool sits in.</param>
    /// <param name="shares">The staff shares (by weight).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of allocations created.</returns>
    Task<Result<int>> DistributeTipsAsync(long companyId, DateOnly date, decimal totalAmount, string tipsPayableAccountCode, IReadOnlyList<TipShare> shares, CancellationToken ct = default);

    /// <summary>Pays a staff member's tip allocation: Dr tips payable, Cr the funding account; marks it paid.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="allocationId">The tip allocation.</param>
    /// <param name="date">The payout date.</param>
    /// <param name="fundingAccountCode">The cash/bank account the payout is made from (credited).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The GL journal number.</returns>
    Task<Result<string>> PayTipAllocationAsync(long companyId, long allocationId, DateOnly date, string fundingAccountCode, CancellationToken ct = default);

    /// <summary>Assigns an order line to a seat (or clears it with null) for split-by-seat billing.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="orderId">The order.</param>
    /// <param name="lineId">The order line.</param>
    /// <param name="seatNumber">The seat, or null to clear.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or an error.</returns>
    Task<Result> AssignLineToSeatAsync(long companyId, long orderId, long lineId, int? seatNumber, CancellationToken ct = default);

    /// <summary>
    /// Bills one seat's share of an order (split by seat): every not-yet-billed, non-voided line assigned to
    /// that seat. Posts revenue and recipe cost of sales for those lines; when the last seat is billed the
    /// order closes and the table frees. Optional service charge / tip / discount apply to this seat's bill.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="orderId">The order.</param>
    /// <param name="seatNumber">The seat to bill.</param>
    /// <param name="warehouseId">The warehouse the ingredients are issued from.</param>
    /// <param name="date">The billing date.</param>
    /// <param name="cashAccountCode">The cash/tender account debited with the gross.</param>
    /// <param name="taxAccountCode">The output-tax account, when there is tax.</param>
    /// <param name="serviceChargeAmount">An optional service charge / gratuity (house income) added to this seat.</param>
    /// <param name="serviceChargeAccountCode">The service-charge income account (required when charging).</param>
    /// <param name="tipAmount">An optional tip (staff money — a liability) added to this seat.</param>
    /// <param name="tipsPayableAccountCode">The tips-payable liability account (required when tipping).</param>
    /// <param name="discountAmount">An optional discount allowed (a contra-revenue) deducted from this seat.</param>
    /// <param name="discountAccountCode">The discounts-allowed account (required when discounting).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The bill result.</returns>
    Task<Result<BillResult>> BillSeatAsync(long companyId, long orderId, int seatNumber, long warehouseId, DateOnly date, string cashAccountCode, string? taxAccountCode = null, decimal serviceChargeAmount = 0m, string? serviceChargeAccountCode = null, decimal tipAmount = 0m, string? tipsPayableAccountCode = null, decimal discountAmount = 0m, string? discountAccountCode = null, CancellationToken ct = default);

    /// <summary>
    /// Voids an order line before it is billed (wrong order, kitchen error): it is excluded from the bill and
    /// consumes no stock. Requires a reason; the order must be open and nothing on the line already billed.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="orderId">The order.</param>
    /// <param name="lineId">The order line to void.</param>
    /// <param name="reason">The mandatory void reason.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or an error.</returns>
    Task<Result> VoidLineAsync(long companyId, long orderId, long lineId, string reason, CancellationToken ct = default);

    /// <summary>
    /// Comps an order line (complimentary): the item is still made and served — so billing recognises its cost
    /// of sales via the recipe — but the guest is charged nothing (no revenue). Requires a reason; the order
    /// must be open and nothing on the line already billed.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="orderId">The order.</param>
    /// <param name="lineId">The order line to comp.</param>
    /// <param name="reason">The mandatory comp reason.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or an error.</returns>
    Task<Result> CompLineAsync(long companyId, long orderId, long lineId, string reason, CancellationToken ct = default);

    /// <summary>Cancels an open order (nothing was billed) and frees the table. Requires a reason.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="orderId">The order.</param>
    /// <param name="reason">The mandatory reason.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or an error.</returns>
    Task<Result> CancelOrderAsync(long companyId, long orderId, string reason, CancellationToken ct = default);

    /// <summary>Reassigns an open order to another waiter (shift change or section handover).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="orderId">The open order.</param>
    /// <param name="newWaiterUserId">The new waiter.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or an error.</returns>
    Task<Result> ReassignWaiterAsync(long companyId, long orderId, long newWaiterUserId, CancellationToken ct = default);

    /// <summary>Transfers an open order to another free table, freeing the original.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="orderId">The order.</param>
    /// <param name="newTableId">The destination table (must be free).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or an error.</returns>
    Task<Result> TransferOrderAsync(long companyId, long orderId, long newTableId, CancellationToken ct = default);

    /// <summary>
    /// Fires the order's not-yet-fired lines to the kitchen as a kitchen order ticket (KOT). Operational
    /// only (no GL). New lines added later can be fired again as a further ticket (course firing).
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="orderId">The open order.</param>
    /// <param name="ticketNumber">The ticket number.</param>
    /// <param name="date">The fire date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created ticket id.</returns>
    Task<Result<long>> FireOrderAsync(long companyId, long orderId, string ticketNumber, DateOnly date, CancellationToken ct = default);

    /// <summary>
    /// Fires only the not-yet-fired lines of a single course (coursing): sends that course to the kitchen as
    /// its own ticket, leaving later courses to be fired when the table is ready. Operational only (no GL).
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="orderId">The open order.</param>
    /// <param name="course">The course to fire.</param>
    /// <param name="ticketNumber">The ticket number.</param>
    /// <param name="date">The fire date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created ticket id.</returns>
    Task<Result<long>> FireCourseAsync(long companyId, long orderId, MenuCourse course, string ticketNumber, DateOnly date, CancellationToken ct = default);

    /// <summary>
    /// Sets a menu item's availability ("86" it when out of stock, or bring it back). An unavailable item
    /// cannot be added to an order.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="menuItemId">The menu item.</param>
    /// <param name="isAvailable">Whether the item is available to order.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or an error.</returns>
    Task<Result> SetMenuAvailabilityAsync(long companyId, long menuItemId, bool isAvailable, CancellationToken ct = default);

    /// <summary>Advances a kitchen ticket to the next preparation state (Sent → Preparing → Ready → Served).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="ticketId">The ticket.</param>
    /// <param name="status">The new status (must be later than the current one).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or an error.</returns>
    Task<Result> AdvanceTicketAsync(long companyId, long ticketId, KitchenTicketStatus status, CancellationToken ct = default);

    /// <summary>
    /// Books a table for a guest at a future time. Refuses a party larger than the table seats and a clash
    /// with another live booking on the same table within the turn-time window. Operational only (no GL).
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="tableId">The table to reserve.</param>
    /// <param name="guestName">The guest's name.</param>
    /// <param name="partySize">The number of covers.</param>
    /// <param name="reservedFor">The date and time the table is booked for.</param>
    /// <param name="guestPhone">The guest's contact number, if any.</param>
    /// <param name="notes">Any notes (occasion, seating preference), if any.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created reservation id.</returns>
    Task<Result<long>> ReserveTableAsync(long companyId, long tableId, string guestName, int partySize, DateTime reservedFor, string? guestPhone = null, string? notes = null, CancellationToken ct = default);

    /// <summary>
    /// Seats a booked reservation: opens a table order on the reserved (free) table and marks the reservation
    /// seated, linking the order.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="reservationId">The reservation.</param>
    /// <param name="orderNumber">The order number for the opened order.</param>
    /// <param name="waiterUserId">The waiter.</param>
    /// <param name="date">The open date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The opened order id.</returns>
    Task<Result<long>> SeatReservationAsync(long companyId, long reservationId, string orderNumber, long waiterUserId, DateOnly date, CancellationToken ct = default);

    /// <summary>Cancels a booked reservation with a reason.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="reservationId">The reservation.</param>
    /// <param name="reason">The mandatory cancellation reason.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or an error.</returns>
    Task<Result> CancelReservationAsync(long companyId, long reservationId, string reason, CancellationToken ct = default);

    /// <summary>Marks a booked reservation a no-show (the guest never arrived).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="reservationId">The reservation.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or an error.</returns>
    Task<Result> MarkReservationNoShowAsync(long companyId, long reservationId, CancellationToken ct = default);

    /// <summary>Adds a walk-in party to the waitlist (queued for a table). Operational only (no GL).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="guestName">The guest's name.</param>
    /// <param name="partySize">The number of covers.</param>
    /// <param name="quotedMinutes">The quoted wait, in minutes.</param>
    /// <param name="addedOn">When the party joined the queue.</param>
    /// <param name="guestPhone">The guest's contact number, if any.</param>
    /// <param name="notes">Any notes (seating preference), if any.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created waitlist entry id.</returns>
    Task<Result<long>> AddToWaitlistAsync(long companyId, string guestName, int partySize, int quotedMinutes, DateTime addedOn, string? guestPhone = null, string? notes = null, CancellationToken ct = default);

    /// <summary>
    /// Seats a waiting party: opens a table order on the given (free) table and marks the entry seated,
    /// linking the table and order.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="waitlistId">The waitlist entry.</param>
    /// <param name="tableId">The table to seat the party at (must be free).</param>
    /// <param name="orderNumber">The order number for the opened order.</param>
    /// <param name="waiterUserId">The waiter.</param>
    /// <param name="date">The open date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The opened order id.</returns>
    Task<Result<long>> SeatFromWaitlistAsync(long companyId, long waitlistId, long tableId, string orderNumber, long waiterUserId, DateOnly date, CancellationToken ct = default);

    /// <summary>Removes a waiting party from the queue (they left before being seated).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="waitlistId">The waitlist entry.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or an error.</returns>
    Task<Result> RemoveFromWaitlistAsync(long companyId, long waitlistId, CancellationToken ct = default);

    /// <summary>Marks a waiting party a no-show (called but did not return).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="waitlistId">The waitlist entry.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or an error.</returns>
    Task<Result> MarkWaitlistNoShowAsync(long companyId, long waitlistId, CancellationToken ct = default);

    /// <summary>
    /// Combines two or more free, ungrouped tables into one unit for a large party. The lowest-numbered table
    /// becomes the primary the order is opened against; the whole group is occupied and freed together.
    /// Operational only (no GL).
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="tableIds">The tables to combine (at least two, all free and ungrouped).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created table group id.</returns>
    Task<Result<long>> CombineTablesAsync(long companyId, IReadOnlyList<long> tableIds, CancellationToken ct = default);

    /// <summary>Releases a table group, separating the tables again. Requires the group to be free (not seated).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="groupId">The table group.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or an error.</returns>
    Task<Result> UncombineTablesAsync(long companyId, long groupId, CancellationToken ct = default);

    /// <summary>Creates a floor-plan section (zone). Operational only (no GL).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The short section code (unique per company).</param>
    /// <param name="name">The section name.</param>
    /// <param name="description">A description, if any.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created section id.</returns>
    Task<Result<long>> CreateSectionAsync(long companyId, string code, string name, string? description = null, CancellationToken ct = default);

    /// <summary>Assigns a table to a floor-plan section, or unassigns it when the section id is null.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="tableId">The table.</param>
    /// <param name="sectionId">The section, or null to unassign.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or an error.</returns>
    Task<Result> AssignTableToSectionAsync(long companyId, long tableId, long? sectionId, CancellationToken ct = default);

    /// <summary>Assigns a server (waiter) to a section, or clears the assignment when the user id is null.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="sectionId">The section.</param>
    /// <param name="waiterUserId">The server, or null to clear.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or an error.</returns>
    Task<Result> AssignSectionServerAsync(long companyId, long sectionId, long? waiterUserId, CancellationToken ct = default);

    /// <summary>Opens or closes a section for service (e.g. closing the patio when it rains).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="sectionId">The section.</param>
    /// <param name="isActive">Whether the section is in service.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or an error.</returns>
    Task<Result> SetSectionActiveAsync(long companyId, long sectionId, bool isActive, CancellationToken ct = default);

    /// <summary>
    /// Sets the company's automatic-gratuity policy: parties of <paramref name="minPartySize"/> or more have a
    /// service charge of <paramref name="percent"/>% of the net suggested at billing. Upserts the single policy row.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="minPartySize">The party-size threshold (0 disables auto-gratuity).</param>
    /// <param name="percent">The gratuity percentage of the net.</param>
    /// <param name="serviceChargeAccountCode">The service-charge income account the gratuity books to.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or an error.</returns>
    Task<Result> SetAutoGratuityPolicyAsync(long companyId, int minPartySize, decimal percent, string serviceChargeAccountCode, CancellationToken ct = default);

    /// <summary>
    /// Quotes the automatic gratuity for an order from the company policy and the order's party size and current
    /// net. Pass the returned amount/account as the service charge when billing to apply it.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="orderId">The order.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The gratuity quote (Applies=false when no policy, party too small, or nothing ordered).</returns>
    Task<Result<AutoGratuityQuote>> QuoteAutoGratuityAsync(long companyId, long orderId, CancellationToken ct = default);
}
