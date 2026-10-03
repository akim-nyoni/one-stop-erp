using One4All.Core.Accounting;
using One4All.Core.Common;

namespace One4All.Application.Abstractions.Reporting;

/// <summary>A line on a consolidated trial balance (an account summed across the group companies).</summary>
/// <param name="AccountCode">The account code.</param>
/// <param name="AccountName">The account name.</param>
/// <param name="Debit">The net debit balance across the group.</param>
/// <param name="Credit">The net credit balance across the group.</param>
public sealed record ConsolidatedTrialBalanceRow(string AccountCode, string AccountName, decimal Debit, decimal Credit);

/// <summary>The consolidated trial balance for a set of companies as at a date.</summary>
/// <param name="AsAtDate">The as-at date.</param>
/// <param name="CompanyIds">The consolidated companies.</param>
/// <param name="Rows">The account rows (after any intercompany elimination).</param>
/// <param name="IntercompanyEliminated">The gross intercompany debit balance removed by elimination (0 when none requested).</param>
/// <param name="IntercompanyUnmatched">The net intercompany balance that did not eliminate (debit positive); its rows are kept so the imbalance is visible.</param>
public sealed record ConsolidatedTrialBalance(DateOnly AsAtDate, IReadOnlyList<long> CompanyIds, IReadOnlyList<ConsolidatedTrialBalanceRow> Rows, decimal IntercompanyEliminated = 0m, decimal IntercompanyUnmatched = 0m)
{
    /// <summary>The total debit.</summary>
    public decimal TotalDebit => Rows.Sum(r => r.Debit);

    /// <summary>The total credit.</summary>
    public decimal TotalCredit => Rows.Sum(r => r.Credit);

    /// <summary>True when the consolidated debits equal the credits.</summary>
    public bool IsBalanced => TotalDebit == TotalCredit;
}

/// <summary>How one company's ledger was translated into the presentation currency.</summary>
/// <param name="CompanyId">The company.</param>
/// <param name="CompanyCode">Its code.</param>
/// <param name="BaseCurrency">Its functional (base) currency.</param>
/// <param name="ClosingRate">The closing rate applied to its assets and liabilities (1 when already in the presentation currency).</param>
/// <param name="TranslationDifference">Its contribution to the translation reserve (credit positive).</param>
public sealed record CompanyTranslation(long CompanyId, string CompanyCode, string BaseCurrency, decimal ClosingRate, decimal TranslationDifference);

/// <summary>A consolidated trial balance translated into one presentation currency.</summary>
/// <param name="AsAtDate">The as-at date.</param>
/// <param name="PresentationCurrency">The currency the group is presented in.</param>
/// <param name="Rows">The account rows, including the translation reserve row when there is one.</param>
/// <param name="Companies">How each company was translated.</param>
/// <param name="TranslationReserve">The cumulative translation difference (credit positive), shown on the reserve row.</param>
/// <param name="IntercompanyEliminated">The gross intercompany debit balance removed by elimination.</param>
/// <param name="IntercompanyUnmatched">The net intercompany balance that did not eliminate (debit positive).</param>
public sealed record TranslatedTrialBalance(DateOnly AsAtDate, string PresentationCurrency, IReadOnlyList<ConsolidatedTrialBalanceRow> Rows, IReadOnlyList<CompanyTranslation> Companies, decimal TranslationReserve, decimal IntercompanyEliminated = 0m, decimal IntercompanyUnmatched = 0m)
{
    /// <summary>The account code of the translation reserve row.</summary>
    public const string TranslationReserveCode = "CTA";

    /// <summary>The total debit.</summary>
    public decimal TotalDebit => Rows.Sum(r => r.Debit);

    /// <summary>The total credit.</summary>
    public decimal TotalCredit => Rows.Sum(r => r.Credit);

    /// <summary>True when the translated debits equal the credits (always, by construction of the reserve).</summary>
    public bool IsBalanced => TotalDebit == TotalCredit;
}

/// <summary>
/// Consolidates the ledgers of several companies (brief §2, the CONSOLIDATION feature): sums each account
/// across the group into a single trial balance. The plain consolidation requires a shared base currency; the
/// translated consolidation first translates each company into a presentation currency (IAS 21 current-rate
/// method). Because every company's own trial balance balances, the consolidated one does too.
/// </summary>
public interface IConsolidationService
{
    /// <summary>Produces the consolidated trial balance for the companies as at a date.</summary>
    /// <param name="companyIds">The companies to consolidate (all sharing a base currency).</param>
    /// <param name="asAtDate">The as-at date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The consolidated trial balance, or an error.</returns>
    Task<Result<ConsolidatedTrialBalance>> ConsolidatedTrialBalanceAsync(IReadOnlyList<long> companyIds, DateOnly asAtDate, CancellationToken ct = default);

    /// <summary>
    /// Produces the consolidated trial balance with intercompany elimination: the named intercompany
    /// accounts (due-from / due-to) are summed across the group and, when they net to zero, dropped from the
    /// rows; any residual is reported as <c>IntercompanyUnmatched</c> and its rows kept.
    /// </summary>
    /// <param name="companyIds">The companies to consolidate (all sharing a base currency).</param>
    /// <param name="asAtDate">The as-at date.</param>
    /// <param name="intercompanyAccountCodes">The intercompany account codes to eliminate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The consolidated trial balance, or an error.</returns>
    Task<Result<ConsolidatedTrialBalance>> ConsolidatedTrialBalanceAsync(IReadOnlyList<long> companyIds, DateOnly asAtDate, IReadOnlyCollection<string> intercompanyAccountCodes, CancellationToken ct = default);

    /// <summary>
    /// Produces the consolidated trial balance for companies with different base currencies, translated into a
    /// presentation currency by the IAS 21 current-rate method: asset and liability balances at the closing rate at
    /// the date; equity, income and expense movements at the daily rate of each posting date. The difference is the
    /// cumulative translation reserve, shown as its own row (<see cref="TranslatedTrialBalance.TranslationReserveCode"/>),
    /// so the result balances. Intercompany accounts are eliminated after translation, as in the plain consolidation.
    /// </summary>
    /// <param name="companyIds">The companies to consolidate.</param>
    /// <param name="presentationCurrency">The currency to present the group in.</param>
    /// <param name="asAtDate">The as-at date.</param>
    /// <param name="intercompanyAccountCodes">Intercompany account codes to eliminate, if any.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The translated trial balance, or an error (e.g. <c>Consolidation.NoRate</c> when a rate is missing).</returns>
    Task<Result<TranslatedTrialBalance>> TranslatedTrialBalanceAsync(IReadOnlyList<long> companyIds, CurrencyCode presentationCurrency, DateOnly asAtDate, IReadOnlyCollection<string>? intercompanyAccountCodes = null, CancellationToken ct = default);
}
