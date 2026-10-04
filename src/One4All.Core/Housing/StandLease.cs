using One4All.Core.Common;

namespace One4All.Core.Housing;

/// <summary>What a leased stand is used for.</summary>
public enum LeaseUse
{
    /// <summary>Commercial.</summary>
    Commercial = 1,

    /// <summary>Industrial.</summary>
    Industrial = 2,

    /// <summary>Agricultural.</summary>
    Agricultural = 3,
}

/// <summary>
/// A lease of a council stand (<c>hse.stand_lease</c>, brief §4.10.5: leases of stands, including long 99-year leases
/// with rent reviews): the lessee, use, term from the first of a month, first-year rent and fixed escalation at each
/// review, and the accounts it posts to — contractual rent billed through AR, income straight-lined against an accrued
/// lease income account (IFRS 16 lessor, operating lease).
/// </summary>
public sealed class StandLease : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private StandLease()
    {
        LeaseNumber = string.Empty;
        IncomeAccountCode = string.Empty;
        AccruedIncomeAccountCode = string.Empty;
    }

    /// <summary>Records a lease.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="leaseNumber">The lease number.</param>
    /// <param name="standId">The stand.</param>
    /// <param name="lesseeCustomerId">The lessee.</param>
    /// <param name="use">What the stand is used for.</param>
    /// <param name="startDate">The first day (the first of a month).</param>
    /// <param name="termYears">The term in years.</param>
    /// <param name="annualRent">The first year's rent.</param>
    /// <param name="reviewEveryYears">Years between rent reviews.</param>
    /// <param name="escalationPercent">The fixed escalation at each review.</param>
    /// <param name="incomeAccountCode">The lease income account.</param>
    /// <param name="accruedIncomeAccountCode">The accrued (straight-lining) lease income account.</param>
    public StandLease(long companyId, string leaseNumber, long standId, long lesseeCustomerId, LeaseUse use, DateOnly startDate, int termYears, decimal annualRent,
        int reviewEveryYears, decimal escalationPercent, string incomeAccountCode, string accruedIncomeAccountCode)
    {
        CompanyId = companyId;
        LeaseNumber = leaseNumber;
        StandId = standId;
        LesseeCustomerId = lesseeCustomerId;
        Use = use;
        StartDate = startDate;
        TermYears = termYears;
        AnnualRent = annualRent;
        ReviewEveryYears = reviewEveryYears;
        EscalationPercent = escalationPercent;
        IncomeAccountCode = incomeAccountCode;
        AccruedIncomeAccountCode = accruedIncomeAccountCode;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The lease number.</summary>
    public string LeaseNumber { get; private set; }

    /// <summary>The stand.</summary>
    public long StandId { get; private set; }

    /// <summary>The lessee.</summary>
    public long LesseeCustomerId { get; private set; }

    /// <summary>What the stand is used for.</summary>
    public LeaseUse Use { get; private set; }

    /// <summary>The first day.</summary>
    public DateOnly StartDate { get; private set; }

    /// <summary>The term in years.</summary>
    public int TermYears { get; private set; }

    /// <summary>The last day.</summary>
    public DateOnly EndDate => StartDate.AddYears(TermYears).AddDays(-1);

    /// <summary>The first year's rent.</summary>
    public decimal AnnualRent { get; private set; }

    /// <summary>Years between rent reviews.</summary>
    public int ReviewEveryYears { get; private set; }

    /// <summary>The fixed escalation at each review.</summary>
    public decimal EscalationPercent { get; private set; }

    /// <summary>The lease income account.</summary>
    public string IncomeAccountCode { get; private set; }

    /// <summary>The accrued (straight-lining) lease income account.</summary>
    public string AccruedIncomeAccountCode { get; private set; }
}
