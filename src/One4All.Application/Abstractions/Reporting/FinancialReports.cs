using One4All.Core.Accounting;

namespace One4All.Application.Abstractions.Reporting;

/// <summary>One line of the trial balance.</summary>
/// <param name="AccountCode">The account code.</param>
/// <param name="AccountName">The account name.</param>
/// <param name="Type">The account type.</param>
/// <param name="Debit">The debit balance (0 if the account is in credit).</param>
/// <param name="Credit">The credit balance (0 if the account is in debit).</param>
public sealed record TrialBalanceRow(string AccountCode, string AccountName, AccountType Type, decimal Debit, decimal Credit);

/// <summary>The trial balance as at a date, in base currency (brief §14 report 1).</summary>
/// <param name="CompanyId">The company.</param>
/// <param name="AsAtDate">The as-at date.</param>
/// <param name="Currency">The currency the amounts are stated in.</param>
/// <param name="Rows">The account rows (non-zero balances).</param>
/// <param name="TotalDebit">Sum of debit balances.</param>
/// <param name="TotalCredit">Sum of credit balances.</param>
public sealed record TrialBalanceReport(
    long CompanyId, DateOnly AsAtDate, string Currency,
    IReadOnlyList<TrialBalanceRow> Rows, decimal TotalDebit, decimal TotalCredit)
{
    /// <summary>True when total debits equal total credits — the trial balance balances.</summary>
    public bool IsBalanced => TotalDebit == TotalCredit;
}

/// <summary>A line in a financial statement (P&amp;L or balance sheet), grouped by report category.</summary>
/// <param name="Category">The report category.</param>
/// <param name="AccountCode">The account code.</param>
/// <param name="AccountName">The account name.</param>
/// <param name="Amount">The signed amount in the statement's natural presentation.</param>
public sealed record StatementLine(string Category, string AccountCode, string AccountName, decimal Amount);

/// <summary>The statement of profit or loss for a period range (brief §14 report 7).</summary>
/// <param name="CompanyId">The company.</param>
/// <param name="FromDate">Start of the range.</param>
/// <param name="ToDate">End of the range.</param>
/// <param name="Currency">The currency.</param>
/// <param name="Income">Income lines (positive = income earned).</param>
/// <param name="Expenses">Expense lines (positive = expense incurred).</param>
public sealed record IncomeStatement(
    long CompanyId, DateOnly FromDate, DateOnly ToDate, string Currency,
    IReadOnlyList<StatementLine> Income, IReadOnlyList<StatementLine> Expenses)
{
    /// <summary>Total income.</summary>
    public decimal TotalIncome => Income.Sum(l => l.Amount);

    /// <summary>Total expenses.</summary>
    public decimal TotalExpenses => Expenses.Sum(l => l.Amount);

    /// <summary>Net profit (income − expenses) for the range.</summary>
    public decimal NetProfit => TotalIncome - TotalExpenses;
}

/// <summary>The statement of financial position as at a date (brief §14 report 6).</summary>
/// <param name="CompanyId">The company.</param>
/// <param name="AsAtDate">The as-at date.</param>
/// <param name="Currency">The currency.</param>
/// <param name="Assets">Asset lines.</param>
/// <param name="Liabilities">Liability lines.</param>
/// <param name="Equity">Equity lines (including current-year retained earnings).</param>
public sealed record BalanceSheet(
    long CompanyId, DateOnly AsAtDate, string Currency,
    IReadOnlyList<StatementLine> Assets, IReadOnlyList<StatementLine> Liabilities, IReadOnlyList<StatementLine> Equity)
{
    /// <summary>Total assets.</summary>
    public decimal TotalAssets => Assets.Sum(l => l.Amount);

    /// <summary>Total liabilities.</summary>
    public decimal TotalLiabilities => Liabilities.Sum(l => l.Amount);

    /// <summary>Total equity (including current-year earnings).</summary>
    public decimal TotalEquity => Equity.Sum(l => l.Amount);

    /// <summary>True when assets equal liabilities plus equity — the balance sheet balances.</summary>
    public bool IsBalanced => TotalAssets == TotalLiabilities + TotalEquity;
}

/// <summary>One movement line in a nominal-activity enquiry.</summary>
/// <param name="Date">The posting date.</param>
/// <param name="JournalNumber">The journal number.</param>
/// <param name="Narrative">The line/journal narrative.</param>
/// <param name="Debit">Base debit.</param>
/// <param name="Credit">Base credit.</param>
/// <param name="RunningBalance">The running base balance after this line.</param>
public sealed record NominalActivityLine(DateOnly Date, string JournalNumber, string? Narrative, decimal Debit, decimal Credit, decimal RunningBalance);

/// <summary>The nominal activity (account ledger detail) for one account over a date range (brief §14 report 3).</summary>
/// <param name="CompanyId">The company.</param>
/// <param name="AccountCode">The account code.</param>
/// <param name="AccountName">The account name.</param>
/// <param name="OpeningBalance">Opening base balance.</param>
/// <param name="Lines">The movement lines.</param>
/// <param name="ClosingBalance">Closing base balance.</param>
public sealed record NominalActivityReport(
    long CompanyId, string AccountCode, string AccountName, decimal OpeningBalance,
    IReadOnlyList<NominalActivityLine> Lines, decimal ClosingBalance);

/// <summary>One posted journal in the daybook (journal-listing) enquiry.</summary>
/// <param name="Date">The posting date.</param>
/// <param name="JournalNumber">The journal number.</param>
/// <param name="SourceModule">The originating module (GL, AR, AP, CB, POS, …).</param>
/// <param name="DocumentType">The document type.</param>
/// <param name="DocumentNumber">The document number.</param>
/// <param name="Narrative">The journal narrative.</param>
/// <param name="Amount">The journal's base-currency total (sum of debits = sum of credits).</param>
public sealed record JournalDaybookLine(DateOnly Date, string JournalNumber, string SourceModule, string? DocumentType, string? DocumentNumber, string? Narrative, decimal Amount);

/// <summary>The journal daybook: posted journals over a date range, newest first (audit day-book).</summary>
/// <param name="CompanyId">The company.</param>
/// <param name="FromDate">Start of the range.</param>
/// <param name="ToDate">End of the range.</param>
/// <param name="Lines">The journal lines.</param>
/// <param name="Total">The sum of the journals' base totals.</param>
public sealed record JournalDaybookReport(
    long CompanyId, DateOnly FromDate, DateOnly ToDate, IReadOnlyList<JournalDaybookLine> Lines, decimal Total);

/// <summary>
/// Read-side services producing the core general-ledger financial reports from posted data (brief §16
/// Phase 2). Every report is derived from <c>gl.period_balance</c> / <c>gl.journal_line</c>; the trial
/// balance must always balance.
/// </summary>
public interface IGeneralLedgerReports
{
    /// <summary>Produces the trial balance as at a date.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="asAtDate">The as-at date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The trial balance.</returns>
    Task<TrialBalanceReport> TrialBalanceAsync(long companyId, DateOnly asAtDate, CancellationToken ct = default);

    /// <summary>Produces the statement of profit or loss for a date range.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="fromDate">Start of the range.</param>
    /// <param name="toDate">End of the range.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The income statement.</returns>
    Task<IncomeStatement> IncomeStatementAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct = default);

    /// <summary>Produces the statement of financial position as at a date.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="asAtDate">The as-at date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The balance sheet.</returns>
    Task<BalanceSheet> BalanceSheetAsync(long companyId, DateOnly asAtDate, CancellationToken ct = default);

    /// <summary>Produces the nominal activity for one account over a date range.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="accountCode">The account code.</param>
    /// <param name="fromDate">Start of the range.</param>
    /// <param name="toDate">End of the range.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The nominal activity report.</returns>
    Task<NominalActivityReport> NominalActivityAsync(long companyId, string accountCode, DateOnly fromDate, DateOnly toDate, CancellationToken ct = default);

    /// <summary>Produces the journal daybook (posted journals) for a date range.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="fromDate">Start of the range.</param>
    /// <param name="toDate">End of the range.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The journal daybook report.</returns>
    Task<JournalDaybookReport> JournalDaybookAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct = default);
}
