using One4All.Core.Accounting;
using One4All.Core.Common;

namespace One4All.Core.System;

/// <summary>
/// A company (legal entity / set of books) in the System Manager (<c>sys.company</c>). Owns its
/// functional (base) currency and an optional second reporting currency for dual-currency reporting
/// (IAS 21, brief §3.3), plus its display time zone. Almost every other record is scoped to a company.
/// </summary>
public sealed class Company : SoftDeletableEntity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private Company()
    {
        Code = string.Empty;
        Name = string.Empty;
        TimeZoneId = "UTC";
    }

    /// <summary>Creates a company.</summary>
    /// <param name="code">Short unique code.</param>
    /// <param name="name">Legal/display name.</param>
    /// <param name="baseCurrency">The functional (base) currency.</param>
    /// <param name="timeZoneId">IANA/Windows time-zone id used to display timestamps.</param>
    public Company(string code, string name, CurrencyCode baseCurrency, string timeZoneId = "UTC")
    {
        Code = code;
        Name = name;
        BaseCurrency = baseCurrency;
        TimeZoneId = timeZoneId;
    }

    /// <summary>Short unique company code.</summary>
    public string Code { get; private set; }

    /// <summary>Legal/display name.</summary>
    public string Name { get; set; }

    /// <summary>Optional registration/tax number.</summary>
    public string? RegistrationNumber { get; set; }

    /// <summary>Optional tax (VAT) number.</summary>
    public string? TaxNumber { get; set; }

    /// <summary>The functional (base) currency in which the books are kept.</summary>
    public CurrencyCode BaseCurrency { get; private set; }

    /// <summary>Optional second reporting currency maintained on every posting (dual-currency reporting).</summary>
    public CurrencyCode? ReportingCurrency { get; private set; }

    /// <summary>The time zone used to display <c>timestamptz</c> values for this company.</summary>
    public string TimeZoneId { get; set; }

    /// <summary>Whether posting to future periods is permitted (per the posting engine's period check).</summary>
    public bool AllowFuturePeriodPosting { get; set; }

    /// <summary>
    /// The value above which a manual journal needs a second user's approval before it posts (maker-checker): the posting
    /// engine refuses a manual journal worth more than this in base currency unless it was submitted for approval. Zero
    /// puts every manual journal through approval; null turns approval off.
    /// </summary>
    public decimal? ManualJournalApprovalThreshold { get; set; }

    /// <summary>
    /// Net-pay protection (brief §4.15.4): the minimum take-home pay as an amount. When deductions would take net pay below the floor
    /// (the higher of this and <see cref="MinimumTakeHomePercent"/> of gross), the run suspends lower-priority deductions and
    /// carries them forward as arrears. Null with a null percentage turns protection off.
    /// </summary>
    public decimal? MinimumTakeHomeAmount { get; set; }

    /// <summary>Gets or sets the minimum take-home pay as a percentage of gross (see <see cref="MinimumTakeHomeAmount"/>).</summary>
    public decimal? MinimumTakeHomePercent { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether payroll runs need maker-checker approval (brief §4.15.6): calculate, approve by someone
    /// else, then commit. When set, a direct run is refused.
    /// </summary>
    public bool PayrollApprovalRequired { get; set; }

    /// <summary>
    /// Gets or sets the net-pay movement from the group's last run (in percent, either way) above which a second, different approver
    /// is needed. Null needs one approver always.
    /// </summary>
    public decimal? PayrollSecondApprovalVariancePercent { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether committing a regular payroll run also raises the AP invoices for its third parties —
    /// schemes and garnishee creditors with a supplier (brief §4.15.5).
    /// </summary>
    public bool RaisePayrollRemittances { get; set; }

    /// <summary>Gets or sets what opens an e-mailed payslip PDF (the national ID by default).</summary>
    public Payroll.PayslipPasswordRule PayslipPasswordRule { get; set; }

    /// <summary>
    /// Gets or sets the smallest cash amount the tills deal in (e.g. 0.05) when coins are scarce: a cash sale may be tendered up to half of
    /// it away from the exact total, the difference posted to <see cref="CashRoundingAccountCode"/> (brief §4.1). Null: no rounding.
    /// </summary>
    public decimal? CashRoundingIncrement { get; set; }

    /// <summary>Gets or sets the account cash rounding differences post to.</summary>
    public string? CashRoundingAccountCode { get; set; }

    /// <summary>Gets or sets the most a refund without a receipt may pay out (null: such refunds are not allowed).</summary>
    public decimal? UnreceiptedRefundLimit { get; set; }

    /// <summary>
    /// Whether fund control is on (brief §4.4 / §4.18.5): expenditure cannot be posted to a budget-enabled expense account
    /// (a vote) that has no appropriation in the year's active approved or supplementary budget.
    /// </summary>
    public bool FundControlEnabled { get; set; }

    /// <summary>Gets or sets a value indicating whether a budget must go through preparation and approval before it can be activated.</summary>
    public bool BudgetApprovalRequired { get; set; }

    /// <summary>Gets or sets a value indicating whether the company keeps fund accounts: every journal line names a fund and each fund balances within the journal.</summary>
    public bool FundAccounting { get; set; }

    /// <summary>Gets or sets the fund lines without one fall into under fund accounting (the general / rate fund), so sub-ledger postings need not name one.</summary>
    public string? DefaultFund { get; set; }

    /// <summary>Sets or clears the second reporting currency.</summary>
    /// <param name="reportingCurrency">The reporting currency, or null to disable dual-currency reporting.</param>
    public void SetReportingCurrency(CurrencyCode? reportingCurrency) => ReportingCurrency = reportingCurrency;
}
