using One4All.Core.Common;

namespace One4All.Application.Abstractions.Restaurant;

/// <summary>An ingredient of a menu item's recipe.</summary>
/// <param name="IngredientItemId">The stock item consumed.</param>
/// <param name="Quantity">The quantity consumed per menu item sold.</param>
public readonly record struct RecipeIngredient(long IngredientItemId, decimal Quantity);

/// <summary>The result of billing a table order.</summary>
/// <param name="Net">The net (tax-exclusive) total.</param>
/// <param name="Tax">The tax total.</param>
/// <param name="Gross">The gross total charged.</param>
/// <param name="CostOfSales">The cost of sales released by exploding the recipes.</param>
/// <param name="RevenueJournalNumber">The revenue GL journal number.</param>
public readonly record struct BillResult(decimal Net, decimal Tax, decimal Gross, decimal CostOfSales, string RevenueJournalNumber);

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
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The bill result.</returns>
    Task<Result<BillResult>> BillOrderAsync(long companyId, long orderId, long warehouseId, DateOnly date, string cashAccountCode, string? taxAccountCode = null, CancellationToken ct = default);
}
