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

    /// <summary>Electricity (conventional or prepaid).</summary>
    Electricity = 7,

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

    /// <summary>A fixed availability charge plus a rate per square metre of the stand's area.</summary>
    AreaBased = 4,
}

/// <summary>
/// A municipal tariff (brief §4.9, <c>mun.tariff</c>) with a flexible calculation and effective-dated
/// versions so historical bills can be recomputed and explained. Stepped tariffs use <see cref="Blocks"/>.
/// </summary>
public sealed class Tariff : SoftDeletableEntity, ICompanyScoped
{
    private readonly List<TariffBlock> _blocks = [];
    private readonly List<TariffSeason> _seasons = [];

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

    /// <summary>The rate per square metre of the stand's area (for <see cref="TariffType.AreaBased"/>).</summary>
    public decimal RatePerSquareMetre { get; set; }

    /// <summary>The minimum charge (floor).</summary>
    public decimal MinimumCharge { get; set; }

    /// <summary>The revenue account billed to.</summary>
    public string RevenueAccountCode { get; private set; }

    /// <summary>Whether the tariff is active.</summary>
    public bool IsActive { get; set; }

    /// <summary>The consumer category the tariff applies to (brief §4.9: tariffs vary by category).</summary>
    public ConsumerCategory Category { get; set; } = ConsumerCategory.Domestic;

    /// <summary>The ward the tariff applies to, or null for the whole council.</summary>
    public string? Ward { get; set; }

    /// <summary>How often the charge is billed (brief §4.9): its amounts are per billing period of this cycle.</summary>
    public BillingCycle Cycle { get; private set; } = BillingCycle.Monthly;

    /// <summary>The month the cycle starts in (e.g. 7 for a July–June rates year).</summary>
    public int CycleStartMonth { get; private set; } = 1;

    /// <summary>For a quarterly, half-yearly or annual charge: billed over this many monthly instalments from the start of the period (1 = all at once).</summary>
    public int Instalments { get; private set; } = 1;

    /// <summary>Sets the billing cycle.</summary>
    /// <param name="cycle">How often.</param>
    /// <param name="startMonth">The month the cycle starts in (1–12).</param>
    /// <param name="instalments">Monthly instalments within a period (1 to the months in the period).</param>
    public void SetCycle(BillingCycle cycle, int startMonth, int instalments)
    {
        if (startMonth is < 1 or > 12 || instalments < 1 || instalments > (int)cycle)
        {
            throw new ArgumentOutOfRangeException(nameof(instalments), "The start month is 1–12 and instalments run from 1 to the months in the period.");
        }

        Cycle = cycle;
        CycleStartMonth = startMonth;
        Instalments = instalments;
    }

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

    /// <summary>The seasonal per-unit rates (brief §4.9: seasonal rates), each covering a range of months.</summary>
    public IReadOnlyList<TariffSeason> Seasons => _seasons.AsReadOnly();

    /// <summary>
    /// Adds a seasonal per-unit rate covering the months from <paramref name="fromMonth"/> to <paramref name="toMonth"/>
    /// inclusive. A range wraps the year when the first month is after the last (for example November to February).
    /// </summary>
    /// <param name="fromMonth">The first month (1–12).</param>
    /// <param name="toMonth">The last month (1–12).</param>
    /// <param name="perUnitRate">The per-unit rate in that season.</param>
    /// <returns>The created season.</returns>
    public TariffSeason AddSeason(int fromMonth, int toMonth, decimal perUnitRate)
    {
        TariffSeason season = new(fromMonth, toMonth, perUnitRate);
        _seasons.Add(season);
        return season;
    }

    /// <summary>The per-unit rate for a month: a covering season's rate, or the standard rate when none covers it.</summary>
    /// <param name="month">The month (1–12).</param>
    /// <returns>The rate.</returns>
    public decimal PerUnitRateFor(int month)
    {
        foreach (TariffSeason season in _seasons)
        {
            bool covers = season.FromMonth <= season.ToMonth
                ? month >= season.FromMonth && month <= season.ToMonth
                : month >= season.FromMonth || month <= season.ToMonth; // wraps the year end
            if (covers)
            {
                return season.PerUnitRate;
            }
        }

        return PerUnitRate;
    }

    /// <summary>
    /// Calculates the charge for a billing period given the metered <paramref name="consumption"/> and the
    /// property <paramref name="valuation"/>, applying the minimum charge as a floor.
    /// </summary>
    /// <param name="consumption">The consumption (e.g. m³ of water); ignored for flat/valuation tariffs.</param>
    /// <param name="valuation">The property valuation (for rate-per-valuation tariffs).</param>
    /// <returns>The computed charge.</returns>
    public decimal CalculateCharge(decimal consumption, decimal valuation) => CalculateCharge(consumption, valuation, 0m);

    /// <summary>
    /// Calculates the charge for a billing period given the metered <paramref name="consumption"/>, the property
    /// <paramref name="valuation"/> and its <paramref name="areaSquareMetres"/>, applying the minimum charge as a floor.
    /// </summary>
    /// <param name="consumption">The consumption (e.g. m³ of water); ignored for flat/valuation/area tariffs.</param>
    /// <param name="valuation">The property valuation (for rate-per-valuation tariffs).</param>
    /// <param name="areaSquareMetres">The stand's area (for area-based tariffs).</param>
    /// <returns>The computed charge.</returns>
    public decimal CalculateCharge(decimal consumption, decimal valuation, decimal areaSquareMetres) => CalculateCharge(consumption, valuation, areaSquareMetres, null);

    /// <summary>
    /// Calculates the charge, using the per-unit rate for <paramref name="billMonth"/> when the tariff has seasonal
    /// rates (brief §4.9). Applies the minimum charge as a floor.
    /// </summary>
    /// <param name="consumption">The consumption (e.g. m³ of water); ignored for flat/valuation/area tariffs.</param>
    /// <param name="valuation">The property valuation (for rate-per-valuation tariffs).</param>
    /// <param name="areaSquareMetres">The stand's area (for area-based tariffs).</param>
    /// <param name="billMonth">The billing month (1–12) for seasonal rates, or null to use the standard rate.</param>
    /// <returns>The computed charge.</returns>
    public decimal CalculateCharge(decimal consumption, decimal valuation, decimal areaSquareMetres, int? billMonth)
    {
        decimal perUnit = billMonth is int month ? PerUnitRateFor(month) : PerUnitRate;
        decimal charge = TariffType switch
        {
            TariffType.Flat => FixedCharge,
            TariffType.PerUnit => FixedCharge + consumption * perUnit,
            TariffType.RatePerValuation => FixedCharge + valuation * RatePerValuationCents / 100m,
            TariffType.AreaBased => FixedCharge + areaSquareMetres * RatePerSquareMetre,
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

/// <summary>
/// A seasonal per-unit rate on a tariff (<c>mun.tariff_season</c>, brief §4.9 seasonal rates): the per-unit rate that
/// applies for the months from <see cref="FromMonth"/> to <see cref="ToMonth"/> inclusive, wrapping the year end when
/// the first month is after the last.
/// </summary>
public sealed class TariffSeason : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private TariffSeason()
    {
    }

    /// <summary>Creates a season.</summary>
    /// <param name="fromMonth">The first month (1–12).</param>
    /// <param name="toMonth">The last month (1–12).</param>
    /// <param name="perUnitRate">The per-unit rate in that season.</param>
    public TariffSeason(int fromMonth, int toMonth, decimal perUnitRate)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(fromMonth, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(fromMonth, 12);
        ArgumentOutOfRangeException.ThrowIfLessThan(toMonth, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(toMonth, 12);
        FromMonth = fromMonth;
        ToMonth = toMonth;
        PerUnitRate = perUnitRate;
    }

    /// <summary>The owning tariff id.</summary>
    public long TariffId { get; private set; }

    /// <summary>The first month (1–12).</summary>
    public int FromMonth { get; private set; }

    /// <summary>The last month (1–12).</summary>
    public int ToMonth { get; private set; }

    /// <summary>The per-unit rate in that season.</summary>
    public decimal PerUnitRate { get; private set; }
}

/// <summary>How often a municipal charge is billed; the value is the months in a period.</summary>
public enum BillingCycle
{
    /// <summary>Every month.</summary>
    Monthly = 1,

    /// <summary>Every three months.</summary>
    Quarterly = 3,

    /// <summary>Every six months.</summary>
    HalfYearly = 6,

    /// <summary>Once a year.</summary>
    Annual = 12,
}
