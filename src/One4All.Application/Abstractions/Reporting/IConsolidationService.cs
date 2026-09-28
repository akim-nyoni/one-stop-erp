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
/// <param name="Rows">The account rows.</param>
public sealed record ConsolidatedTrialBalance(DateOnly AsAtDate, IReadOnlyList<long> CompanyIds, IReadOnlyList<ConsolidatedTrialBalanceRow> Rows)
{
    /// <summary>The total debit.</summary>
    public decimal TotalDebit => Rows.Sum(r => r.Debit);

    /// <summary>The total credit.</summary>
    public decimal TotalCredit => Rows.Sum(r => r.Credit);

    /// <summary>True when the consolidated debits equal the credits.</summary>
    public bool IsBalanced => TotalDebit == TotalCredit;
}

/// <summary>
/// Consolidates the ledgers of several companies (brief §2, the CONSOLIDATION feature): sums each account
/// across the group into a single trial balance. This is a simple line-by-line consolidation for companies
/// sharing a base currency (intercompany eliminations and foreign-currency translation are out of scope);
/// because every company's own trial balance balances, the consolidated one does too.
/// </summary>
public interface IConsolidationService
{
    /// <summary>Produces the consolidated trial balance for the companies as at a date.</summary>
    /// <param name="companyIds">The companies to consolidate (all sharing a base currency).</param>
    /// <param name="asAtDate">The as-at date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The consolidated trial balance, or an error.</returns>
    Task<Result<ConsolidatedTrialBalance>> ConsolidatedTrialBalanceAsync(IReadOnlyList<long> companyIds, DateOnly asAtDate, CancellationToken ct = default);
}
