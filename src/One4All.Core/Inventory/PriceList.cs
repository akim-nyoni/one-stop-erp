using One4All.Core.Accounting;
using One4All.Core.Common;

namespace One4All.Core.Inventory;

/// <summary>How a price list prices items it has no price for from their cost (brief §4.1 price book).</summary>
public enum CostPlusBasis
{
    /// <summary>It does not: an item needs a price on the list.</summary>
    None = 0,

    /// <summary>Cost plus a markup on cost: cost × (1 + p%).</summary>
    Markup = 1,

    /// <summary>A margin on the selling price: cost ÷ (1 − p%).</summary>
    Margin = 2,
}

/// <summary>
/// A selling price list (brief §4.2/§4.13, <c>inv.price_list</c>): per item, one or more quantity-break
/// prices in the list's currency. A customer may be linked to a list; the company's default list is the
/// fallback. Prices resolve to the break with the highest minimum quantity not exceeding the quantity sold.
/// </summary>
public sealed class PriceList : SoftDeletableEntity, ICompanyScoped
{
    private readonly List<PriceListLine> _lines = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private PriceList()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    /// <summary>Creates a price list.</summary>
    /// <param name="companyId">The owning company.</param>
    /// <param name="code">The list code (unique within the company).</param>
    /// <param name="name">The list name.</param>
    /// <param name="currency">The currency every price on the list is in.</param>
    public PriceList(long companyId, string code, string name, CurrencyCode currency)
    {
        CompanyId = companyId;
        Code = code.Trim().ToUpperInvariant();
        Name = name;
        Currency = currency;
        IsActive = true;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The list code.</summary>
    public string Code { get; private set; }

    /// <summary>The list name.</summary>
    public string Name { get; set; }

    /// <summary>The currency of every price on the list.</summary>
    public CurrencyCode Currency { get; private set; }

    /// <summary>Whether this is the company's default list (the fallback for customers with no list of their own).</summary>
    public bool IsDefault { get; set; }

    /// <summary>Whether the list is active.</summary>
    public bool IsActive { get; set; }

    /// <summary>Gets or sets how items without a price on the list are priced from their cost (none: they are not).</summary>
    public CostPlusBasis CostPlusBasis { get; set; }

    /// <summary>Gets or sets the markup on cost, or the margin on price, in percent.</summary>
    public decimal CostPlusPercent { get; set; }

    /// <summary>Gets or sets the price point cost-plus prices round to (0: cents).</summary>
    public decimal CostPlusRoundTo { get; set; }

    /// <summary>Gets or sets the rounding direction (0 nearest, 1 up, 2 down).</summary>
    public int CostPlusRounding { get; set; }

    /// <summary>The price lines (item × minimum quantity).</summary>
    public IReadOnlyList<PriceListLine> Lines => _lines.AsReadOnly();

    /// <summary>
    /// Sets the price for an item at a quantity break, replacing an existing break at the same minimum
    /// quantity. A minimum quantity of 1 is the base price.
    /// </summary>
    /// <param name="itemId">The item.</param>
    /// <param name="minimumQuantity">The quantity from which this price applies (≥ 1).</param>
    /// <param name="unitPrice">The unit price (≥ 0).</param>
    /// <param name="effectiveFrom">The day the price takes effect (null: in force from the start, the standing price).</param>
    /// <returns>The line.</returns>
    public PriceListLine SetPrice(long itemId, decimal minimumQuantity, decimal unitPrice, DateOnly? effectiveFrom = null)
    {
        if (minimumQuantity < 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumQuantity), "The minimum quantity must be at least 1.");
        }

        if (unitPrice < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(unitPrice), "The unit price cannot be negative.");
        }

        PriceListLine? existing = _lines.FirstOrDefault(l => l.ItemId == itemId && l.MinimumQuantity == minimumQuantity && l.EffectiveFrom == effectiveFrom);
        if (existing is not null)
        {
            existing.UnitPrice = unitPrice;
            return existing;
        }

        PriceListLine line = new(itemId, minimumQuantity, unitPrice) { EffectiveFrom = effectiveFrom };
        _lines.Add(line);
        return line;
    }

    /// <summary>Removes every price for an item.</summary>
    /// <param name="itemId">The item.</param>
    /// <returns>The number of breaks removed.</returns>
    public int RemoveItem(long itemId) => _lines.RemoveAll(l => l.ItemId == itemId);

    /// <summary>
    /// Resolves the unit price for an item at a quantity: the break with the highest minimum quantity that
    /// does not exceed the quantity. Null when the list has no price for the item at that quantity.
    /// </summary>
    /// <param name="itemId">The item.</param>
    /// <param name="quantity">The quantity sold.</param>
    /// <returns>The resolved line, or null.</returns>
    public PriceListLine? Resolve(long itemId, decimal quantity) => Resolve(itemId, quantity, DateOnly.FromDateTime(DateTime.Today));

    /// <summary>
    /// Resolves the unit price for an item at a quantity on a date: for each quantity break the price in force that day (the latest one
    /// effective on or before it; an undated price stands until a dated one takes over), then the highest break not above the quantity.
    /// </summary>
    /// <param name="itemId">The item.</param>
    /// <param name="quantity">The quantity sold.</param>
    /// <param name="asAt">The date (the order or sale date).</param>
    /// <returns>The resolved line, or null.</returns>
    public PriceListLine? Resolve(long itemId, decimal quantity, DateOnly asAt) => InForce(asAt)
        .Where(l => l.ItemId == itemId && l.MinimumQuantity <= quantity)
        .OrderByDescending(l => l.MinimumQuantity)
        .FirstOrDefault();

    /// <summary>The lines in force on a date: per item and quantity break, the latest effective on or before it.</summary>
    /// <param name="asAt">The date.</param>
    /// <returns>The lines.</returns>
    public IReadOnlyList<PriceListLine> InForce(DateOnly asAt) => [.. _lines
        .Where(l => l.EffectiveFrom is null || l.EffectiveFrom <= asAt)
        .GroupBy(l => (l.ItemId, l.MinimumQuantity))
        .Select(g => g.OrderByDescending(l => l.EffectiveFrom ?? DateOnly.MinValue).First())];
}

/// <summary>A quantity-break price for one item on a <see cref="PriceList"/> (<c>inv.price_list_line</c>).</summary>
public sealed class PriceListLine : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private PriceListLine()
    {
    }

    /// <summary>Creates a price line.</summary>
    /// <param name="itemId">The item.</param>
    /// <param name="minimumQuantity">The quantity from which the price applies.</param>
    /// <param name="unitPrice">The unit price.</param>
    public PriceListLine(long itemId, decimal minimumQuantity, decimal unitPrice)
    {
        ItemId = itemId;
        MinimumQuantity = minimumQuantity;
        UnitPrice = unitPrice;
    }

    /// <summary>The owning price list.</summary>
    public long PriceListId { get; private set; }

    /// <summary>The item.</summary>
    public long ItemId { get; private set; }

    /// <summary>The quantity from which this price applies.</summary>
    public decimal MinimumQuantity { get; private set; }

    /// <summary>The unit price.</summary>
    public decimal UnitPrice { get; internal set; }

    /// <summary>The day the price takes effect (null: the standing price, until a dated one takes over).</summary>
    public DateOnly? EffectiveFrom { get; internal set; }
}
