using One4All.Core.Common;

namespace One4All.Core.Pos;

/// <summary>How a promotion discounts a basket (brief §4.1).</summary>
public enum PromotionKind
{
    /// <summary>Mix and match: every N units from the qualifying items (any mix) for a set price.</summary>
    NForPrice = 1,

    /// <summary>
    /// Buy X get Y: for every X qualifying units, Y reward units are discounted by a percentage (100 = free). With no reward items the
    /// reward comes from the qualifying items themselves, cheapest first — lowest-price-free.
    /// </summary>
    BuyXGetY = 2,

    /// <summary>A percentage off every unit of a category (or of the listed items).</summary>
    PercentOff = 3,

    /// <summary>Meal deal: one item from each group for a set price.</summary>
    MealDeal = 4,
}

/// <summary>
/// A configured promotion (<c>pos.promotion</c>, brief §4.1): its kind and parameters, the items it applies to, when and where it runs
/// (dates, a daily time window for happy hours, a branch), who gets it (members only, a coupon code), how it ranks against others
/// (priority, compounding) and the GL account its discount posts to so margin stays analysable.
/// </summary>
public sealed class Promotion : AuditableEntity, ICompanyScoped
{
    private readonly List<PromotionItem> _items = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private Promotion()
    {
        Code = string.Empty;
        Name = string.Empty;
        DiscountAccountCode = string.Empty;
    }

    /// <summary>Creates the promotion.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The code.</param>
    /// <param name="name">The name shown on receipts.</param>
    /// <param name="kind">The kind.</param>
    /// <param name="discountAccountCode">The discount GL account.</param>
    public Promotion(long companyId, string code, string name, PromotionKind kind, string discountAccountCode)
    {
        CompanyId = companyId;
        Code = code.Trim().ToUpperInvariant();
        Name = name;
        Kind = kind;
        DiscountAccountCode = discountAccountCode;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The code.</summary>
    public string Code { get; private set; }

    /// <summary>Gets or sets the name shown on receipts.</summary>
    public string Name { get; set; }

    /// <summary>The kind.</summary>
    public PromotionKind Kind { get; private set; }

    /// <summary>Gets or sets the discount GL account.</summary>
    public string DiscountAccountCode { get; set; }

    /// <summary>Gets or sets N (N for price), X (buy X get Y).</summary>
    public int Quantity { get; set; }

    /// <summary>Gets or sets Y (buy X get Y).</summary>
    public int RewardQuantity { get; set; }

    /// <summary>Gets or sets the set price (N for price, meal deal).</summary>
    public decimal? Price { get; set; }

    /// <summary>Gets or sets the percentage off (percent off; buy X get Y — 100 is free).</summary>
    public decimal? Percent { get; set; }

    /// <summary>Gets or sets the item category a percent-off promotion covers (when it lists no items).</summary>
    public string? Category { get; set; }

    /// <summary>Gets or sets the order promotions are applied in (lower first).</summary>
    public int Priority { get; set; }

    /// <summary>Gets or sets a value indicating whether it may stack on units another promotion has already discounted.</summary>
    public bool Compounding { get; set; }

    /// <summary>Gets or sets a value indicating whether only loyalty members get it.</summary>
    public bool MemberOnly { get; set; }

    /// <summary>Gets or sets the coupon code the basket must present, if any.</summary>
    public string? CouponCode { get; set; }

    /// <summary>Gets or sets the customer types it is for, comma-separated (null: every customer).</summary>
    public string? CustomerTypes { get; set; }

    /// <summary>Whether a customer of a type may have it.</summary>
    /// <param name="customerType">The customer's type (null: an unnamed or untyped customer).</param>
    /// <returns>True when eligible.</returns>
    public bool IsFor(string? customerType)
        => CustomerTypes is null || (customerType is not null && CustomerTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(customerType.Trim(), StringComparer.OrdinalIgnoreCase));

    /// <summary>Gets or sets the first day it runs.</summary>
    public DateOnly StartsOn { get; set; }

    /// <summary>Gets or sets the last day it runs (open-ended when null).</summary>
    public DateOnly? EndsOn { get; set; }

    /// <summary>Gets or sets the daily start of a happy-hour window.</summary>
    public TimeOnly? DailyFrom { get; set; }

    /// <summary>Gets or sets the daily end of a happy-hour window.</summary>
    public TimeOnly? DailyTo { get; set; }

    /// <summary>Gets or sets the branch it runs in (every branch when null).</summary>
    public long? BranchId { get; set; }

    /// <summary>Gets or sets a value indicating whether it is switched on.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>The items it applies to: group 0 qualifies; group 1 is a buy-X-get-Y reward set; groups 1, 2… are a meal deal's courses.</summary>
    public IReadOnlyList<PromotionItem> Items => _items.AsReadOnly();

    /// <summary>Replaces the items.</summary>
    /// <param name="items">The item and group pairs.</param>
    public void SetItems(IEnumerable<(long ItemId, int Group)> items)
    {
        _items.Clear();
        _items.AddRange(items.Select(i => new PromotionItem(i.ItemId, i.Group)));
    }
}

/// <summary>An item on a promotion, in a group.</summary>
public sealed class PromotionItem : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private PromotionItem()
    {
    }

    /// <summary>Creates the item.</summary>
    /// <param name="itemId">The item.</param>
    /// <param name="group">The group.</param>
    public PromotionItem(long itemId, int group)
    {
        ItemId = itemId;
        Group = group;
    }

    /// <summary>The owning promotion.</summary>
    public long PromotionId { get; private set; }

    /// <summary>The item.</summary>
    public long ItemId { get; private set; }

    /// <summary>The group.</summary>
    public int Group { get; private set; }
}
