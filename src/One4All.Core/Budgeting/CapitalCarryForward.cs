using One4All.Core.Common;

namespace One4All.Core.Budgeting;

/// <summary>
/// An approved carry-forward of unspent capital budget into the next year (<c>bud.capital_carry_forward</c>; brief §5 capital budget:
/// year-end carry-forward of unspent approved capital, approved as a separate act, not automatic). Itself the auditable document: what was
/// carried, from which year, into which budget, by whom and under which resolution.
/// </summary>
public sealed class CapitalCarryForward : AuditableEntity, ICompanyScoped
{
    private CapitalCarryForward()
    {
        AccountCode = string.Empty;
        Reference = string.Empty;
    }

    /// <summary>Records a carry-forward.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="fromFiscalYearId">The year the capital was unspent in.</param>
    /// <param name="toBudgetId">The next year's capital budget it goes into.</param>
    /// <param name="accountCode">The capital vote.</param>
    /// <param name="amount">The amount carried.</param>
    /// <param name="reference">The approval (resolution) reference.</param>
    /// <param name="approvedByUserId">Who approved it.</param>
    /// <param name="approvedOn">When.</param>
    public CapitalCarryForward(long companyId, long fromFiscalYearId, long toBudgetId, string accountCode, decimal amount, string reference, long approvedByUserId, DateOnly approvedOn)
    {
        CompanyId = companyId;
        FromFiscalYearId = fromFiscalYearId;
        ToBudgetId = toBudgetId;
        AccountCode = accountCode;
        Amount = amount;
        Reference = reference;
        ApprovedByUserId = approvedByUserId;
        ApprovedOn = approvedOn;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>Gets the year the capital was unspent in.</summary>
    public long FromFiscalYearId { get; private set; }

    /// <summary>Gets the budget it went into.</summary>
    public long ToBudgetId { get; private set; }

    /// <summary>Gets the capital vote.</summary>
    public string AccountCode { get; private set; }

    /// <summary>Gets the amount carried.</summary>
    public decimal Amount { get; private set; }

    /// <summary>Gets the approval reference.</summary>
    public string Reference { get; private set; }

    /// <summary>Gets who approved it.</summary>
    public long ApprovedByUserId { get; private set; }

    /// <summary>Gets when.</summary>
    public DateOnly ApprovedOn { get; private set; }
}
