using One4All.Core.Common;

namespace One4All.Core.Accounting;

/// <summary>Tax treatment of a tax code (brief §3.5).</summary>
public enum TaxType
{
    /// <summary>Standard-rated.</summary>
    Standard = 1,

    /// <summary>Zero-rated (taxable at 0%).</summary>
    Zero = 2,

    /// <summary>Exempt (no tax, not recoverable).</summary>
    Exempt = 3,

    /// <summary>Outside the scope of tax.</summary>
    OutOfScope = 4,

    /// <summary>Reverse charge (buyer accounts for both output and input).</summary>
    ReverseCharge = 5,

    /// <summary>Withholding tax (deducted from supplier payments).</summary>
    Withholding = 6,

    /// <summary>Deemed supply.</summary>
    Deemed = 7,
}

/// <summary>What a tax code applies to.</summary>
public enum TaxAppliesTo
{
    /// <summary>Goods and services.</summary>
    Both = 0,

    /// <summary>Goods only.</summary>
    Goods = 1,

    /// <summary>Services only.</summary>
    Services = 2,
}

/// <summary>
/// A tax code (<c>sys.tax_code</c>, brief §3.5) with an effective-dated rate history, its input/output GL
/// control accounts, recoverable percentage, return-box mapping and jurisdiction. The rate in force on a
/// given date is resolved from the history, so a prior-period document is always taxed at the rate that
/// applied then.
/// </summary>
public sealed class TaxCode : SoftDeletableEntity, ICompanyScoped
{
    private readonly List<TaxRate> _rates = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private TaxCode()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    /// <summary>Creates a tax code.</summary>
    /// <param name="companyId">The owning company.</param>
    /// <param name="code">The unique tax code.</param>
    /// <param name="name">The display name.</param>
    /// <param name="type">The tax type.</param>
    public TaxCode(long companyId, string code, string name, TaxType type)
    {
        CompanyId = companyId;
        Code = code.Trim().ToUpperInvariant();
        Name = name;
        Type = type;
        RecoverablePercent = 100m;
        IsActive = true;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The unique tax code.</summary>
    public string Code { get; private set; }

    /// <summary>The display name.</summary>
    public string Name { get; set; }

    /// <summary>The tax type.</summary>
    public TaxType Type { get; private set; }

    /// <summary>The output (sales) tax GL account code.</summary>
    public string? OutputAccountCode { get; set; }

    /// <summary>The input (purchase) tax GL account code.</summary>
    public string? InputAccountCode { get; set; }

    /// <summary>The recoverable percentage of input tax (0–100).</summary>
    public decimal RecoverablePercent { get; set; }

    /// <summary>What the code applies to.</summary>
    public TaxAppliesTo AppliesTo { get; set; }

    /// <summary>The jurisdiction (country/region) the code belongs to.</summary>
    public string? Jurisdiction { get; set; }

    /// <summary>The box on the tax return this code maps to.</summary>
    public string? ReturnBox { get; set; }

    /// <summary>Whether the code is active.</summary>
    public bool IsActive { get; set; }

    /// <summary>The effective-dated rate history.</summary>
    public IReadOnlyCollection<TaxRate> Rates => _rates.AsReadOnly();

    /// <summary>Adds an effective-dated rate.</summary>
    /// <param name="effectiveFrom">The date the rate takes effect.</param>
    /// <param name="ratePercent">The rate as a percentage (e.g. 15 for 15%).</param>
    public void AddRate(DateOnly effectiveFrom, decimal ratePercent) => _rates.Add(new TaxRate(effectiveFrom, ratePercent));

    /// <summary>
    /// Returns the rate percentage in force on <paramref name="date"/>. Exempt/out-of-scope/zero codes
    /// always resolve to 0; other types use the latest rate effective on or before the date.
    /// </summary>
    /// <param name="date">The tax-point date.</param>
    /// <returns>The rate percentage (0 if none applies).</returns>
    public decimal RateOn(DateOnly date)
    {
        if (Type is TaxType.Exempt or TaxType.OutOfScope or TaxType.Zero)
        {
            return 0m;
        }

        return _rates
            .Where(r => r.EffectiveFrom <= date)
            .OrderByDescending(r => r.EffectiveFrom)
            .Select(r => r.RatePercent)
            .FirstOrDefault();
    }
}

/// <summary>One effective-dated rate of a <see cref="TaxCode"/> (<c>sys.tax_rate</c>).</summary>
public sealed class TaxRate : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private TaxRate()
    {
    }

    /// <summary>Creates a rate.</summary>
    /// <param name="effectiveFrom">The date it takes effect.</param>
    /// <param name="ratePercent">The rate percentage.</param>
    public TaxRate(DateOnly effectiveFrom, decimal ratePercent)
    {
        EffectiveFrom = effectiveFrom;
        RatePercent = ratePercent;
    }

    /// <summary>The owning tax-code id.</summary>
    public long TaxCodeId { get; private set; }

    /// <summary>The date the rate takes effect.</summary>
    public DateOnly EffectiveFrom { get; private set; }

    /// <summary>The rate as a percentage.</summary>
    public decimal RatePercent { get; private set; }
}
