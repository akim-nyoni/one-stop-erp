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
/// <param name="Gross">The gross total charged (net + tax + service charge + tip).</param>
/// <param name="CostOfSales">The cost of sales released by exploding the recipes.</param>
/// <param name="FullyBilled">True when this bill settled the whole order (table freed).</param>
/// <param name="RevenueJournalNumber">The revenue GL journal number.</param>
public readonly record struct BillResult(decimal Net, decimal Tax, decimal ServiceCharge, decimal Tip, decimal Gross, decimal CostOfSales, bool FullyBilled, string RevenueJournalNumber);

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
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created order id.</returns>
    Task<Result<long>> OpenOrderAsync(long companyId, long tableId, string orderNumber, long waiterUserId, DateOnly date, CancellationToken ct = default);

    /// <summary>Adds a menu item to an open order at its menu price.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="orderId">The order.</param>
    /// <param name="menuItemId">The menu item.</param>
    /// <param name="quantity">The quantity.</param>
    /// <param name="taxAmount">The tax on the line, if any.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The line net value.</returns>
    Task<Result<decimal>> AddItemAsync(long companyId, long orderId, long menuItemId, decimal quantity, decimal taxAmount = 0m, CancellationToken ct = default);

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
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The bill result.</returns>
    Task<Result<BillResult>> BillOrderAsync(long companyId, long orderId, long warehouseId, DateOnly date, string cashAccountCode, string? taxAccountCode = null, decimal serviceChargeAmount = 0m, string? serviceChargeAccountCode = null, decimal tipAmount = 0m, string? tipsPayableAccountCode = null, CancellationToken ct = default);

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
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The bill result.</returns>
    Task<Result<BillResult>> BillPartialAsync(long companyId, long orderId, long warehouseId, DateOnly date, IReadOnlyList<SplitPortion> portions, string cashAccountCode, string? taxAccountCode = null, decimal serviceChargeAmount = 0m, string? serviceChargeAccountCode = null, decimal tipAmount = 0m, string? tipsPayableAccountCode = null, CancellationToken ct = default);

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

    /// <summary>Cancels an open order (nothing was billed) and frees the table. Requires a reason.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="orderId">The order.</param>
    /// <param name="reason">The mandatory reason.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or an error.</returns>
    Task<Result> CancelOrderAsync(long companyId, long orderId, string reason, CancellationToken ct = default);

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
}
