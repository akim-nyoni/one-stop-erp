using One4All.Core.Accounting;
using One4All.Core.Common;

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
public sealed record ResolvedPrice(long ItemId, decimal Quantity, decimal UnitPrice, CurrencyCode Currency, long PriceListId, string PriceListCode, decimal MinimumQuantity, bool FromCustomerList);

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

    /// <summary>Sets an item's price at a quantity break on a list (replacing an existing break at that quantity).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="priceListId">The price list.</param>
    /// <param name="itemId">The item.</param>
    /// <param name="minimumQuantity">The quantity from which the price applies (1 = base price).</param>
    /// <param name="unitPrice">The unit price.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or an error.</returns>
    Task<Result> SetPriceAsync(long companyId, long priceListId, long itemId, decimal minimumQuantity, decimal unitPrice, CancellationToken ct = default);

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
}
