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

    /// <summary>Gets or sets the month the tax year starts in (1 = January; e.g. 3 for a March–February tax year); year-to-date pay and cumulative tax follow it.</summary>
    public int TaxYearStartMonth { get; set; } = 1;

    /// <summary>Gets or sets the deemed (official) annual interest rate a cheap or interest-free staff loan's benefit is taxed at (null: no loan benefit).</summary>
    public decimal? DeemedLoanInterestRatePercent { get; set; }

    /// <summary>
    /// Gets or sets the market interest rate a stand buyer would pay for credit (IFRS 15 financing component): an instalment sale longer than a year at a
    /// lower rate has its revenue discounted to present value at this rate (null: no discounting).
    /// </summary>
    public decimal? HousingMarketRatePercent { get; set; }

    /// <summary>Gets or sets where budget utilisation alerts are emailed.</summary>
    public string? BudgetAlertEmail { get; set; }

    /// <summary>Gets or sets the utilisation thresholds alerted on, as percentages ("75, 90, 100"; blank: those).</summary>
    public string? BudgetAlertThresholds { get; set; }

    /// <summary>Gets or sets the income account interest charged on staff loans is credited to.</summary>
    public string? StaffLoanInterestAccountCode { get; set; }

    /// <summary>
    /// Gets or sets the server address approvers reach from their email (e.g. https://erp.example.com); when set, each approver with an email address is
    /// sent single-use approve / reject links as well as the in-app notice.
    /// </summary>
    public string? ApprovalLinkBaseUrl { get; set; }

    /// <summary>
    /// Gets or sets the net-pay movement from the group's last run (in percent, either way) above which a second, different approver
    /// is needed. Null needs one approver always.
    /// </summary>
    public decimal? PayrollSecondApprovalVariancePercent { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether committing a regular payroll run also raises the AP invoices for its third parties -
    /// schemes and garnishee creditors with a supplier (brief §4.15.5).
    /// </summary>
    public bool RaisePayrollRemittances { get; set; }

    /// <summary>Gets or sets what opens an e-mailed payslip PDF (the national ID by default).</summary>
    public Payroll.PayslipPasswordRule PayslipPasswordRule { get; set; }

    /// <summary>
    /// Gets or sets the report whose layout prints the payslip (the company's own design in the report designer, e.g. a copy of PR74), or
    /// null for the standard text payslip. The report runs for one employee and the pay date, and its pages become the PDF.
    /// </summary>
    public string? PayslipLayoutCode { get; set; }

    /// <summary>
    /// Gets or sets which report lays out each business document (JSON: document kind → report code, the company's own designs in the report designer,
    /// e.g. a copy of DOC11 for the tax invoice); a kind without an entry prints the standard text document.
    /// </summary>
    public string? DocumentLayoutsJson { get; set; }

    /// <summary>Gets or sets how many stands one beneficiary (checked on national ID, spouse included) may hold across the council (brief §4.10.4 "one stand, configurable to N"); a scheme may set its own.</summary>
    public int StandsPerBeneficiary { get; set; } = 1;

    /// <summary>
    /// Gets or sets the smallest cash amount the tills deal in (e.g. 0.05) when coins are scarce: a cash sale may be tendered up to half of
    /// it away from the exact total, the difference posted to <see cref="CashRoundingAccountCode"/> (brief §4.1). Null: no rounding.
    /// </summary>
    public decimal? CashRoundingIncrement { get; set; }

    /// <summary>Gets or sets the account cash rounding differences post to.</summary>
    public string? CashRoundingAccountCode { get; set; }

    /// <summary>
    /// The currency the till gives change in when it is not the base currency (brief §4.1 multi-currency tendering): change is converted at the day's
    /// rate and rounded to that currency's smallest cash unit, the difference posted to <see cref="CashRoundingAccountCode"/>. Null: change in base.
    /// </summary>
    public string? PosChangeCurrency { get; set; }

    /// <summary>
    /// The withholding tax code deducted from any payment to a supplier without a valid tax clearance on the payment date when the
    /// payment names none itself (brief §3.5). Null: withholding only where a payment asks for it.
    /// </summary>
    public string? WithholdingWithoutClearanceTaxCode { get; set; }

    /// <summary>Whether supplier payment runs must be saved, reviewed and approved by a second person before they pay (brief §4.6).</summary>
    public bool PaymentRunApprovalRequired { get; set; }

    /// <summary>
    /// Gets or sets the income account settlement discounts taken from suppliers are credited to; while it is set, payment runs pay invoices still
    /// within a supplier's discount days (even before they are due) less the discount.
    /// </summary>
    public string? SettlementDiscountReceivedAccountCode { get; set; }

    /// <summary>How far (percent of the ordered quantity) a goods receipt may go over a purchase order line without a supervisor (brief §4.3).</summary>
    public decimal OverReceiptTolerancePercent { get; set; }

    /// <summary>How far (percent) a supplier invoice price may differ from the order price and still match (brief §4.3 three-way match); a clerk may tighten it per match, never widen it.</summary>
    public decimal InvoicePriceTolerancePercent { get; set; } = 5m;

    /// <summary>How far (percent of the quantity received and not yet invoiced) a supplier invoice quantity may exceed the receipt and still match.</summary>
    public decimal InvoiceQuantityTolerancePercent { get; set; }

    /// <summary>
    /// Gets or sets the largest discount off the price list a sales order may carry without authority (brief §4.4 price and discount control); beyond it
    /// an authoriser who may override prices signs it off, or the company's SalesDiscount workflow approves it. Null: no limit.
    /// </summary>
    public decimal? MaxSalesDiscountPercent { get; set; }

    /// <summary>The fee discount for children of staff while the parent is employed (0: none); the better of it and any sibling discount applies.</summary>
    public decimal StaffChildDiscountPercent { get; set; }

    /// <summary>
    /// How many days back a user may date a posting without the back-date permission (null: no back-dating control). Applies to postings made
    /// by a known user (manual journals and documents keyed by a user).
    /// </summary>
    public int? BackDateAllowanceDays { get; set; }

    /// <summary>Whether a rate keyed on a customer or supplier document needs the rate-override permission and is recorded (brief §3.3).</summary>
    public bool RateOverrideControl { get; set; }

    /// <summary>
    /// Whether stock may go negative only by exception (brief §3.6): an issue doing so needs a reason and a supervisor with Inventory › Stock Issue
    /// authorise; a till sale doing so is let through but recorded for review; each is kept (and corrected when the receipt lands).
    /// </summary>
    public bool NegativeStockControl { get; set; }

    /// <summary>
    /// Whether batch / serial numbers are enforced (brief §4.2): a receipt of a tracked item must carry them, and an issue must name them or
    /// takes the earliest-expiry batches on hand.
    /// </summary>
    public bool EnforceLotTracking { get; set; }

    /// <summary>The company's logo (PNG or JPEG) for report layouts (expression <c>CompanyLogo</c>).</summary>
    public byte[]? Logo { get; set; }

    /// <summary>The company's report house style (fonts, colours, footer, logo placement) as JSON, applied to every report; null for none.</summary>
    public string? ReportThemeJson { get; set; }

    /// <summary>Gets or sets the most a refund without a receipt may pay out (null: such refunds are not allowed).</summary>
    public decimal? UnreceiptedRefundLimit { get; set; }

    /// <summary>Gets or sets how many days after a sale an ordinary supervisor may authorise its refund; later needs the late-refund authority (null: no limit).</summary>
    public int? RefundWindowDays { get; set; }

    /// <summary>
    /// Whether fund control is on (brief §4.4 / §4.18.5): expenditure cannot be posted to a budget-enabled expense account
    /// (a vote) that has no appropriation in the year's active approved or supplementary budget.
    /// </summary>
    public bool FundControlEnabled { get; set; }

    /// <summary>Gets or sets a value indicating whether a budget must go through preparation and approval before it can be activated.</summary>
    public bool BudgetApprovalRequired { get; set; }

    /// <summary>
    /// Gets or sets the source modules (comma-separated, e.g. "AR, AP, POS") whose postings are queued as deferred journals and posted in a controlled release
    /// (brief §3.2 rule 8, the Sage 200 deferred-posting option); blank posts everything straight away. Sub-ledgers update at once either way.
    /// </summary>
    public string? DeferredPostingModules { get; set; }

    /// <summary>Whether postings from a source module are queued rather than posted at once.</summary>
    /// <param name="sourceModule">The source module.</param>
    /// <returns>True when queued.</returns>
    public bool DefersPostingsFor(string? sourceModule)
        => !string.IsNullOrWhiteSpace(DeferredPostingModules) && !string.IsNullOrWhiteSpace(sourceModule)
            && DeferredPostingModules.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(m => m.Equals(sourceModule, StringComparison.OrdinalIgnoreCase));

    /// <summary>Gets or sets a value indicating whether the company keeps fund accounts: every journal line names a fund and each fund balances within the journal.</summary>
    public bool FundAccounting { get; set; }

    /// <summary>Gets or sets the company's locale (e.g. "en-ZW", "pt-MZ"): its screen language, and the date and number formats of its reports (null: English, invariant formats).</summary>
    public string? Locale { get; set; }

    /// <summary>Gets or sets when VAT becomes due: invoice date, tax point, or cash accounting.</summary>
    public One4All.Core.Accounting.TaxPointBasis TaxPointBasis { get; set; } = One4All.Core.Accounting.TaxPointBasis.InvoiceDate;

    /// <summary>Gets or sets the fund lines without one fall into under fund accounting (the general / rate fund), so sub-ledger postings need not name one.</summary>
    public string? DefaultFund { get; set; }

    /// <summary>Sets or clears the second reporting currency.</summary>
    /// <param name="reportingCurrency">The reporting currency, or null to disable dual-currency reporting.</param>
    public void SetReportingCurrency(CurrencyCode? reportingCurrency) => ReportingCurrency = reportingCurrency;
}
