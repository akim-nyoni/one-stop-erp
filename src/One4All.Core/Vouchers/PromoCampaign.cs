using One4All.Core.Common;

namespace One4All.Core.Vouchers;

/// <summary>How a promotion discounts a sale.</summary>
public enum PromoDiscountType
{
    /// <summary>A percentage of the spend.</summary>
    Percent = 1,

    /// <summary>A fixed amount off.</summary>
    Amount = 2,
}

/// <summary>
/// A discount / promotion campaign (<c>vou.promo_campaign</c>, brief §4.8): codes valid between two dates, giving a
/// percentage or a fixed amount off a minimum spend, each code usable a set number of times and the campaign capped in
/// total. Redemptions are recorded for analysis; the discount itself is applied by the sale that redeems it.
/// </summary>
public sealed class PromoCampaign : AuditableEntity, ICompanyScoped
{
    private readonly List<PromoCode> _codes = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private PromoCampaign()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    /// <summary>Creates a campaign.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The campaign code.</param>
    /// <param name="name">The campaign name.</param>
    /// <param name="discountType">Percentage or amount off.</param>
    /// <param name="discountValue">The percentage or amount.</param>
    /// <param name="validFrom">The first day codes work.</param>
    /// <param name="validTo">The last day codes work.</param>
    /// <param name="minimumSpend">The smallest spend a code applies to.</param>
    /// <param name="usesPerCode">How many times each code may be used (1 for single-use).</param>
    /// <param name="maxRedemptions">The cap across the whole campaign, or null for none.</param>
    public PromoCampaign(long companyId, string code, string name, PromoDiscountType discountType, decimal discountValue, DateOnly validFrom, DateOnly validTo,
        decimal minimumSpend, int usesPerCode, int? maxRedemptions)
    {
        CompanyId = companyId;
        Code = code.Trim().ToUpperInvariant();
        Name = name;
        DiscountType = discountType;
        DiscountValue = discountValue;
        ValidFrom = validFrom;
        ValidTo = validTo;
        MinimumSpend = minimumSpend;
        UsesPerCode = usesPerCode;
        MaxRedemptions = maxRedemptions;
        IsActive = true;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The campaign code.</summary>
    public string Code { get; private set; }

    /// <summary>The campaign name.</summary>
    public string Name { get; set; }

    /// <summary>Percentage or amount off.</summary>
    public PromoDiscountType DiscountType { get; private set; }

    /// <summary>The percentage or amount.</summary>
    public decimal DiscountValue { get; private set; }

    /// <summary>The first day codes work.</summary>
    public DateOnly ValidFrom { get; private set; }

    /// <summary>The last day codes work.</summary>
    public DateOnly ValidTo { get; private set; }

    /// <summary>The smallest spend a code applies to.</summary>
    public decimal MinimumSpend { get; private set; }

    /// <summary>How many times each code may be used.</summary>
    public int UsesPerCode { get; private set; }

    /// <summary>The cap across the whole campaign.</summary>
    public int? MaxRedemptions { get; private set; }

    /// <summary>Redemptions so far across the campaign.</summary>
    public int Redemptions { get; private set; }

    /// <summary>Whether the campaign is running.</summary>
    public bool IsActive { get; set; }

    /// <summary>Gets or sets the customer types the codes are for, comma-separated (null: every customer).</summary>
    public string? CustomerTypes { get; set; }

    /// <summary>Gets or sets the item categories the discount applies to, comma-separated (null: the whole spend).</summary>
    public string? ItemCategories { get; set; }

    /// <summary>The campaign's codes.</summary>
    public IReadOnlyList<PromoCode> Codes => _codes.AsReadOnly();

    /// <summary>Whether a customer of a type may use the codes.</summary>
    /// <param name="customerType">The customer's type (null: an unnamed or untyped customer).</param>
    /// <returns>True when eligible.</returns>
    public bool IsFor(string? customerType) => CustomerTypes is null || (customerType is not null && List(CustomerTypes).Contains(customerType.Trim(), StringComparer.OrdinalIgnoreCase));

    /// <summary>Whether the discount applies to items of a category.</summary>
    /// <param name="category">The item category.</param>
    /// <returns>True when covered.</returns>
    public bool Covers(string? category) => ItemCategories is null || (category is not null && List(ItemCategories).Contains(category.Trim(), StringComparer.OrdinalIgnoreCase));

    private static string[] List(string value) => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Adds a code.</summary>
    /// <param name="code">The code.</param>
    public void AddCode(string code) => _codes.Add(new PromoCode(code.Trim().ToUpperInvariant()));

    /// <summary>Records a redemption of one of the campaign's codes.</summary>
    /// <param name="code">The code redeemed.</param>
    public void Redeem(PromoCode code)
    {
        ArgumentNullException.ThrowIfNull(code);
        code.Use();
        Redemptions++;
    }
}

/// <summary>A promotion code (<c>vou.promo_code</c>).</summary>
public sealed class PromoCode : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private PromoCode() => Code = string.Empty;

    /// <summary>Creates a code.</summary>
    /// <param name="code">The code.</param>
    public PromoCode(string code) => Code = code;

    /// <summary>The campaign.</summary>
    public long PromoCampaignId { get; private set; }

    /// <summary>The code.</summary>
    public string Code { get; private set; }

    /// <summary>How many times it has been used.</summary>
    public int TimesUsed { get; private set; }

    /// <summary>Records a use.</summary>
    internal void Use() => TimesUsed++;
}

/// <summary>A redemption of a promotion code (<c>vou.promo_redemption</c>).</summary>
public sealed class PromoRedemption : Entity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private PromoRedemption() => Reference = string.Empty;

    /// <summary>Records a redemption.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="campaignId">The campaign.</param>
    /// <param name="codeId">The code.</param>
    /// <param name="date">When.</param>
    /// <param name="spend">The spend it was applied to.</param>
    /// <param name="discount">The discount given.</param>
    /// <param name="reference">The sale or invoice it was used on.</param>
    public PromoRedemption(long companyId, long campaignId, long codeId, DateOnly date, decimal spend, decimal discount, string reference)
    {
        CompanyId = companyId;
        PromoCampaignId = campaignId;
        PromoCodeId = codeId;
        Date = date;
        Spend = spend;
        Discount = discount;
        Reference = reference;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The campaign.</summary>
    public long PromoCampaignId { get; private set; }

    /// <summary>The code.</summary>
    public long PromoCodeId { get; private set; }

    /// <summary>When.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>The spend it was applied to.</summary>
    public decimal Spend { get; private set; }

    /// <summary>The discount given.</summary>
    public decimal Discount { get; private set; }

    /// <summary>The sale or invoice it was used on.</summary>
    public string Reference { get; private set; }
}
