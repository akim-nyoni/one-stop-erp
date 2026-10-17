using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Accounting;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Accounting;

/// <summary>Gathers the Data Health dashboard from the integrity check, the control reconciliation and direct queries.</summary>
public sealed class DataHealthService : IDataHealthService
{
    private const int MaxItems = 20;

    private readonly One4AllDbContext _db;
    private readonly ILedgerIntegrityService _integrity;
    private readonly IGeneralLedgerReports _reports;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="integrity">The ledger integrity check.</param>
    /// <param name="reports">The GL reports (control reconciliation).</param>
    public DataHealthService(One4AllDbContext db, ILedgerIntegrityService integrity, IGeneralLedgerReports reports)
    {
        _db = db;
        _integrity = integrity;
        _reports = reports;
    }

    /// <inheritdoc />
    public async Task<Result<DataHealthReport>> SnapshotAsync(long companyId, DateOnly asAtDate, CancellationToken ct = default)
    {
        Result<LedgerIntegrityReport> checkedLedger = await _integrity.CheckAsync(companyId, ct).ConfigureAwait(false);
        if (checkedLedger.IsFailure)
        {
            return checkedLedger.Error;
        }

        List<HealthIndicator> indicators = [];

        // Unbalanced batches, from the integrity check.
        List<IntegrityIssue> unbalanced = [.. checkedLedger.Value.Issues.Where(i => i.Check == IntegrityCheck.UnbalancedJournal)];
        indicators.Add(Indicator(HealthIndicatorKind.UnbalancedBatches, unbalanced.Count, unbalanced.Sum(i => Math.Abs((i.Expected ?? 0m) - (i.Actual ?? 0m))),
            unbalanced.Select(i => $"{i.Subject}: {i.Detail}")));

        // Control account variances.
        ControlReconciliationReport control = await _reports.ControlReconciliationAsync(companyId, asAtDate, ct).ConfigureAwait(false);
        List<ControlReconciliationRow> variances = [.. control.Rows.Where(r => r.Difference != 0m)];
        indicators.Add(Indicator(HealthIndicatorKind.ControlVariances, variances.Count, variances.Sum(r => Math.Abs(r.Difference)),
            variances.Select(r => $"{r.Control} ({r.AccountCode}): GL {r.GlBalance:N2} vs sub-ledger {r.SubLedgerBalance:N2}")));

        // Negative stock.
        var negative = await (
            from b in _db.StockBalances.AsNoTracking()
            join i in _db.Items.IgnoreQueryFilters([Persistence.One4AllDbContext.SoftDeleteFilter]).AsNoTracking() on b.ItemId equals i.Id
            where b.CompanyId == companyId && b.QuantityOnHand < 0m
            orderby i.Code
            select new { i.Code, b.WarehouseId, b.QuantityOnHand, b.AverageCost }).ToListAsync(ct).ConfigureAwait(false);
        indicators.Add(Indicator(HealthIndicatorKind.NegativeStock, negative.Count, negative.Sum(n => Math.Abs(n.QuantityOnHand * n.AverageCost)),
            negative.Select(n => $"{n.Code} in warehouse {n.WarehouseId}: {n.QuantityOnHand:0.####}")));

        // Unposted batches: drafts (including those awaiting approval) and deferred journals.
        List<JournalEntry> unposted = await _db.Journals.AsNoTracking().Include(j => j.Lines)
            .Where(j => j.CompanyId == companyId && (j.Status == JournalStatus.Draft || j.Status == JournalStatus.Deferred))
            .OrderBy(j => j.PostingDate).ThenBy(j => j.Id).ToListAsync(ct).ConfigureAwait(false);
        indicators.Add(Indicator(HealthIndicatorKind.UnpostedBatches, unposted.Count, unposted.Sum(j => j.TotalBaseDebit),
            unposted.Select(j => $"{(string.IsNullOrEmpty(j.JournalNumber) ? $"#{j.Id}" : j.JournalNumber)} {j.PostingDate:yyyy-MM-dd} {(j.AwaitingApproval ? "awaiting approval" : j.Status.ToString().ToLowerInvariant())}: {j.Narrative}")));

        // Orphans: balances left on deleted traders or items.
        var orphanItems = await (
            from o in _db.OpenItems.AsNoTracking()
            join c in _db.Customers.IgnoreQueryFilters([Persistence.One4AllDbContext.SoftDeleteFilter]).AsNoTracking() on o.TraderId equals c.Id
            where o.CompanyId == companyId && o.Ledger == TradingLedger.AccountsReceivable && o.OutstandingBase != 0m && c.IsDeleted
            select new { Who = "Customer " + c.Code, o.DocumentNumber, o.OutstandingBase }).ToListAsync(ct).ConfigureAwait(false);
        orphanItems.AddRange(await (
            from o in _db.OpenItems.AsNoTracking()
            join s in _db.Suppliers.IgnoreQueryFilters([Persistence.One4AllDbContext.SoftDeleteFilter]).AsNoTracking() on o.TraderId equals s.Id
            where o.CompanyId == companyId && o.Ledger == TradingLedger.AccountsPayable && o.OutstandingBase != 0m && s.IsDeleted
            select new { Who = "Supplier " + s.Code, o.DocumentNumber, o.OutstandingBase }).ToListAsync(ct).ConfigureAwait(false));
        var orphanStock = await (
            from b in _db.StockBalances.AsNoTracking()
            join i in _db.Items.IgnoreQueryFilters([Persistence.One4AllDbContext.SoftDeleteFilter]).AsNoTracking() on b.ItemId equals i.Id
            where b.CompanyId == companyId && b.QuantityOnHand != 0m && i.IsDeleted
            select new { i.Code, b.QuantityOnHand, b.AverageCost }).ToListAsync(ct).ConfigureAwait(false);
        indicators.Add(Indicator(HealthIndicatorKind.OrphanRecords, orphanItems.Count + orphanStock.Count,
            orphanItems.Sum(o => Math.Abs(o.OutstandingBase)) + orphanStock.Sum(s => Math.Abs(s.QuantityOnHand * s.AverageCost)),
            orphanItems.Select(o => $"{o.Who} (deleted) still owes/is owed {o.OutstandingBase:N2} on {o.DocumentNumber}")
                .Concat(orphanStock.Select(s => $"Item {s.Code} (deleted) still holds {s.QuantityOnHand:0.####}"))));

        // Everything else the integrity check found.
        List<IntegrityIssue> others = [.. checkedLedger.Value.Issues.Where(i => i.Check != IntegrityCheck.UnbalancedJournal)];
        indicators.Add(new HealthIndicator(HealthIndicatorKind.LedgerIntegrity,
            DataHealthGrading.Grade(HealthIndicatorKind.LedgerIntegrity, others.Count, others.Any(i => i.Severity == IntegritySeverity.Error)),
            others.Count, 0m, [.. others.Take(MaxItems).Select(i => $"{i.Check} - {i.Subject}: {i.Detail}")]));

        return new DataHealthReport(asAtDate, DataHealthGrading.Overall(indicators.Select(i => i.Status)), indicators);
    }

    private static HealthIndicator Indicator(HealthIndicatorKind kind, int count, decimal value, IEnumerable<string> items)
        => new(kind, DataHealthGrading.Grade(kind, count), count, Math.Round(value, 2, MidpointRounding.AwayFromZero), [.. items.Take(MaxItems)]);
}
