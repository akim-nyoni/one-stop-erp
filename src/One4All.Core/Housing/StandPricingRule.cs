using One4All.Core.Common;

namespace One4All.Core.Housing;

/// <summary>What a pricing rule prices, most specific first (brief §4.10.2: by scheme, by size band, or per stand).</summary>
public enum PricingScope
{
    /// <summary>One stand.</summary>
    Stand = 1,

    /// <summary>The stands of a scheme within a size band.</summary>
    SizeBand = 2,

    /// <summary>Every stand of a scheme.</summary>
    Scheme = 3,
}

/// <summary>
/// An effective-dated stand pricing rule (<c>hse.stand_pricing_rule</c>, brief §4.10.2 selling price build-up): the
/// overhead recovery, development levy, margin and rounding applied to a stand's capitalised cost. Rules are never edited —
/// a price change is a new rule from a later date — and only an approved rule prices a stand.
/// </summary>
public sealed class StandPricingRule : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private StandPricingRule()
    {
    }

    /// <summary>Proposes a rule.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="scope">What it prices.</param>
    /// <param name="schemeId">The scheme (scheme and size-band rules).</param>
    /// <param name="minSize">The smallest stand in the band.</param>
    /// <param name="maxSize">The largest stand in the band.</param>
    /// <param name="standId">The stand (stand rules).</param>
    /// <param name="effectiveFrom">When it takes effect.</param>
    /// <param name="overheadPercent">Overhead / administration recovery on cost.</param>
    /// <param name="levyPerStand">Development levy per stand.</param>
    /// <param name="levyPerSquareMetre">Development levy per square metre.</param>
    /// <param name="marginPercent">Margin on cost plus overhead.</param>
    /// <param name="roundTo">Rounding of the price.</param>
    /// <param name="proposedByUserId">Who proposed it.</param>
    public StandPricingRule(long companyId, PricingScope scope, long? schemeId, decimal? minSize, decimal? maxSize, long? standId, DateOnly effectiveFrom,
        decimal overheadPercent, decimal levyPerStand, decimal levyPerSquareMetre, decimal marginPercent, decimal roundTo, long proposedByUserId)
    {
        CompanyId = companyId;
        Scope = scope;
        SchemeId = schemeId;
        MinSize = minSize;
        MaxSize = maxSize;
        StandId = standId;
        EffectiveFrom = effectiveFrom;
        OverheadPercent = overheadPercent;
        LevyPerStand = levyPerStand;
        LevyPerSquareMetre = levyPerSquareMetre;
        MarginPercent = marginPercent;
        RoundTo = roundTo;
        ProposedByUserId = proposedByUserId;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>What it prices.</summary>
    public PricingScope Scope { get; private set; }

    /// <summary>The scheme.</summary>
    public long? SchemeId { get; private set; }

    /// <summary>The smallest stand in the band.</summary>
    public decimal? MinSize { get; private set; }

    /// <summary>The largest stand in the band.</summary>
    public decimal? MaxSize { get; private set; }

    /// <summary>The stand.</summary>
    public long? StandId { get; private set; }

    /// <summary>When it takes effect.</summary>
    public DateOnly EffectiveFrom { get; private set; }

    /// <summary>Overhead / administration recovery on cost.</summary>
    public decimal OverheadPercent { get; private set; }

    /// <summary>Development levy per stand.</summary>
    public decimal LevyPerStand { get; private set; }

    /// <summary>Development levy per square metre.</summary>
    public decimal LevyPerSquareMetre { get; private set; }

    /// <summary>Margin on cost plus overhead.</summary>
    public decimal MarginPercent { get; private set; }

    /// <summary>Rounding of the price.</summary>
    public decimal RoundTo { get; private set; }

    /// <summary>Who proposed it.</summary>
    public long ProposedByUserId { get; private set; }

    /// <summary>Who approved it.</summary>
    public long? ApprovedByUserId { get; private set; }

    /// <summary>When it was approved; only approved rules price stands.</summary>
    public DateOnly? ApprovedOn { get; private set; }

    /// <summary>Approves it.</summary>
    /// <param name="userId">Who.</param>
    /// <param name="date">When.</param>
    public void Approve(long userId, DateOnly date)
    {
        ApprovedByUserId = userId;
        ApprovedOn = date;
    }
}
