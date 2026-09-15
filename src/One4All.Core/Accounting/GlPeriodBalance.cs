using One4All.Core.Common;

namespace One4All.Core.Accounting;

/// <summary>
/// Accumulated debit/credit movements for one composite account key in one fiscal period
/// (<c>gl.period_balance</c>, brief §3.2 rule 7). Maintained by the posting engine in the same
/// transaction as the journal, so the trial balance is always derivable by summing these rows without
/// scanning every line. Amounts are held in base and (optionally) reporting currency.
/// </summary>
public sealed class GlPeriodBalance : Entity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private GlPeriodBalance()
    {
        AccountCode = string.Empty;
        CostCentre = string.Empty;
        Department = string.Empty;
    }

    /// <summary>Creates a zeroed period balance for an account key.</summary>
    /// <param name="companyId">The owning company.</param>
    /// <param name="fiscalYearId">The fiscal year.</param>
    /// <param name="fiscalPeriodId">The fiscal period.</param>
    /// <param name="key">The composite account key.</param>
    public GlPeriodBalance(long companyId, long fiscalYearId, long fiscalPeriodId, AccountKey key)
    {
        CompanyId = companyId;
        FiscalYearId = fiscalYearId;
        FiscalPeriodId = fiscalPeriodId;
        AccountCode = key.AccountCode;
        CostCentre = key.CostCentre;
        Department = key.Department;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The fiscal year.</summary>
    public long FiscalYearId { get; private set; }

    /// <summary>The fiscal period.</summary>
    public long FiscalPeriodId { get; private set; }

    /// <summary>The base account code.</summary>
    public string AccountCode { get; private set; }

    /// <summary>The cost-centre code.</summary>
    public string CostCentre { get; private set; }

    /// <summary>The department code.</summary>
    public string Department { get; private set; }

    /// <summary>Accumulated base-currency debits in the period.</summary>
    public decimal BaseDebit { get; private set; }

    /// <summary>Accumulated base-currency credits in the period.</summary>
    public decimal BaseCredit { get; private set; }

    /// <summary>Accumulated reporting-currency debits in the period.</summary>
    public decimal ReportingDebit { get; private set; }

    /// <summary>Accumulated reporting-currency credits in the period.</summary>
    public decimal ReportingCredit { get; private set; }

    /// <summary>The net base movement (debit − credit) for the period.</summary>
    public decimal NetBase => BaseDebit - BaseCredit;

    /// <summary>The composite account key.</summary>
    public AccountKey Key => new(AccountCode, CostCentre, Department);

    /// <summary>Adds a movement to the period balance.</summary>
    /// <param name="baseDebit">Base-currency debit.</param>
    /// <param name="baseCredit">Base-currency credit.</param>
    /// <param name="reportingDebit">Reporting-currency debit.</param>
    /// <param name="reportingCredit">Reporting-currency credit.</param>
    public void AddMovement(decimal baseDebit, decimal baseCredit, decimal reportingDebit = 0m, decimal reportingCredit = 0m)
    {
        BaseDebit += baseDebit;
        BaseCredit += baseCredit;
        ReportingDebit += reportingDebit;
        ReportingCredit += reportingCredit;
    }
}
