using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Banking;
using One4All.Application.Banking;
using One4All.Core.Accounting;
using One4All.Core.Banking;
using One4All.Core.Payroll;
using One4All.Core.Procurement;
using One4All.Core.SalesOrders;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Banking;

/// <summary>The full cash-flow forecast from every source the system knows (brief §4.4).</summary>
public sealed class CashForecastService : ICashForecastService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public CashForecastService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<CashForecast> ForecastAsync(long companyId, DateOnly from, int weeks, decimal minimumBalance, CancellationToken ct = default)
    {
        weeks = Math.Clamp(weeks, 1, 52);
        DateOnly end = from.AddDays(weeks * 7);
        List<CashFlowLine> lines = [];

        // Opening: the bank accounts' ledger balances at the end of the day before.
        List<string> bankAccounts = await _db.BankAccounts.AsNoTracking().Where(b => b.CompanyId == companyId).Select(b => b.GlAccountCode).Distinct().ToListAsync(ct).ConfigureAwait(false);
        decimal opening = await _db.Journals.AsNoTracking().Where(j => j.CompanyId == companyId && j.Status != JournalStatus.Draft && j.PostingDate < from)
            .Where(j => j.Status == JournalStatus.Posted || j.Status == JournalStatus.Reversed)
            .SelectMany(j => j.Lines).Where(l => bankAccounts.Contains(l.AccountCode)).SumAsync(l => (decimal?)(l.BaseDebit - l.BaseCredit), ct).ConfigureAwait(false) ?? 0m;

        // Saved payment runs: paid on their payment date; their suppliers' invoices due by then are not counted again.
        var runs = await _db.Set<PaymentRunBatch>().AsNoTracking().Include(r => r.Lines).Where(r => r.CompanyId == companyId && r.Status == PaymentRunStatus.Proposed)
            .ToListAsync(ct).ConfigureAwait(false);
        HashSet<(long Supplier, DateOnly DueBy)> inRuns = [];
        foreach (PaymentRunBatch run in runs)
        {
            foreach (PaymentRunBatchLine line in run.Lines.Where(l => l.Included && l.Amount > 0m))
            {
                lines.Add(new CashFlowLine(run.PaymentDate, CashFlowSource.PaymentRuns, $"{run.Number} {line.SupplierCode}", -line.Amount));
                inRuns.Add((line.SupplierId, run.DueOnOrBefore));
            }
        }

        // Receivables and payables by due date.
        var open = await _db.OpenItems.AsNoTracking().Where(i => i.CompanyId == companyId && i.OutstandingBase != 0m && i.DueDate < end
                && (i.Ledger == TradingLedger.AccountsReceivable || i.Ledger == TradingLedger.AccountsPayable))
            .Select(i => new { i.Ledger, i.TraderId, i.DocumentNumber, i.DueDate, i.Sign, i.OutstandingBase }).ToListAsync(ct).ConfigureAwait(false);
        foreach (var item in open)
        {
            decimal signed = item.Sign == LedgerSign.Debit ? item.OutstandingBase : -item.OutstandingBase;
            if (item.Ledger == TradingLedger.AccountsReceivable)
            {
                lines.Add(new CashFlowLine(item.DueDate, CashFlowSource.Receivables, item.DocumentNumber, signed));
            }
            else if (!inRuns.Any(r => r.Supplier == item.TraderId && item.DueDate <= r.DueBy))
            {
                lines.Add(new CashFlowLine(item.DueDate, CashFlowSource.Payables, item.DocumentNumber, signed));
            }
        }

        // Open sales orders: what is not yet invoiced, on delivery plus the customer's terms.
        Dictionary<long, int> customerTerms = await _db.Customers.AsNoTracking().Where(c => c.CompanyId == companyId).ToDictionaryAsync(c => c.Id, c => c.PaymentTermsDays, ct).ConfigureAwait(false);
        var salesOrders = await _db.SalesOrders.AsNoTracking().Include(o => o.Lines)
            .Where(o => o.CompanyId == companyId && o.Status != SalesOrderStatus.Invoiced && o.Status != SalesOrderStatus.Cancelled).ToListAsync(ct).ConfigureAwait(false);
        foreach (SalesOrder order in salesOrders)
        {
            decimal value = order.Lines.Sum(l => Math.Max(0m, l.OrderedQuantity - l.InvoicedQuantity) * l.UnitPrice);
            DateOnly due = (order.RequestedDeliveryDate ?? order.OrderDate).AddDays(customerTerms.GetValueOrDefault(order.CustomerId));
            lines.Add(new CashFlowLine(due, CashFlowSource.SalesOrders, order.OrderNumber, decimal.Round(value, 2, MidpointRounding.AwayFromZero)));
        }

        // Open purchase orders: what is not yet invoiced, on the required date plus the supplier's terms.
        Dictionary<long, int> supplierTerms = await _db.Suppliers.AsNoTracking().Where(s => s.CompanyId == companyId).ToDictionaryAsync(s => s.Id, s => s.PaymentTermsDays, ct).ConfigureAwait(false);
        var purchaseOrders = await _db.PurchaseOrders.AsNoTracking().Include(o => o.Lines)
            .Where(o => o.CompanyId == companyId && o.Status != PurchaseOrderStatus.Cancelled && o.Status != PurchaseOrderStatus.Closed).ToListAsync(ct).ConfigureAwait(false);
        foreach (PurchaseOrder order in purchaseOrders)
        {
            decimal value = order.Lines.Sum(l => Math.Max(0m, l.OrderedQuantity - l.InvoicedQuantity) * l.UnitPrice);
            DateOnly due = (order.RequiredBy ?? order.OrderDate).AddDays(supplierTerms.GetValueOrDefault(order.SupplierId));
            lines.Add(new CashFlowLine(due, CashFlowSource.PurchaseOrders, order.OrderNumber, -decimal.Round(value, 2, MidpointRounding.AwayFromZero)));
        }

        // Standing orders paid from the bank.
        List<StandingOrder> standing = await _db.Set<StandingOrder>().AsNoTracking().Where(s => s.CompanyId == companyId && s.IsActive).ToListAsync(ct).ConfigureAwait(false);
        foreach (StandingOrder order in standing)
        {
            foreach (DateOnly date in Recurrence.Due(order.StartDate, order.EndDate, order.Frequency, order.LastRunOn ?? from.AddDays(-1), end.AddDays(-1)))
            {
                lines.Add(new CashFlowLine(date, CashFlowSource.StandingOrders, order.Reference, -order.Amount));
            }
        }

        // Payroll: the last regular run repeated monthly — net pay on its pay day, deductions remitted ten days later.
        DateOnly? lastPay = await _db.Payslips.AsNoTracking().Where(p => p.CompanyId == companyId && p.RunKind == PayRunKind.Regular).MaxAsync(p => (DateOnly?)p.PayDate, ct).ConfigureAwait(false);
        if (lastPay is DateOnly pay)
        {
            var run = await _db.Payslips.AsNoTracking().Where(p => p.CompanyId == companyId && p.RunKind == PayRunKind.Regular && p.PayDate == pay)
                .GroupBy(p => 1).Select(g => new { Net = g.Sum(p => p.NetPay), Deductions = g.Sum(p => p.TotalDeductions) }).FirstAsync(ct).ConfigureAwait(false);
            for (int m = 1; m <= 13; m++)
            {
                DateOnly payDay = pay.AddMonths(m);
                lines.Add(new CashFlowLine(payDay, CashFlowSource.Payroll, $"Net pay {payDay:yyyy-MM}", -run.Net));
                lines.Add(new CashFlowLine(payDay.AddDays(10), CashFlowSource.Payroll, $"Payroll deductions {payDay:yyyy-MM}", -run.Deductions));
            }
        }

        // Flows keyed in by hand.
        foreach (CashForecastEntry entry in await _db.Set<CashForecastEntry>().AsNoTracking().Where(e => e.CompanyId == companyId && e.Date < end).ToListAsync(ct).ConfigureAwait(false))
        {
            lines.AddRange(entry.Dates().Where(d => d >= from && d < end).Select(d => new CashFlowLine(d, CashFlowSource.Manual, entry.Description, entry.Amount)));
        }

        return CashForecastBuilder.Build(from, weeks, opening, minimumBalance, lines);
    }

    /// <inheritdoc />
    public async Task<Core.Common.Result<long>> AddEntryAsync(long companyId, DateOnly date, decimal amount, string description, DateOnly? repeatMonthlyUntil = null, CancellationToken ct = default)
    {
        if (amount == 0m || string.IsNullOrWhiteSpace(description) || repeatMonthlyUntil < date)
        {
            return Core.Common.Error.Validation("Cash.Entry", "A forecast entry needs an amount, a description and a repeat that ends after it starts.");
        }

        CashForecastEntry entry = new(companyId, date, amount, description.Trim()) { RepeatMonthlyUntil = repeatMonthlyUntil };
        _db.Set<CashForecastEntry>().Add(entry);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return entry.Id;
    }

    /// <inheritdoc />
    public async Task<Core.Common.Result> RemoveEntryAsync(long companyId, long entryId, CancellationToken ct = default)
    {
        CashForecastEntry? entry = await _db.Set<CashForecastEntry>().FirstOrDefaultAsync(e => e.Id == entryId && e.CompanyId == companyId, ct).ConfigureAwait(false);
        if (entry is null)
        {
            return Core.Common.Result.Failure(Core.Common.Error.NotFound("Cash.EntryNotFound", $"Forecast entry {entryId} not found."));
        }

        _db.Set<CashForecastEntry>().Remove(entry);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Core.Common.Result.Success();
    }
}
