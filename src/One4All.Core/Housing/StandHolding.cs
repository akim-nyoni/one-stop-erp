using One4All.Core.Common;

namespace One4All.Core.Housing;

/// <summary>How joint holders hold a stand (brief §4.10.6 survivorship rules).</summary>
public enum CoOwnershipRegime
{
    /// <summary>Joint tenancy: a deceased holder's share passes to the survivors.</summary>
    JointWithSurvivorship = 1,

    /// <summary>Tenancy in common: each share passes with its holder's estate.</summary>
    InCommon = 2,
}

/// <summary>
/// A stand held jointly (<c>hse.stand_co_ownership</c>): the regime the holders hold it under; severed to tenancy in common
/// when one holder's share is transferred on its own.
/// </summary>
public sealed class StandCoOwnership : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private StandCoOwnership()
    {
    }

    /// <summary>Registers joint ownership.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="standId">The stand.</param>
    /// <param name="regime">The regime.</param>
    public StandCoOwnership(long companyId, long standId, CoOwnershipRegime regime)
    {
        CompanyId = companyId;
        StandId = standId;
        Regime = regime;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The stand.</summary>
    public long StandId { get; private set; }

    /// <summary>The regime.</summary>
    public CoOwnershipRegime Regime { get; private set; }

    /// <summary>Severs a joint tenancy into a tenancy in common.</summary>
    public void Sever() => Regime = CoOwnershipRegime.InCommon;
}

/// <summary>
/// A holder's share of a stand for a period (<c>hse.stand_holding</c>): append-only — a change ends the current periods and
/// opens new ones, so the holders on any date can always be shown.
/// </summary>
public sealed class StandHolding : Entity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private StandHolding()
    {
        Reason = string.Empty;
    }

    /// <summary>Records a share.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="standId">The stand.</param>
    /// <param name="customerId">The holder.</param>
    /// <param name="sharePercent">The share.</param>
    /// <param name="heldFrom">From when.</param>
    /// <param name="reason">What gave rise to it.</param>
    public StandHolding(long companyId, long standId, long customerId, decimal sharePercent, DateOnly heldFrom, string reason)
    {
        CompanyId = companyId;
        StandId = standId;
        CustomerId = customerId;
        SharePercent = sharePercent;
        HeldFrom = heldFrom;
        Reason = reason;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The stand.</summary>
    public long StandId { get; private set; }

    /// <summary>The holder.</summary>
    public long CustomerId { get; private set; }

    /// <summary>The share.</summary>
    public decimal SharePercent { get; private set; }

    /// <summary>From when.</summary>
    public DateOnly HeldFrom { get; private set; }

    /// <summary>Until when (inclusive); current while null.</summary>
    public DateOnly? HeldTo { get; private set; }

    /// <summary>What gave rise to it.</summary>
    public string Reason { get; private set; }

    /// <summary>Whether the holder has died (the share sits with the estate under tenancy in common).</summary>
    public bool HolderDeceased { get; private set; }

    /// <summary>Ends the period.</summary>
    /// <param name="date">The last day held.</param>
    public void End(DateOnly date) => HeldTo = date;

    /// <summary>Records the holder's death.</summary>
    public void MarkDeceased() => HolderDeceased = true;
}
