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

/// <summary>One account row of the trial-balance movement report (base currency).</summary>
/// <param name="AccountCode">The account code.</param>
/// <param name="AccountName">The account name.</param>
/// <param name="Opening">The net balance brought forward at the start of the range (debit positive).</param>
/// <param name="Debit">Debits posted within the range.</param>
/// <param name="Credit">Credits posted within the range.</param>
/// <param name="Closing">The net balance carried forward at the end of the range (debit positive).</param>
public sealed record TrialBalanceMovementRow(string AccountCode, string AccountName, decimal Opening, decimal Debit, decimal Credit, decimal Closing);

/// <summary>The trial balance with period movement between two dates.</summary>
/// <param name="CompanyId">The company.</param>
/// <param name="FromDate">Start of the range.</param>
/// <param name="ToDate">End of the range.</param>
/// <param name="Rows">The per-account rows with any non-zero opening, movement or closing.</param>
public sealed record TrialBalanceMovementReport(long CompanyId, DateOnly FromDate, DateOnly ToDate, IReadOnlyList<TrialBalanceMovementRow> Rows);

/// <summary>One account on the chart-of-accounts listing.</summary>
/// <param name="AccountCode">The account code.</param>
/// <param name="AccountName">The account name.</param>
/// <param name="Type">The account type (Asset/Liability/Income/…).</param>
/// <param name="ReportCategory">The report category grouping.</param>
/// <param name="IsControl">Whether the account is a control account.</param>
/// <param name="IsActive">Whether the account is active.</param>
public sealed record ChartOfAccountsRow(string AccountCode, string AccountName, string Type, string ReportCategory, bool IsControl, bool IsActive);

/// <summary>The chart-of-accounts listing.</summary>
/// <param name="CompanyId">The company.</param>
/// <param name="Rows">The account rows, ordered by code.</param>
public sealed record ChartOfAccountsReport(long CompanyId, IReadOnlyList<ChartOfAccountsRow> Rows);

/// <summary>One headline figure on the financial snapshot.</summary>
/// <param name="Metric">The metric name.</param>
/// <param name="Amount">The amount (base currency).</param>
public sealed record FinancialSnapshotRow(string Metric, decimal Amount);

/// <summary>An executive financial snapshot as at a date (balance-sheet totals plus year-to-date profit).</summary>
/// <param name="AsAtDate">The as-at date.</param>
/// <param name="Rows">The headline figures.</param>
public sealed record FinancialSnapshotReport(DateOnly AsAtDate, IReadOnlyList<FinancialSnapshotRow> Rows);

/// <summary>One control account's reconciliation of GL balance against its sub-ledger (base currency).</summary>
/// <param name="Control">The control account (e.g. "Accounts Receivable").</param>
/// <param name="AccountCode">The GL account code.</param>
/// <param name="GlBalance">The GL balance in the account's natural sign.</param>
/// <param name="SubLedgerBalance">The sub-ledger total that should equal it.</param>
/// <param name="Difference">GL − sub-ledger (should be zero).</param>
public sealed record ControlReconciliationRow(string Control, string AccountCode, decimal GlBalance, decimal SubLedgerBalance, decimal Difference);

/// <summary>The control-account reconciliation as at a date.</summary>
/// <param name="AsAtDate">The as-at date.</param>
/// <param name="Rows">The per-control rows.</param>
public sealed record ControlReconciliationReport(DateOnly AsAtDate, IReadOnlyList<ControlReconciliationRow> Rows);

/// <summary>One tax-code line of the VAT breakdown (base currency).</summary>
/// <param name="TaxCode">The tax code (or "(untagged)" for VAT lines without a code).</param>
/// <param name="OutputTax">Output tax raised under this code (net credit on VAT-output accounts).</param>
/// <param name="InputTax">Input tax incurred under this code (net debit on VAT-input accounts).</param>
/// <param name="Net">Output − input for the code.</param>
public sealed record VatCodeRow(string TaxCode, decimal OutputTax, decimal InputTax, decimal Net);

/// <summary>The VAT return broken down by tax code for a period.</summary>
/// <param name="FromDate">Start of the period.</param>
/// <param name="ToDate">End of the period.</param>
/// <param name="Rows">The per-code rows.</param>
/// <param name="NetPayable">Total output − input across all codes.</param>
public sealed record VatReturnByCodeReport(DateOnly FromDate, DateOnly ToDate, IReadOnlyList<VatCodeRow> Rows, decimal NetPayable);

/// <summary>The VAT/sales-tax return for a period (base currency).</summary>
/// <param name="FromDate">Start of the return period.</param>
/// <param name="ToDate">End of the return period.</param>
/// <param name="OutputTax">Output tax on sales (net credit on VAT-output control accounts).</param>
/// <param name="InputTax">Input tax on purchases (net debit on VAT-input control accounts).</param>
/// <param name="NetPayable">Output tax − input tax (positive is payable to the authority).</param>
public sealed record VatReturnReport(DateOnly FromDate, DateOnly ToDate, decimal OutputTax, decimal InputTax, decimal NetPayable);

/// <summary>
/// The Statement of Cash Flows (IAS 7) derived by the movement method: each account's net movement over the
/// period is classified by its <c>CashFlowCategory</c> into operating, investing and financing activities;
/// because every journal balances, the sum of those (sign-reversed non-cash) movements equals the change in
/// cash and cash equivalents. Any movement on an untagged non-cash account falls into <paramref name="Unclassified"/>.
/// </summary>
/// <param name="FromDate">Start of the period.</param>
/// <param name="ToDate">End of the period.</param>
/// <param name="Operating">Net cash from operating activities.</param>
/// <param name="Investing">Net cash from investing activities.</param>
/// <param name="Financing">Net cash from financing activities.</param>
/// <param name="Unclassified">Net cash effect of movements on untagged non-cash accounts (should be zero when fully tagged).</param>
/// <param name="OpeningCash">Cash and cash equivalents at the start.</param>
/// <param name="ClosingCash">Cash and cash equivalents at the end.</param>
public sealed record CashFlowStatement(
    DateOnly FromDate, DateOnly ToDate,
    decimal Operating, decimal Investing, decimal Financing, decimal Unclassified,
    decimal OpeningCash, decimal ClosingCash)
{
    /// <summary>The net change in cash and cash equivalents over the period.</summary>
    public decimal NetChange => ClosingCash - OpeningCash;

    /// <summary>The change explained by the classified activities (equals <see cref="NetChange"/> when the books balance).</summary>
    public decimal ClassifiedChange => Operating + Investing + Financing + Unclassified;
}

/// <summary>
/// Read-side services producing the core general-ledger financial reports from posted data (brief §16
/// Phase 2). Every report is derived from <c>gl.period_balance</c> / <c>gl.journal_line</c>; the trial
/// balance must always balance.
/// </summary>
public interface IGeneralLedgerReports
{
    /// <summary>
    /// Produces the VAT/sales-tax return for a period: output tax raised on sales (net credit on VAT-output
    /// control accounts) less input tax on purchases (net debit on VAT-input control accounts), giving the net
    /// amount payable to the authority. Control accounts are found by their control-account type.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="fromDate">Start of the return period.</param>
    /// <param name="toDate">End of the return period.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The VAT return.</returns>
    Task<VatReturnReport> VatReturnAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct = default);

    /// <summary>
    /// Produces the VAT return broken down by tax code: for each code tagged on the VAT-control-account lines,
    /// the output tax (net credit on VAT-output accounts) and input tax (net debit on VAT-input accounts) for a
    /// period. VAT lines posted without a tax code are grouped under "(untagged)".
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="fromDate">Start of the period.</param>
    /// <param name="toDate">End of the period.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The VAT return by code.</returns>
    Task<VatReturnByCodeReport> VatReturnByCodeAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct = default);

    /// <summary>
    /// Reconciles the GL control accounts (AR, AP, Stock) against their sub-ledgers as at a date: the GL balance
    /// in the account's natural sign versus the sub-ledger total (open receivables, open payables, stock value),
    /// with the difference — which should be zero when the ledgers agree.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="asAtDate">The as-at date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The control-account reconciliation.</returns>
    Task<ControlReconciliationReport> ControlReconciliationAsync(long companyId, DateOnly asAtDate, CancellationToken ct = default);

    /// <summary>
    /// Produces an executive financial snapshot as at a date: total assets, liabilities and equity (from the
    /// balance sheet) plus year-to-date income, expenses and net profit (from the income statement for
    /// 1 January to the as-at date). A one-glance owner's summary.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="asAtDate">The as-at date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The financial snapshot.</returns>
    Task<FinancialSnapshotReport> FinancialSnapshotAsync(long companyId, DateOnly asAtDate, CancellationToken ct = default);

    /// <summary>Produces the chart-of-accounts listing (reference report), ordered by account code.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The chart-of-accounts report.</returns>
    Task<ChartOfAccountsReport> ChartOfAccountsAsync(long companyId, CancellationToken ct = default);

    /// <summary>
    /// Produces the trial balance with movement for a date range: per account the opening balance brought
    /// forward (everything before the range), the debits and credits within the range, and the closing balance.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="fromDate">Start of the range.</param>
    /// <param name="toDate">End of the range.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The trial-balance movement report.</returns>
    Task<TrialBalanceMovementReport> TrialBalanceMovementAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct = default);

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

    /// <summary>Produces the Statement of Cash Flows for a date range (movement method, classified by account tag).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="fromDate">Start of the period.</param>
    /// <param name="toDate">End of the period.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The cash flow statement.</returns>
    Task<CashFlowStatement> CashFlowStatementAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct = default);

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
