using One4All.Core.Common;

namespace One4All.Application.Abstractions.Banking;

/// <summary>A statement line to import.</summary>
/// <param name="StatementDate">The statement date.</param>
/// <param name="Description">The description.</param>
/// <param name="Amount">The signed amount (+ in, − out).</param>
/// <param name="Reference">Optional reference.</param>
public readonly record struct StatementLineImport(DateOnly StatementDate, string Description, decimal Amount, string? Reference);

/// <summary>An unreconciled item (statement line or GL posting) on the reconciliation.</summary>
/// <param name="Date">The date.</param>
/// <param name="Description">A description/reference.</param>
/// <param name="Amount">The signed amount.</param>
public readonly record struct ReconciliationItem(DateOnly Date, string Description, decimal Amount);

/// <summary>The bank reconciliation statement (brief §4.7 report 69).</summary>
/// <param name="BankAccountCode">The bank account.</param>
/// <param name="AsAtDate">The as-at date.</param>
/// <param name="GlBalance">The GL cash-book balance.</param>
/// <param name="StatementBalance">The imported statement balance.</param>
/// <param name="UnpresentedGlItems">GL postings not yet appearing on the statement.</param>
/// <param name="UnmatchedStatementItems">Statement lines not yet in the GL.</param>
public sealed record BankReconciliationReport(
    string BankAccountCode, DateOnly AsAtDate, decimal GlBalance, decimal StatementBalance,
    IReadOnlyList<ReconciliationItem> UnpresentedGlItems, IReadOnlyList<ReconciliationItem> UnmatchedStatementItems)
{
    /// <summary>The residual difference after accounting for the unreconciled items (should be zero when fully reconciled).</summary>
    public decimal Difference =>
        StatementBalance - GlBalance + UnpresentedGlItems.Sum(i => i.Amount) - UnmatchedStatementItems.Sum(i => i.Amount);
}

/// <summary>One bank account on the cash-position report.</summary>
/// <param name="Code">The bank account code.</param>
/// <param name="Name">The bank account name.</param>
/// <param name="Currency">The account currency.</param>
/// <param name="GlAccountCode">The GL cash-book account.</param>
/// <param name="Balance">The GL cash-book balance (debit positive) as at the date.</param>
/// <param name="UnreconciledLines">Imported statement lines not yet reconciled.</param>
public sealed record CashPositionRow(string Code, string Name, string Currency, string GlAccountCode, decimal Balance, int UnreconciledLines);

/// <summary>The cash position: every active bank account's cash-book balance as at a date.</summary>
/// <param name="AsAtDate">The as-at date.</param>
/// <param name="Rows">One row per active bank account.</param>
/// <param name="TotalBalance">The total across all accounts (base currency).</param>
public sealed record CashPositionReport(DateOnly AsAtDate, IReadOnlyList<CashPositionRow> Rows, decimal TotalBalance);

/// <summary>A statement line on the reconciliation work list.</summary>
/// <param name="Id">The statement line id.</param>
/// <param name="Date">The statement date.</param>
/// <param name="Description">The bank's description.</param>
/// <param name="Reference">The bank's reference, if any.</param>
/// <param name="Amount">The signed amount (+ in, − out).</param>
/// <param name="MatchedJournalLineId">The GL line it is matched to, or null while unreconciled.</param>
public sealed record StatementWorkItem(long Id, DateOnly Date, string Description, string? Reference, decimal Amount, long? MatchedJournalLineId);

/// <summary>A GL cash-book posting on the reconciliation work list.</summary>
/// <param name="Id">The journal line id.</param>
/// <param name="Date">The posting date.</param>
/// <param name="JournalNumber">The journal number.</param>
/// <param name="DocumentNumber">The journal's document number, if any.</param>
/// <param name="Amount">The signed amount on the bank account (debit positive).</param>
public sealed record GlWorkItem(long Id, DateOnly Date, string JournalNumber, string? DocumentNumber, decimal Amount);

/// <summary>The reconciliation work list for a bank account as at a date.</summary>
/// <param name="StatementLines">Every statement line dated on or before the date (matched ones carry their GL line id).</param>
/// <param name="UnpresentedGlLines">GL postings on the bank account not matched to any statement line.</param>
public sealed record BankWorkList(IReadOnlyList<StatementWorkItem> StatementLines, IReadOnlyList<GlWorkItem> UnpresentedGlLines);

/// <summary>Imports bank statements and reconciles them to the GL cash book (brief §4.7).</summary>
public interface IBankReconciliationService
{
    /// <summary>
    /// The reconciliation work list: statement lines (with their match, if any) and the unpresented GL
    /// postings, both with ids so a screen can match and unmatch them. Derived the same way as the report.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="bankAccountId">The bank account.</param>
    /// <param name="asAtDate">The as-at date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The work list, or not-found.</returns>
    Task<Result<BankWorkList>> WorkListAsync(long companyId, long bankAccountId, DateOnly asAtDate, CancellationToken ct = default);

    /// <summary>
    /// Produces the cash position as at a date: each active bank account's GL cash-book balance (and how
    /// many imported statement lines are still unreconciled), with the total — the daily treasury view.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="asAtDate">The as-at date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The cash position.</returns>
    Task<Result<CashPositionReport>> CashPositionAsync(long companyId, DateOnly asAtDate, CancellationToken ct = default);

    /// <summary>Imports statement lines for a bank account.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="bankAccountId">The bank account.</param>
    /// <param name="lines">The lines to import.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number imported, or an error.</returns>
    Task<Result<int>> ImportStatementLinesAsync(long companyId, long bankAccountId, IReadOnlyList<StatementLineImport> lines, CancellationToken ct = default);

    /// <summary>
    /// Parses a bank's CSV export with the given layout and imports every line. Refuses the whole file when
    /// any line fails to parse (the error lists each bad line), so a partial statement is never imported.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="bankAccountId">The bank account.</param>
    /// <param name="csv">The CSV content.</param>
    /// <param name="layout">The bank's column layout.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of lines imported.</returns>
    Task<Result<int>> ImportStatementCsvAsync(long companyId, long bankAccountId, string csv, One4All.Application.Banking.BankCsvLayout layout, CancellationToken ct = default);

    /// <summary>
    /// Imports a statement file in any supported format (brief §4.7: CSV, MT940, camt.053). As for CSV, the whole file is
    /// refused when any entry cannot be read.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="bankAccountId">The bank account.</param>
    /// <param name="content">The file content.</param>
    /// <param name="format">The file format.</param>
    /// <param name="csvLayout">The column layout, for CSV files (the simple layout when omitted).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of lines imported.</returns>
    Task<Result<int>> ImportStatementFileAsync(long companyId, long bankAccountId, string content, One4All.Application.Banking.StatementFileFormat format, One4All.Application.Banking.BankCsvLayout? csvLayout = null, CancellationToken ct = default);

    /// <summary>Auto-matches unreconciled statement lines to unmatched GL cash-book postings by equal amount.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="bankAccountId">The bank account.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of lines matched, or an error.</returns>
    Task<Result<int>> AutoMatchAsync(long companyId, long bankAccountId, int dateToleranceDays = One4All.Application.Banking.BankAutoMatcher.DefaultDateToleranceDays, CancellationToken ct = default);

    /// <summary>
    /// Manually matches an unreconciled statement line to a specific GL cash-book posting on the bank
    /// account (operator judgement, where auto-match by amount could not). Refuses a line that is already
    /// reconciled, a journal line that is not a posting on the bank's GL account, or one already matched.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="bankAccountId">The bank account.</param>
    /// <param name="statementLineId">The statement line to match.</param>
    /// <param name="journalLineId">The GL cash-book journal line to match it to.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or NotFound/Rule.</returns>
    Task<Result> MatchLineAsync(long companyId, long bankAccountId, long statementLineId, long journalLineId, CancellationToken ct = default);

    /// <summary>Un-matches a previously reconciled statement line, returning it to the unreconciled pool.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="statementLineId">The statement line to un-match.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or NotFound/Rule when the line is missing or not reconciled.</returns>
    Task<Result> UnmatchLineAsync(long companyId, long statementLineId, CancellationToken ct = default);

    /// <summary>Produces the bank reconciliation statement as at a date.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="bankAccountId">The bank account.</param>
    /// <param name="asAtDate">The as-at date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The reconciliation, or an error.</returns>
    Task<Result<BankReconciliationReport>> GetReconciliationAsync(long companyId, long bankAccountId, DateOnly asAtDate, CancellationToken ct = default);

    /// <summary>
    /// Signs off the reconciliation to a date and locks the period (brief §4.7): the reconciliation must agree to the
    /// cent with every statement line to that date in the ledger. Afterwards nothing can be posted to the bank's ledger
    /// account, imported onto its statement, or unmatched, on or before the date.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="bankAccountId">The bank account.</param>
    /// <param name="asAtDate">The date reconciled to; after any earlier sign-off.</param>
    /// <param name="userId">Who signs it off.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The reconciliation signed off, or why it cannot be.</returns>
    Task<Result<BankReconciliationReport>> SignOffAsync(long companyId, long bankAccountId, DateOnly asAtDate, long userId, CancellationToken ct = default);
}
