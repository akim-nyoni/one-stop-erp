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
    /// Whether fund control is on (brief §4.4 / §4.18.5): expenditure cannot be posted to a budget-enabled expense account
    /// (a vote) that has no appropriation in the year's active approved or supplementary budget.
    /// </summary>
    public bool FundControlEnabled { get; set; }

    /// <summary>Sets or clears the second reporting currency.</summary>
    /// <param name="reportingCurrency">The reporting currency, or null to disable dual-currency reporting.</param>
    public void SetReportingCurrency(CurrencyCode? reportingCurrency) => ReportingCurrency = reportingCurrency;
}
