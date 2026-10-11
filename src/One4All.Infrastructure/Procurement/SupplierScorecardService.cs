using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Procurement;
using One4All.Application.Procurement;
using One4All.Core.Inventory;
using One4All.Core.Procurement;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Procurement;

/// <summary>The supplier scorecard over goods received, supplier returns and AP invoices.</summary>
public sealed class SupplierScorecardService : ISupplierScorecardService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public SupplierScorecardService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<IReadOnlyList<SupplierScore>> ScorecardAsync(long companyId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        DateOnly first = from;
        DateOnly last = to;
        var deliveries = await (
            from grn in _db.Set<GoodsReceivedNote>()
            join po in _db.Set<PurchaseOrder>() on grn.PurchaseOrderId equals po.Id
            where grn.CompanyId == companyId && grn.Date >= first && grn.Date <= last
            select new { grn.SupplierId, grn.Date, po.RequiredBy, Quantity = grn.Lines.Sum(l => l.Quantity) })
            .ToListAsync(ct).ConfigureAwait(false);

        // A return to supplier posts a stock movement and an AP credit note on the same journal: that names the supplier.
        var returns = await (
            from m in _db.StockMovements
            join note in _db.OpenItems on m.JournalEntryId equals note.JournalEntryId
            where m.CompanyId == companyId && m.Type == StockMovementType.SupplierReturn && m.Date >= first && m.Date <= last
                  && note.Ledger == TradingLedger.AccountsPayable && note.ItemType == OpenItemType.CreditNote
            group m by note.TraderId into g
            select new { SupplierId = g.Key, Quantity = -g.Sum(m => m.Quantity) })
            .ToDictionaryAsync(r => r.SupplierId, r => r.Quantity, ct).ConfigureAwait(false);

        Dictionary<long, decimal> spend = await _db.OpenItems
            .Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsPayable && i.ItemType == OpenItemType.Invoice && i.DocumentDate >= first && i.DocumentDate <= last)
            .GroupBy(i => i.TraderId)
            .Select(g => new { SupplierId = g.Key, Amount = g.Sum(i => i.OriginalBase) })
            .ToDictionaryAsync(s => s.SupplierId, s => s.Amount, ct).ConfigureAwait(false);

        // Purchase price variance: invoices matched to orders in the period, at invoice prices against order prices.
        var matched = await _db.Set<InvoicedOrderLine>().AsNoTracking()
            .Where(l => l.CompanyId == companyId && l.Date >= first && l.Date <= last)
            .GroupBy(l => l.SupplierId)
            .Select(g => new { SupplierId = g.Key, AtOrder = g.Sum(l => l.Quantity * l.OrderPrice), AtInvoice = g.Sum(l => l.Quantity * l.InvoicePrice) })
            .ToDictionaryAsync(m => m.SupplierId, ct).ConfigureAwait(false);

        HashSet<long> active = [.. deliveries.Select(d => d.SupplierId), .. returns.Keys, .. spend.Keys, .. matched.Keys];
        var suppliers = await _db.Suppliers.AsNoTracking()
            .Where(s => s.CompanyId == companyId && active.Contains(s.Id))
            .Select(s => new { s.Id, s.Code, s.Name })
            .ToListAsync(ct).ConfigureAwait(false);

        List<SupplierScore> scores = [];
        foreach (var supplier in suppliers.OrderBy(s => s.Code, StringComparer.Ordinal))
        {
            var mine = deliveries.Where(d => d.SupplierId == supplier.Id).ToList();
            List<bool> judged = [.. mine.Select(d => SupplierScoring.OnTime(d.Date, d.RequiredBy)).OfType<bool>()];
            decimal received = mine.Sum(d => d.Quantity);
            decimal returned = returns.GetValueOrDefault(supplier.Id);
            scores.Add(new SupplierScore(
                supplier.Id, supplier.Code, supplier.Name, mine.Count, judged.Count, judged.Count(j => j),
                received, returned, spend.GetValueOrDefault(supplier.Id),
                SupplierScoring.Rate(judged.Count(j => j), judged.Count),
                SupplierScoring.Rate(received - returned, received),
                matched.TryGetValue(supplier.Id, out var m) ? decimal.Round(m.AtInvoice - m.AtOrder, 2, MidpointRounding.AwayFromZero) : 0m,
                matched.TryGetValue(supplier.Id, out var n) ? SupplierScoring.Rate(n.AtInvoice - n.AtOrder, n.AtOrder) : null));
        }

        return scores;
    }
}
