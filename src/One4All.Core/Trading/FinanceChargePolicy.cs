using One4All.Core.Common;

namespace One4All.Core.Trading;

/// <summary>
/// A company's standing terms for interest on overdue customer accounts (<c>trading.finance_charge_policy</c>, brief §4.4): the annual rate, the grace
/// days, the minimum charge, the income account, whether interest compounds and whether indigent customers are exempt. The scheduled task
/// <c>ar.finance-charges</c> raises the charges on these terms, so a council or business charges interest every month without anyone running it.
/// </summary>
public sealed class FinanceChargePolicy : AuditableEntity, ICompanyScoped
{
    private FinanceChargePolicy()
    {
        InterestIncomeAccountCode = string.Empty;
        DocumentPrefix = "FC";
    }

    /// <summary>Creates a company's policy.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="annualRatePercent">The annual rate.</param>
    /// <param name="interestIncomeAccountCode">The interest income account.</param>
    public FinanceChargePolicy(long companyId, decimal annualRatePercent, string interestIncomeAccountCode)
    {
        CompanyId = companyId;
        AnnualRatePercent = annualRatePercent;
        InterestIncomeAccountCode = interestIncomeAccountCode;
        DocumentPrefix = "FC";
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>Gets or sets the annual interest rate (percent).</summary>
    public decimal AnnualRatePercent { get; set; }

    /// <summary>Gets or sets the days after the due date before interest starts.</summary>
    public int GraceDays { get; set; } = 30;

    /// <summary>Gets or sets the smallest charge raised (smaller ones are skipped).</summary>
    public decimal MinimumCharge { get; set; }

    /// <summary>Gets or sets the interest income account.</summary>
    public string InterestIncomeAccountCode { get; set; }

    /// <summary>Gets or sets the finance-charge document prefix.</summary>
    public string DocumentPrefix { get; set; }

    /// <summary>Gets or sets a value indicating whether interest is charged on earlier interest.</summary>
    public bool Compound { get; set; }

    /// <summary>Gets or sets a value indicating whether indigent customers are exempt.</summary>
    public bool ExemptIndigent { get; set; } = true;
}
