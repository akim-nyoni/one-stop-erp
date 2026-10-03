using One4All.Core.Common;

namespace One4All.Core.Procurement;

/// <summary>
/// A supplier volume rebate agreement (<c>pop.rebate_agreement</c>, brief §4.3): for purchases from the supplier in the
/// period, the supplier pays back a rate that rises with spend. The rebate earned so far is accrued as a receivable, and
/// the agreement remembers what has been accrued so each run books only the change.
/// </summary>
public sealed class RebateAgreement : AuditableEntity, ICompanyScoped
{
    private readonly List<RebateAgreementTier> _tiers = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private RebateAgreement()
    {
        Code = string.Empty;
    }

    /// <summary>Creates an agreement.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="supplierId">The supplier.</param>
    /// <param name="code">The agreement code.</param>
    /// <param name="from">The first day of the period.</param>
    /// <param name="to">The last day of the period.</param>
    public RebateAgreement(long companyId, long supplierId, string code, DateOnly from, DateOnly to)
    {
        CompanyId = companyId;
        SupplierId = supplierId;
        Code = code.Trim().ToUpperInvariant();
        FromDate = from;
        ToDate = to;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The supplier.</summary>
    public long SupplierId { get; private set; }

    /// <summary>The agreement code.</summary>
    public string Code { get; private set; }

    /// <summary>The first day of the period.</summary>
    public DateOnly FromDate { get; private set; }

    /// <summary>The last day of the period.</summary>
    public DateOnly ToDate { get; private set; }

    /// <summary>The rebate accrued so far.</summary>
    public decimal Accrued { get; private set; }

    /// <summary>The tiers.</summary>
    public IReadOnlyList<RebateAgreementTier> Tiers => _tiers.AsReadOnly();

    /// <summary>Adds a tier.</summary>
    /// <param name="threshold">The spend from which it applies.</param>
    /// <param name="ratePercent">Its rate.</param>
    public void AddTier(decimal threshold, decimal ratePercent) => _tiers.Add(new RebateAgreementTier(threshold, ratePercent));

    /// <summary>Records a change in what has been accrued.</summary>
    /// <param name="amount">The change.</param>
    public void Accrue(decimal amount) => Accrued += amount;
}

/// <summary>A tier of a rebate agreement (<c>pop.rebate_agreement_tier</c>).</summary>
public sealed class RebateAgreementTier : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private RebateAgreementTier()
    {
    }

    /// <summary>Creates a tier.</summary>
    /// <param name="threshold">The spend from which it applies.</param>
    /// <param name="ratePercent">Its rate.</param>
    public RebateAgreementTier(decimal threshold, decimal ratePercent)
    {
        Threshold = threshold;
        RatePercent = ratePercent;
    }

    /// <summary>The agreement.</summary>
    public long RebateAgreementId { get; private set; }

    /// <summary>The spend from which it applies.</summary>
    public decimal Threshold { get; private set; }

    /// <summary>Its rate.</summary>
    public decimal RatePercent { get; private set; }
}
