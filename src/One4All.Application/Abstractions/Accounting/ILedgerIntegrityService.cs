using One4All.Core.Common;

namespace One4All.Application.Abstractions.Accounting;

/// <summary>What a data-integrity check looked at.</summary>
public enum IntegrityCheck
{
    /// <summary>A posted journal whose base debits and credits differ.</summary>
    UnbalancedJournal = 1,

    /// <summary>Numbers missing from a year's journal numbering.</summary>
    JournalNumberGap = 2,

    /// <summary>A cached GL period balance that differs from its posted journal lines.</summary>
    PeriodBalance = 3,

    /// <summary>A stock balance whose quantity on hand differs from its movement history.</summary>
    StockQuantity = 4,

    /// <summary>A stock balance whose value differs from its movement history by more than rounding.</summary>
    StockValue = 5,

    /// <summary>An open item whose outstanding amount differs from its original amount less its allocations.</summary>
    OpenItemOutstanding = 6,

    /// <summary>A FIFO item whose cost layers do not add up to its stock balance.</summary>
    CostLayers = 7,
}

/// <summary>How serious an integrity finding is.</summary>
public enum IntegritySeverity
{
    /// <summary>Worth reviewing (e.g. a numbering gap, rounding beyond tolerance).</summary>
    Warning = 1,

    /// <summary>The data disagrees with itself and must be corrected.</summary>
    Error = 2,
}

/// <summary>One integrity finding.</summary>
/// <param name="Check">The check that found it.</param>
/// <param name="Severity">How serious it is.</param>
/// <param name="Subject">What it concerns (journal number, account and period, item and warehouse, document).</param>
/// <param name="Detail">A readable explanation.</param>
/// <param name="Expected">The value recomputed from the underlying records, where there is one.</param>
/// <param name="Actual">The value stored, where there is one.</param>
public sealed record IntegrityIssue(IntegrityCheck Check, IntegritySeverity Severity, string Subject, string Detail, decimal? Expected = null, decimal? Actual = null);

/// <summary>The result of a company's data-integrity check.</summary>
/// <param name="Issues">The findings, errors first.</param>
/// <param name="JournalsChecked">Posted journals checked for balance.</param>
/// <param name="PeriodBalancesChecked">Period-balance keys compared.</param>
/// <param name="StockBalancesChecked">Stock balances compared.</param>
/// <param name="OpenItemsChecked">Open items compared.</param>
public sealed record LedgerIntegrityReport(IReadOnlyList<IntegrityIssue> Issues, int JournalsChecked, int PeriodBalancesChecked, int StockBalancesChecked, int OpenItemsChecked)
{
    /// <summary>True when nothing was found.</summary>
    public bool IsClean => Issues.Count == 0;

    /// <summary>The number of errors.</summary>
    public int ErrorCount => Issues.Count(i => i.Severity == IntegritySeverity.Error);

    /// <summary>The number of warnings.</summary>
    public int WarningCount => Issues.Count(i => i.Severity == IntegritySeverity.Warning);
}

/// <summary>The result of rebuilding the cached GL period balances.</summary>
/// <param name="Corrected">Existing rows restated to the journal totals.</param>
/// <param name="Created">Rows created for journal totals that had none.</param>
/// <param name="Removed">Rows removed because no posted line supports them.</param>
public readonly record struct PeriodBalanceRebuildResult(int Corrected, int Created, int Removed)
{
    /// <summary>True when the cache already matched.</summary>
    public bool NothingToDo => Corrected == 0 && Created == 0 && Removed == 0;
}

/// <summary>
/// "Check data" for a company: verifies that the ledgers agree with themselves. The posted journal lines are the
/// record; everything else checked here — cached GL period balances, stock balances, open-item outstanding amounts —
/// is derived from records and must match them. Also reports unbalanced posted journals and gaps in journal numbering.
/// </summary>
public interface ILedgerIntegrityService
{
    /// <summary>Runs every check for a company. Read-only.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The findings.</returns>
    Task<Result<LedgerIntegrityReport>> CheckAsync(long companyId, CancellationToken ct = default);

    /// <summary>
    /// Rebuilds the company's cached GL period balances from its posted journal lines — the one repair that is always
    /// safe, because the balances are purely derived. Stock and open-item differences are reported, not repaired: they
    /// need investigating.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What was changed.</returns>
    Task<Result<PeriodBalanceRebuildResult>> RebuildPeriodBalancesAsync(long companyId, CancellationToken ct = default);
}
