using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Accounting;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Inventory;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Accounting;

/// <summary>
/// Check data: recomputes every derived figure from the records it is derived from and reports disagreements.
/// <list type="bullet">
/// <item>Posted journals must balance in base currency.</item>
/// <item>Each year's journal numbers should run without gaps.</item>
/// <item>Cached GL period balances must equal the sums of the posted (and later reversed) journal lines.</item>
/// <item>Stock quantity on hand must equal the sum of the movements; value may differ only by average-cost rounding,
/// at most a cent per movement.</item>
/// <item>An open item's outstanding amount must equal its original amount less the allocations not reversed — the only
/// path that changes it.</item>
/// </list>
/// </summary>
public sealed class LedgerIntegrityService : ILedgerIntegrityService
{
    private const decimal RoundingPerMovement = 0.01m;

    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public LedgerIntegrityService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result<LedgerIntegrityReport>> CheckAsync(long companyId, CancellationToken ct = default)
    {
        if (!await _db.Companies.AnyAsync(c => c.Id == companyId, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Integrity.CompanyNotFound", $"Company {companyId} not found.");
        }

        List<IntegrityIssue> issues = [];
        int journals = await CheckJournalBalanceAsync(companyId, issues, ct).ConfigureAwait(false);
        await CheckNumberingAsync(companyId, issues, ct).ConfigureAwait(false);
        int periodKeys = await CheckPeriodBalancesAsync(companyId, issues, ct).ConfigureAwait(false);
        int stock = await CheckStockAsync(companyId, issues, ct).ConfigureAwait(false);
        int openItems = await CheckOpenItemsAsync(companyId, issues, ct).ConfigureAwait(false);

        List<IntegrityIssue> ordered = [.. issues.OrderByDescending(i => i.Severity).ThenBy(i => i.Check).ThenBy(i => i.Subject, StringComparer.Ordinal)];
        return new LedgerIntegrityReport(ordered, journals, periodKeys, stock, openItems);
    }

    /// <inheritdoc />
    public async Task<Result<PeriodBalanceRebuildResult>> RebuildPeriodBalancesAsync(long companyId, CancellationToken ct = default)
    {
        if (!await _db.Companies.AnyAsync(c => c.Id == companyId, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Integrity.CompanyNotFound", $"Company {companyId} not found.");
        }

        Dictionary<(long PeriodId, AccountKey Key), LineTotals> expected = await ExpectedPeriodTotalsAsync(companyId, ct).ConfigureAwait(false);
        List<GlPeriodBalance> cached = await _db.PeriodBalances.Where(b => b.CompanyId == companyId).ToListAsync(ct).ConfigureAwait(false);

        int corrected = 0;
        int removed = 0;
        HashSet<(long, AccountKey)> seen = [];
        foreach (GlPeriodBalance balance in cached)
        {
            (long, AccountKey) key = (balance.FiscalPeriodId, balance.Key);
            seen.Add(key);
            if (!expected.TryGetValue(key, out LineTotals totals))
            {
                _db.PeriodBalances.Remove(balance);
                removed++;
                continue;
            }

            if (!totals.Matches(balance))
            {
                balance.Restate(totals.BaseDebit, totals.BaseCredit, totals.ReportingDebit, totals.ReportingCredit);
                corrected++;
            }
        }

        int created = 0;
        foreach (((long periodId, AccountKey key), LineTotals totals) in expected)
        {
            if (seen.Contains((periodId, key)))
            {
                continue;
            }

            GlPeriodBalance balance = new(companyId, totals.FiscalYearId, periodId, key);
            balance.AddMovement(totals.BaseDebit, totals.BaseCredit, totals.ReportingDebit, totals.ReportingCredit);
            _db.PeriodBalances.Add(balance);
            created++;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new PeriodBalanceRebuildResult(corrected, created, removed);
    }

    private IQueryable<JournalEntry> PostedJournals(long companyId)
        => _db.Journals.Where(j => j.CompanyId == companyId && (j.Status == JournalStatus.Posted || j.Status == JournalStatus.Reversed));

    private async Task<int> CheckJournalBalanceAsync(long companyId, List<IntegrityIssue> issues, CancellationToken ct)
    {
        var totals = await (
            from l in _db.Set<JournalLine>()
            join j in PostedJournals(companyId) on l.JournalEntryId equals j.Id
            group l by new { j.Id, j.JournalNumber } into g
            select new { g.Key.JournalNumber, Debit = g.Sum(x => x.BaseDebit), Credit = g.Sum(x => x.BaseCredit) })
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (var t in totals.Where(t => t.Debit != t.Credit))
        {
            issues.Add(new IntegrityIssue(IntegrityCheck.UnbalancedJournal, IntegritySeverity.Error, t.JournalNumber,
                $"Base debits {Money(t.Debit)} and credits {Money(t.Credit)} differ by {Money(t.Debit - t.Credit)}.", t.Credit, t.Debit));
        }

        return totals.Count;
    }

    private async Task CheckNumberingAsync(long companyId, List<IntegrityIssue> issues, CancellationToken ct)
    {
        List<string> numbers = await _db.Journals
            .Where(j => j.CompanyId == companyId && j.JournalNumber != string.Empty)
            .Select(j => j.JournalNumber)
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (NumberGap gap in JournalNumberGaps.Find(numbers))
        {
            string range = JournalNumberGaps.Describe(gap);
            issues.Add(new IntegrityIssue(IntegrityCheck.JournalNumberGap, IntegritySeverity.Warning, range,
                gap.Count == 1 ? $"Journal number {range} is missing." : $"{gap.Count} journal numbers are missing: {range}."));
        }
    }

    private async Task<int> CheckPeriodBalancesAsync(long companyId, List<IntegrityIssue> issues, CancellationToken ct)
    {
        Dictionary<(long PeriodId, AccountKey Key), LineTotals> expected = await ExpectedPeriodTotalsAsync(companyId, ct).ConfigureAwait(false);
        List<GlPeriodBalance> cached = await _db.PeriodBalances.AsNoTracking().Where(b => b.CompanyId == companyId).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, string> periodNames = await PeriodNamesAsync(companyId, ct).ConfigureAwait(false);

        HashSet<(long, AccountKey)> keys = [.. expected.Keys, .. cached.Select(b => (b.FiscalPeriodId, b.Key))];
        Dictionary<(long, AccountKey), GlPeriodBalance> byKey = cached.ToDictionary(b => (b.FiscalPeriodId, b.Key));

        foreach ((long periodId, AccountKey key) in keys)
        {
            LineTotals totals = expected.GetValueOrDefault((periodId, key));
            GlPeriodBalance? balance = byKey.GetValueOrDefault((periodId, key));
            decimal expectedNet = totals.BaseDebit - totals.BaseCredit;
            decimal actualNet = balance is null ? 0m : balance.BaseDebit - balance.BaseCredit;

            bool matches = balance is null ? totals.IsZero : totals.Matches(balance);
            if (matches)
            {
                continue;
            }

            string subject = $"{Describe(key)} — {periodNames.GetValueOrDefault(periodId, periodId.ToString(CultureInfo.InvariantCulture))}";
            string detail = balance is null
                ? $"No cached balance; the posted lines total a net {Money(expectedNet)}."
                : $"Cached balance (Dr {Money(balance.BaseDebit)} / Cr {Money(balance.BaseCredit)}) differs from the posted lines (Dr {Money(totals.BaseDebit)} / Cr {Money(totals.BaseCredit)}). Rebuild the period balances to correct it.";
            issues.Add(new IntegrityIssue(IntegrityCheck.PeriodBalance, IntegritySeverity.Error, subject, detail, expectedNet, actualNet));
        }

        return keys.Count;
    }

    private async Task<int> CheckStockAsync(long companyId, List<IntegrityIssue> issues, CancellationToken ct)
    {
        var balances = await (
            from b in _db.StockBalances
            join i in _db.Items.IgnoreQueryFilters([Persistence.One4AllDbContext.SoftDeleteFilter]) on b.ItemId equals i.Id
            join w in _db.Warehouses.IgnoreQueryFilters([Persistence.One4AllDbContext.SoftDeleteFilter]) on b.WarehouseId equals w.Id
            where b.CompanyId == companyId
            select new { b.ItemId, b.WarehouseId, ItemCode = i.Code, WarehouseCode = w.Code, b.QuantityOnHand, b.TotalValue, i.CostingMethod })
            .ToListAsync(ct).ConfigureAwait(false);

        var movements = await _db.StockMovements
            .Where(m => m.CompanyId == companyId)
            .GroupBy(m => new { m.ItemId, m.WarehouseId })
            .Select(g => new { g.Key.ItemId, g.Key.WarehouseId, Quantity = g.Sum(m => m.Quantity), Value = g.Sum(m => m.Value), Count = g.Count() })
            .ToListAsync(ct).ConfigureAwait(false);
        var byKey = movements.ToDictionary(m => (m.ItemId, m.WarehouseId));

        foreach (var b in balances)
        {
            var m = byKey.GetValueOrDefault((b.ItemId, b.WarehouseId));
            decimal quantity = m?.Quantity ?? 0m;
            decimal value = m?.Value ?? 0m;
            int count = m?.Count ?? 0;
            string subject = $"{b.ItemCode} @ {b.WarehouseCode}";

            if (quantity != b.QuantityOnHand)
            {
                issues.Add(new IntegrityIssue(IntegrityCheck.StockQuantity, IntegritySeverity.Error, subject,
                    $"Quantity on hand {Qty(b.QuantityOnHand)} differs from the movements' {Qty(quantity)}.", quantity, b.QuantityOnHand));
            }

            decimal tolerance = RoundingPerMovement * Math.Max(count, 1);
            if (Math.Abs(value - b.TotalValue) > tolerance)
            {
                issues.Add(new IntegrityIssue(IntegrityCheck.StockValue, IntegritySeverity.Warning, subject,
                    $"Stock value {Money(b.TotalValue)} differs from the movements' {Money(value)} by more than rounding ({Money(tolerance)}).", value, b.TotalValue));
            }
        }

        // A FIFO item's open layers must hold exactly what is on hand (nothing when stock has gone below zero).
        var layers = await _db.Set<StockCostLayer>()
            .Where(l => l.CompanyId == companyId && l.RemainingQuantity > 0m)
            .GroupBy(l => new { l.ItemId, l.WarehouseId })
            .Select(g => new { g.Key.ItemId, g.Key.WarehouseId, Quantity = g.Sum(l => l.RemainingQuantity), Value = g.Sum(l => l.RemainingValue) })
            .ToListAsync(ct).ConfigureAwait(false);
        var layersByKey = layers.ToDictionary(l => (l.ItemId, l.WarehouseId));
        foreach (var b in balances.Where(b => b.CostingMethod == CostingMethod.Fifo))
        {
            var l = layersByKey.GetValueOrDefault((b.ItemId, b.WarehouseId));
            decimal expectedQuantity = Math.Max(0m, b.QuantityOnHand);
            decimal expectedValue = b.QuantityOnHand > 0m ? b.TotalValue : 0m;
            if ((l?.Quantity ?? 0m) != expectedQuantity || Math.Abs((l?.Value ?? 0m) - expectedValue) > RoundingPerMovement)
            {
                issues.Add(new IntegrityIssue(IntegrityCheck.CostLayers, IntegritySeverity.Error, $"{b.ItemCode} @ {b.WarehouseCode}",
                    $"FIFO layers hold {Qty(l?.Quantity ?? 0m)} worth {Money(l?.Value ?? 0m)}, not the {Qty(expectedQuantity)} worth {Money(expectedValue)} on hand.", expectedValue, l?.Value ?? 0m));
            }
        }

        // Movements with no balance row at all.
        HashSet<(long, long)> withBalance = [.. balances.Select(b => (b.ItemId, b.WarehouseId))];
        foreach (var m in movements.Where(m => !withBalance.Contains((m.ItemId, m.WarehouseId)) && m.Quantity != 0m))
        {
            issues.Add(new IntegrityIssue(IntegrityCheck.StockQuantity, IntegritySeverity.Error,
                $"item {m.ItemId} @ warehouse {m.WarehouseId}",
                $"Movements total {Qty(m.Quantity)} but there is no stock balance.", m.Quantity, 0m));
        }

        return balances.Count;
    }

    private async Task<int> CheckOpenItemsAsync(long companyId, List<IntegrityIssue> issues, CancellationToken ct)
    {
        var items = await _db.OpenItems
            .Where(i => i.CompanyId == companyId)
            .Select(i => new { i.Id, i.Ledger, i.DocumentNumber, i.OriginalBase, i.OutstandingBase })
            .ToListAsync(ct).ConfigureAwait(false);

        var allocations = await _db.Allocations
            .Where(a => a.CompanyId == companyId && !a.IsReversed)
            .Select(a => new { a.DebitItemId, a.CreditItemId, a.DebitBase, a.CreditBase })
            .ToListAsync(ct).ConfigureAwait(false);

        Dictionary<long, decimal> drawn = [];
        foreach (var a in allocations)
        {
            drawn[a.DebitItemId] = drawn.GetValueOrDefault(a.DebitItemId) + a.DebitBase;
            drawn[a.CreditItemId] = drawn.GetValueOrDefault(a.CreditItemId) + a.CreditBase;
        }

        foreach (var item in items)
        {
            decimal expected = decimal.Round(item.OriginalBase - drawn.GetValueOrDefault(item.Id), 4, MidpointRounding.ToEven);
            if (expected != item.OutstandingBase)
            {
                issues.Add(new IntegrityIssue(IntegrityCheck.OpenItemOutstanding, IntegritySeverity.Error, $"{item.Ledger} {item.DocumentNumber}",
                    $"Outstanding {Money(item.OutstandingBase)} differs from the original {Money(item.OriginalBase)} less allocations ({Money(expected)}).",
                    expected, item.OutstandingBase));
            }
        }

        return items.Count;
    }

    private async Task<Dictionary<(long PeriodId, AccountKey Key), LineTotals>> ExpectedPeriodTotalsAsync(long companyId, CancellationToken ct)
    {
        var rows = await (
            from l in _db.Set<JournalLine>()
            join j in PostedJournals(companyId) on l.JournalEntryId equals j.Id
            group l by new { j.FiscalYearId, j.FiscalPeriodId, l.AccountCode, l.CostCentre, l.Department } into g
            select new
            {
                g.Key.FiscalYearId,
                g.Key.FiscalPeriodId,
                g.Key.AccountCode,
                g.Key.CostCentre,
                g.Key.Department,
                BaseDebit = g.Sum(x => x.BaseDebit),
                BaseCredit = g.Sum(x => x.BaseCredit),
                ReportingDebit = g.Sum(x => x.ReportingDebit),
                ReportingCredit = g.Sum(x => x.ReportingCredit),
            })
            .ToListAsync(ct).ConfigureAwait(false);

        return rows.ToDictionary(
            r => (r.FiscalPeriodId, new AccountKey(r.AccountCode, r.CostCentre, r.Department)),
            r => new LineTotals(r.FiscalYearId, r.BaseDebit, r.BaseCredit, r.ReportingDebit, r.ReportingCredit));
    }

    private async Task<Dictionary<long, string>> PeriodNamesAsync(long companyId, CancellationToken ct)
        => await _db.FiscalYears
            .Where(y => y.CompanyId == companyId)
            .SelectMany(y => y.Periods.Select(p => new { p.Id, y.Code, p.PeriodNumber, p.Name }))
            .ToDictionaryAsync(p => p.Id, p => $"{p.Code} P{p.PeriodNumber:00} ({p.Name})", ct).ConfigureAwait(false);

    private static string Describe(AccountKey key)
    {
        string text = key.AccountCode;
        if (key.CostCentre.Length > 0)
        {
            text += $" cc {key.CostCentre}";
        }

        if (key.Department.Length > 0)
        {
            text += $" dept {key.Department}";
        }

        return text;
    }

    private static string Money(decimal amount) => amount.ToString("#,##0.00", CultureInfo.InvariantCulture);

    private static string Qty(decimal quantity) => quantity.ToString("#,##0.####", CultureInfo.InvariantCulture);

    /// <summary>Line totals for one period and account key.</summary>
    private readonly record struct LineTotals(long FiscalYearId, decimal BaseDebit, decimal BaseCredit, decimal ReportingDebit, decimal ReportingCredit)
    {
        public bool IsZero => BaseDebit == 0m && BaseCredit == 0m && ReportingDebit == 0m && ReportingCredit == 0m;

        public bool Matches(GlPeriodBalance balance)
            => balance.BaseDebit == BaseDebit && balance.BaseCredit == BaseCredit
               && balance.ReportingDebit == ReportingDebit && balance.ReportingCredit == ReportingCredit;
    }
}
