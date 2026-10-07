using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Inventory;

namespace One4All.Application.Abstractions.Inventory;

/// <summary>A resolved selling price.</summary>
/// <param name="ItemId">The item.</param>
/// <param name="Quantity">The quantity priced.</param>
/// <param name="UnitPrice">The unit price.</param>
/// <param name="Currency">The price list's currency.</param>
/// <param name="PriceListId">The list the price came from.</param>
/// <param name="PriceListCode">That list's code.</param>
/// <param name="MinimumQuantity">The quantity break that applied.</param>
/// <param name="FromCustomerList">True when the customer's own list supplied the price (false = the default list).</param>
public sealed record ResolvedPrice(long ItemId, decimal Quantity, decimal UnitPrice, CurrencyCode Currency, long PriceListId, string PriceListCode, decimal MinimumQuantity, bool FromCustomerList, bool FromCost = false)
{
    /// <summary>The day the price came into force (null: the standing price).</summary>
    public DateOnly? EffectiveFrom { get; init; }
}

/// <summary>
/// Selling-price resolution (brief §4.13): looks up an item's price for a customer and quantity, from the
/// customer's own price list first and the company's default list otherwise, honouring quantity breaks.
/// Also maintains the lists.
/// </summary>
public interface IPricingService
{
    /// <summary>Creates a price list. Making it the default clears the flag on any other list.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The list code.</param>
    /// <param name="name">The list name.</param>
    /// <param name="currency">The list currency.</param>
    /// <param name="isDefault">Whether this is the company's default list.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The price list id.</returns>
    Task<Result<long>> CreatePriceListAsync(long companyId, string code, string name, CurrencyCode currency, bool isDefault, CancellationToken ct = default);

    /// <summary>
    /// Changes every price on a list (or only the given items) by a percentage, rounding each to a price-point
    /// increment. Refuses a change of −100% or below. Returns how many price breaks changed.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="priceListId">The price list.</param>
    /// <param name="percent">The change in percent (+5 = increase 5%).</param>
    /// <param name="roundTo">The price-point increment (0 = cents).</param>
    /// <param name="rounding">The rounding direction.</param>
    /// <param name="itemIds">Only these items, or null for every item on the list.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of price breaks whose price changed.</returns>
    Task<Result<int>> AdjustPricesAsync(long companyId, long priceListId, decimal percent, decimal roundTo, One4All.Application.Inventory.PriceRounding rounding, IReadOnlyCollection<long>? itemIds = null, CancellationToken ct = default);

    /// <summary>
    /// Creates a new (non-default) list in the same currency as a source list, with every price break copied
    /// and adjusted by a percentage and rounded — e.g. a trade list at −10% of standard.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="sourcePriceListId">The list to copy.</param>
    /// <param name="code">The new list's code.</param>
    /// <param name="name">The new list's name.</param>
    /// <param name="percent">The change in percent applied to every copied price.</param>
    /// <param name="roundTo">The price-point increment (0 = cents).</param>
    /// <param name="rounding">The rounding direction.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The new list's id.</returns>
    Task<Result<long>> CopyPriceListAsync(long companyId, long sourcePriceListId, string code, string name, decimal percent, decimal roundTo, One4All.Application.Inventory.PriceRounding rounding, CancellationToken ct = default);

    /// <summary>Sets an item's price at a quantity break on a list (replacing an existing break at that quantity).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="priceListId">The price list.</param>
    /// <param name="itemId">The item.</param>
    /// <param name="minimumQuantity">The quantity from which the price applies (1 = base price).</param>
    /// <param name="unitPrice">The unit price.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or an error.</returns>
    Task<Result> SetPriceAsync(long companyId, long priceListId, long itemId, decimal minimumQuantity, decimal unitPrice, CancellationToken ct = default);

    /// <summary>
    /// Schedules a price from a date (brief §4.13 effective dating): it takes over from whatever is in force on that day; orders and sales
    /// dated before it keep the earlier price. Re-scheduling the same day replaces it; a date in the past is refused (history is kept).
    /// </summary>
    Task<Result> SchedulePriceAsync(long companyId, long priceListId, long itemId, decimal minimumQuantity, decimal unitPrice, DateOnly effectiveFrom, DateOnly today, CancellationToken ct = default);

    /// <summary>Schedules a percentage change of a list's prices in force on a date, from that date (e.g. an annual increase), leaving today's prices alone.</summary>
    Task<Result<int>> ScheduleAdjustmentAsync(long companyId, long priceListId, decimal percent, decimal roundTo, One4All.Application.Inventory.PriceRounding rounding, DateOnly effectiveFrom,
        DateOnly today, CancellationToken ct = default);

    /// <summary>An item's price history on a list: every scheduled and standing price with its quantity break and date.</summary>
    Task<IReadOnlyList<(decimal MinimumQuantity, decimal UnitPrice, DateOnly? EffectiveFrom)>> PriceHistoryAsync(long companyId, long priceListId, long itemId, CancellationToken ct = default);

    /// <summary>Resolves the price as at a date (the order or sale date): a price scheduled for later does not apply yet.</summary>
    Task<Result<ResolvedPrice>> PriceAsync(long companyId, long? customerId, long itemId, decimal quantity, DateOnly asAt, CancellationToken ct = default);

    /// <summary>Links a customer to a price list (null = use the default list).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="customerId">The customer.</param>
    /// <param name="priceListId">The price list, or null.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or an error.</returns>
    Task<Result> AssignCustomerAsync(long companyId, long customerId, long? priceListId, CancellationToken ct = default);

    /// <summary>
    /// Resolves the unit price of an item for a customer at a quantity: the customer's own list when it
    /// prices the item, else the default list. Fails when neither prices the item at that quantity.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="customerId">The customer (null = use the default list only).</param>
    /// <param name="itemId">The item.</param>
    /// <param name="quantity">The quantity (≥ 1 is expected; breaks below 1 never match).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The resolved price.</returns>
    Task<Result<ResolvedPrice>> PriceAsync(long companyId, long? customerId, long itemId, decimal quantity, CancellationToken ct = default);

    /// <summary>
    /// Makes a price list price items it has no price for from their cost: a markup on cost or a margin on price, rounded to a price point.
    /// Cost is the item's average cost across warehouses, or its standard cost. Only a list in the base currency can (<c>Price.CostPlusCurrency</c>).
    /// <see cref="CostPlusBasis.None"/> turns it off. Prices set on the list still win.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="priceListId">The list.</param>
    /// <param name="basis">Markup, margin or none.</param>
    /// <param name="percent">The percent (a margin below 100).</param>
    /// <param name="roundTo">The price point.</param>
    /// <param name="rounding">The rounding direction.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    Task<Result> SetCostPlusAsync(long companyId, long priceListId, Core.Inventory.CostPlusBasis basis, decimal percent, decimal roundTo, One4All.Application.Inventory.PriceRounding rounding,
        CancellationToken ct = default);
}
