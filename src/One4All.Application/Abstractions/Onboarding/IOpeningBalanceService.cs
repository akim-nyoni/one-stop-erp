using One4All.Core.Common;
using One4All.Core.Trading;

namespace One4All.Application.Abstractions.Onboarding;

/// <summary>A problem with one row of an import file.</summary>
/// <param name="LineNumber">The file line.</param>
/// <param name="Message">What is wrong.</param>
public sealed record ImportError(int LineNumber, string Message);

/// <summary>What an import found or did.</summary>
/// <param name="Rows">Data rows read.</param>
/// <param name="Created">Records created or postings made (0 on a preview).</param>
/// <param name="Skipped">Rows for records that already exist (left as they are).</param>
/// <param name="Errors">Problems; when there are any, nothing was written.</param>
/// <param name="Committed">Whether the import was written (false for a preview or when there were errors).</param>
public sealed record ImportReport(int Rows, int Created, int Skipped, IReadOnlyList<ImportError> Errors, bool Committed);

/// <summary>A control account against its sub-ledger.</summary>
/// <param name="Ledger">AR, AP or stock.</param>
/// <param name="ControlBalance">The control account balance.</param>
/// <param name="SubLedgerBalance">The sub-ledger total.</param>
public sealed record ControlCheck(string Ledger, decimal ControlBalance, decimal SubLedgerBalance)
{
    /// <summary>Gets a value indicating whether they agree.</summary>
    public bool Agrees => Math.Abs(ControlBalance - SubLedgerBalance) < 0.01m;
}

/// <summary>The go-live reconciliation after migrating opening balances.</summary>
/// <param name="SuspenseAccountCode">The migration suspense account.</param>
/// <param name="SuspenseBalance">What is left in it (zero when everything agreed).</param>
/// <param name="Controls">Control accounts against sub-ledgers.</param>
/// <param name="TrialBalanceLoaded">Whether the opening trial balance was loaded.</param>
public sealed record OpeningBalanceReconciliation(string SuspenseAccountCode, decimal SuspenseBalance, IReadOnlyList<ControlCheck> Controls, bool TrialBalanceLoaded)
{
    /// <summary>Gets a value indicating whether the company is ready to go live.</summary>
    public bool Ready => TrialBalanceLoaded && Math.Abs(SuspenseBalance) < 0.01m && Controls.All(c => c.Agrees);
}

/// <summary>
/// The opening balance wizard and master data import (brief §10): customers, suppliers and items from CSV templates; opening stock
/// (quantity and cost); open receivable and payable items; and the legacy trial balance - sub-ledgers and stock against the migration
/// suspense account, the trial balance's other accounts with the suspense taking the control balances, so the suspense nets to zero
/// when the legacy sub-ledgers agree with their controls. Every import validates first: <c>commit = false</c> previews; any error writes
/// nothing. Columns are named in the header row (dates yyyy-MM-dd, numbers with a dot).
/// </summary>
public interface IOpeningBalanceService
{
    /// <summary>Customers: code, name, currency, credit_limit (optional).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="csv">The file.</param>
    /// <param name="commit">Write, or only validate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The report.</returns>
    Task<ImportReport> ImportCustomersAsync(long companyId, string csv, bool commit, CancellationToken ct = default);

    /// <summary>Suppliers: code, name, currency.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="csv">The file.</param>
    /// <param name="commit">Write, or only validate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The report.</returns>
    Task<ImportReport> ImportSuppliersAsync(long companyId, string csv, bool commit, CancellationToken ct = default);

    /// <summary>Items: code, description, type (Stock, Service… optional), revenue_account, cost_account (optional), category (optional).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="csv">The file.</param>
    /// <param name="commit">Write, or only validate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The report.</returns>
    Task<ImportReport> ImportItemsAsync(long companyId, string csv, bool commit, CancellationToken ct = default);

    /// <summary>Opening stock: item, warehouse, quantity, unit_cost - received on the date against the suspense account.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="csv">The file.</param>
    /// <param name="date">The opening date.</param>
    /// <param name="suspenseAccountCode">The migration suspense account.</param>
    /// <param name="commit">Write, or only validate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The report.</returns>
    Task<ImportReport> ImportStockAsync(long companyId, string csv, DateOnly date, string suspenseAccountCode, bool commit, CancellationToken ct = default);

    /// <summary>
    /// Open items: trader, document, date, amount (positive for an invoice, negative for a credit) - posted to the ledger against the
    /// suspense account at their own dates and numbers.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="ledger">Receivables or payables.</param>
    /// <param name="csv">The file.</param>
    /// <param name="suspenseAccountCode">The migration suspense account.</param>
    /// <param name="commit">Write, or only validate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The report.</returns>
    Task<ImportReport> ImportOpenItemsAsync(long companyId, TradingLedger ledger, string csv, string suspenseAccountCode, bool commit, CancellationToken ct = default);

    /// <summary>
    /// The legacy trial balance: account, debit, credit - must balance. Non-control accounts are posted as at the date in one opening
    /// journal; control accounts are brought in by the sub-ledger imports, so the suspense account takes their trial balance figures.
    /// Loaded once (<c>Migration.TrialBalanceLoaded</c>).
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="csv">The file.</param>
    /// <param name="date">The opening date.</param>
    /// <param name="suspenseAccountCode">The migration suspense account.</param>
    /// <param name="commit">Write, or only validate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The report.</returns>
    Task<ImportReport> ImportTrialBalanceAsync(long companyId, string csv, DateOnly date, string suspenseAccountCode, bool commit, CancellationToken ct = default);

    /// <summary>The go-live reconciliation: suspense balance and control accounts against sub-ledgers.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="suspenseAccountCode">The migration suspense account.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The reconciliation.</returns>
    Task<Result<OpeningBalanceReconciliation>> ReconcileAsync(long companyId, string suspenseAccountCode, CancellationToken ct = default);
}
