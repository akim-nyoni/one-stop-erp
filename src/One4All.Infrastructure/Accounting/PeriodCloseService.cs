using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Banking;
using One4All.Core.Common;
using One4All.Core.Inventory;
using One4All.Core.Offline;
using One4All.Core.Procurement;
using One4All.Core.Trading;
using One4All.Core.Workflow;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Accounting;

/// <summary>
/// The period-close checklist: counts, as at a period end, everything a controller should clear before
/// closing — unposted journals, unsynced offline operations, unreconciled statement lines, open
/// stocktakes, goods received not invoiced, approvals in progress and foreign-currency balances not yet revalued.
/// Reads only.
/// </summary>
public sealed class PeriodCloseService : IPeriodCloseService
{
    private readonly One4AllDbContext _db;
    private readonly ITradingRevaluationService _revaluation;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="revaluation">The trading revaluation service (previewed, never posted, by the checklist).</param>
    public PeriodCloseService(One4AllDbContext db, ITradingRevaluationService revaluation)
    {
        _db = db;
        _revaluation = revaluation;
    }

    /// <inheritdoc />
    public async Task<PeriodCloseChecklist> ChecklistAsync(long companyId, DateOnly periodEnd, CancellationToken ct = default)
    {
        List<PeriodCloseCheck> checks = [];

        int unposted = await _db.Journals
            .CountAsync(j => j.CompanyId == companyId && j.PostingDate <= periodEnd && (j.Status == JournalStatus.Draft || j.Status == JournalStatus.Deferred), ct)
            .ConfigureAwait(false);
        checks.Add(new PeriodCloseCheck("UnpostedJournals", "Unposted journals", unposted, 0m, true,
            "Draft or deferred journals dated in or before the period must be posted or deleted."));

        int unsynced = await _db.OfflineOperations
            .CountAsync(o => o.CompanyId == companyId && (o.Status == OfflineOperationStatus.Pending || o.Status == OfflineOperationStatus.Failed), ct)
            .ConfigureAwait(false);
        checks.Add(new PeriodCloseCheck("UnsyncedOffline", "Unsynced offline operations", unsynced, 0m, true,
            "Offline POS sales still pending or failed must be synced, requeued or discarded."));

        int unreconciled = await _db.Set<BankStatementLine>()
            .CountAsync(l => l.CompanyId == companyId && l.StatementDate <= periodEnd && !l.IsReconciled, ct)
            .ConfigureAwait(false);
        checks.Add(new PeriodCloseCheck("UnreconciledBank", "Unreconciled bank statement lines", unreconciled, 0m, true,
            "Statement lines dated in or before the period that are not matched to the cash book."));

        int stocktakes = await _db.Stocktakes
            .CountAsync(s => s.CompanyId == companyId && s.StocktakeDate <= periodEnd && s.Status == StocktakeStatus.Draft, ct)
            .ConfigureAwait(false);
        checks.Add(new PeriodCloseCheck("OpenStocktakes", "Open stocktakes", stocktakes, 0m, false,
            "Stocktake sessions started in or before the period that have not been posted."));

        var grni = await (
            from po in _db.Set<PurchaseOrder>()
            from line in po.Lines
            where po.CompanyId == companyId && po.Status != PurchaseOrderStatus.Cancelled && line.ReceivedQuantity > line.InvoicedQuantity
            select new { Qty = line.ReceivedQuantity - line.InvoicedQuantity, line.UnitPrice })
            .ToListAsync(ct).ConfigureAwait(false);
        checks.Add(new PeriodCloseCheck("Grni", "Goods received not invoiced", grni.Count, grni.Sum(g => decimal.Round(g.Qty * g.UnitPrice, 2, MidpointRounding.ToEven)), false,
            "Purchase order lines received but not yet matched to a supplier invoice (the GRNI accrual)."));

        int approvals = await _db.WorkflowInstances
            .CountAsync(i => i.CompanyId == companyId && i.Status == WorkflowStatus.InProgress, ct)
            .ConfigureAwait(false);
        checks.Add(new PeriodCloseCheck("ApprovalsInProgress", "Approvals in progress", approvals, 0m, false,
            "Workflow instances still awaiting approval."));

        // Foreign-currency debtors and creditors should carry the period-end revaluation. A preview says what a run
        // would post; anything unbooked (or no closing rate to value at) is a warning.
        int unrevalued = 0;
        decimal unbooked = 0m;
        List<string> notes = [];
        foreach (TradingLedger ledger in new[] { TradingLedger.AccountsReceivable, TradingLedger.AccountsPayable })
        {
            Result<TradingRevaluationResult> preview = await _revaluation.PreviewAsync(companyId, ledger, periodEnd, ct).ConfigureAwait(false);
            string name = ledger == TradingLedger.AccountsReceivable ? "AR" : "AP";
            if (preview.IsFailure)
            {
                if (preview.Error.Code != "Reval.AccountsMissing")
                {
                    unrevalued++;
                    notes.Add($"{name}: {preview.Error.Message}");
                }

                continue;
            }

            if (preview.Value.Posted != 0m)
            {
                unrevalued += preview.Value.Items.Count;
                unbooked += Math.Abs(preview.Value.Posted);
                notes.Add($"{name}: {preview.Value.Posted:N2} unbooked");
            }
        }

        checks.Add(new PeriodCloseCheck("ForeignRevaluation", "Foreign balances not revalued", unrevalued, unbooked, false,
            notes.Count == 0
                ? "Foreign-currency debtors and creditors carry the period-end revaluation at the closing rate."
                : $"Run the foreign-currency revaluation at the period end ({string.Join("; ", notes)})."));

        return new PeriodCloseChecklist(periodEnd, checks);
    }
}
