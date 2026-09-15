using One4All.Core.Common;

namespace One4All.Core.Municipal;

/// <summary>A municipal service type (brief §4.9).</summary>
public enum ServiceType
{
    /// <summary>Property rates (property tax).</summary>
    Rates = 1,

    /// <summary>Water.</summary>
    Water = 2,

    /// <summary>Sewer / sanitation.</summary>
    Sewer = 3,

    /// <summary>Refuse / waste collection.</summary>
    Refuse = 4,

    /// <summary>Street lighting.</summary>
    StreetLighting = 5,

    /// <summary>Licences / permits.</summary>
    Licence = 6,

    /// <summary>Other configurable service.</summary>
    Other = 99,
}

/// <summary>How a tariff is calculated (brief §4.9 tariff engine).</summary>
public enum TariffType
{
    /// <summary>A flat charge regardless of consumption.</summary>
    Flat = 0,

    /// <summary>A fixed availability charge plus a per-unit consumption charge.</summary>
    PerUnit = 1,

    /// <summary>Stepped / block tariff: bands of consumption at increasing rates.</summary>
    Stepped = 2,

    /// <summary>Rate per valuation (cents in the dollar on the property value).</summary>
    RatePerValuation = 3,
}

/// <summary>
/// A municipal tariff (brief §4.9, <c>mun.tariff</c>) with a flexible calculation and effective-dated
/// versions so historical bills can be recomputed and explained. Stepped tariffs use <see cref="Blocks"/>.
/// </summary>
public sealed class Tariff : SoftDeletableEntity, ICompanyScoped
{
    private readonly List<TariffBlock> _blocks = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private Tariff()
    {
        Code = string.Empty;
        Name = string.Empty;
        RevenueAccountCode = string.Empty;
    }

    /// <summary>Creates a tariff.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The tariff code.</param>
    /// <param name="name">The tariff name.</param>
    /// <param name="serviceType">The service it applies to.</param>
    /// <param name="tariffType">The calculation type.</param>
    /// <param name="effectiveFrom">The date the tariff takes effect.</param>
    /// <param name="revenueAccountCode">The revenue account billed to.</param>
    public Tariff(long companyId, string code, string name, ServiceType serviceType, TariffType tariffType, DateOnly effectiveFrom, string revenueAccountCode)
    {
        CompanyId = companyId;
        Code = code.Trim().ToUpperInvariant();
        Name = name;
        ServiceType = serviceType;
        TariffType = tariffType;
        EffectiveFrom = effectiveFrom;
        RevenueAccountCode = revenueAccountCode;
        IsActive = true;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The tariff code.</summary>
    public string Code { get; private set; }

    /// <summary>The tariff name.</summary>
    public string Name { get; set; }

    /// <summary>The service type.</summary>
    public ServiceType ServiceType { get; private set; }

    /// <summary>The calculation type.</summary>
    public TariffType TariffType { get; private set; }

    /// <summary>The date the tariff takes effect.</summary>
    public DateOnly EffectiveFrom { get; private set; }

    /// <summary>A fixed / availability charge component.</summary>
    public decimal FixedCharge { get; set; }

    /// <summary>The per-unit rate (for <see cref="TariffType.PerUnit"/>).</summary>
    public decimal PerUnitRate { get; set; }

    /// <summary>The rate per unit of valuation, in cents (for <see cref="TariffType.RatePerValuation"/>).</summary>
    public decimal RatePerValuationCents { get; set; }

    /// <summary>The minimum charge (floor).</summary>
    public decimal MinimumCharge { get; set; }

    /// <summary>The revenue account billed to.</summary>
    public string RevenueAccountCode { get; private set; }

    /// <summary>Whether the tariff is active.</summary>
    public bool IsActive { get; set; }

    /// <summary>The stepped-tariff blocks (used when <see cref="TariffType"/> is <see cref="TariffType.Stepped"/>).</summary>
    public IReadOnlyList<TariffBlock> Blocks => _blocks.OrderBy(x => x.FromUnit).ToList();

    /// <summary>Adds a stepped block.</summary>
    /// <param name="fromUnit">The block's lower bound (inclusive).</param>
    /// <param name="toUnit">The block's upper bound (inclusive), or null for "and above".</param>
    /// <param name="rate">The per-unit rate in the block.</param>
    /// <returns>The created block.</returns>
    public TariffBlock AddBlock(decimal fromUnit, decimal? toUnit, decimal rate)
    {
        TariffBlock block = new(fromUnit, toUnit, rate);
        _blocks.Add(block);
        return block;
    }

    /// <summary>
    /// Calculates the charge for a billing period given the metered <paramref name="consumption"/> and the
    /// property <paramref name="valuation"/>, applying the minimum charge as a floor.
    /// </summary>
    /// <param name="consumption">The consumption (e.g. m³ of water); ignored for flat/valuation tariffs.</param>
    /// <param name="valuation">The property valuation (for rate-per-valuation tariffs).</param>
    /// <returns>The computed charge.</returns>
    public decimal CalculateCharge(decimal consumption, decimal valuation)
    {
        decimal charge = TariffType switch
        {
            TariffType.Flat => FixedCharge,
            TariffType.PerUnit => FixedCharge + consumption * PerUnitRate,
            TariffType.RatePerValuation => FixedCharge + valuation * RatePerValuationCents / 100m,
            TariffType.Stepped => FixedCharge + SteppedCharge(consumption),
            _ => FixedCharge,
        };

        charge = decimal.Round(charge, 4, MidpointRounding.ToEven);
        return charge < MinimumCharge ? MinimumCharge : charge;
    }

    private decimal SteppedCharge(decimal consumption)
    {
        // Blocks are cumulative bounds: FromUnit is the lower threshold (0-based) and ToUnit the upper
        // (null = open-ended). Units billed in a block = min(consumption, ToUnit) − FromUnit, floored at 0.
        decimal total = 0m;
        foreach (TariffBlock block in _blocks.OrderBy(b => b.FromUnit))
        {
            decimal upper = block.ToUnit ?? consumption;
            decimal unitsInBlock = Math.Min(consumption, upper) - block.FromUnit;
            if (unitsInBlock > 0m)
            {
                total += unitsInBlock * block.Rate;
            }
        }

        return total;
    }
}

/// <summary>A block of a stepped tariff (<c>mun.tariff_block</c>).</summary>
public sealed class TariffBlock : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private TariffBlock()
    {
    }

    /// <summary>Creates a block.</summary>
    /// <param name="fromUnit">The lower bound (inclusive).</param>
    /// <param name="toUnit">The upper bound (inclusive), or null for open-ended.</param>
    /// <param name="rate">The per-unit rate.</param>
    public TariffBlock(decimal fromUnit, decimal? toUnit, decimal rate)
    {
        FromUnit = fromUnit;
        ToUnit = toUnit;
        Rate = rate;
    }

    /// <summary>The owning tariff id.</summary>
    public long TariffId { get; private set; }

    /// <summary>The lower bound (inclusive).</summary>
    public decimal FromUnit { get; private set; }

    /// <summary>The upper bound (inclusive), or null for open-ended.</summary>
    public decimal? ToUnit { get; private set; }

    /// <summary>The per-unit rate in the block.</summary>
    public decimal Rate { get; private set; }
}
