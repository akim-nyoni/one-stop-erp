using One4All.Core.Common;

namespace One4All.Core.Budgeting;

/// <summary>A utilisation threshold a vote has been alerted for (<c>bgt.alert_sent</c>), so each vote and threshold is emailed once a year.</summary>
public sealed class BudgetAlertSent : Entity, ICompanyScoped
{
    private BudgetAlertSent()
    {
        AccountCode = string.Empty;
    }

    /// <summary>Records the alert.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="fiscalYearId">The year.</param>
    /// <param name="accountCode">The vote.</param>
    /// <param name="threshold">The threshold crossed.</param>
    /// <param name="sentOnUtc">When it was sent.</param>
    public BudgetAlertSent(long companyId, long fiscalYearId, string accountCode, decimal threshold, DateTimeOffset sentOnUtc)
    {
        CompanyId = companyId;
        FiscalYearId = fiscalYearId;
        AccountCode = accountCode;
        Threshold = threshold;
        SentOnUtc = sentOnUtc;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the year.</summary>
    public long FiscalYearId { get; private set; }

    /// <summary>Gets the vote.</summary>
    public string AccountCode { get; private set; }

    /// <summary>Gets the threshold crossed.</summary>
    public decimal Threshold { get; private set; }

    /// <summary>Gets when it was sent.</summary>
    public DateTimeOffset SentOnUtc { get; private set; }
}
