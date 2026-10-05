using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Housing;
using One4All.Core.Accounting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Trading;

namespace One4All.Infrastructure.Reporting;

/// <summary>Serves the AR-numbered receivables reports (brief §14 B): every data command whose name starts with <c>Recv</c>.</summary>
public sealed class ReceivablesReportDataProvider : IReportDataProvider
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the provider.</summary>
    /// <param name="db">The database context.</param>
    public ReceivablesReportDataProvider(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public bool CanHandle(string command) => command.StartsWith("Recv", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetRowsAsync(long companyId, string command, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct = default)
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        DateOnly asAt = DateParameter(parameters, "AsAtDate") ?? today;
        DateOnly to = DateParameter(parameters, "ToDate") ?? today;
        DateOnly from = DateParameter(parameters, "FromDate") ?? new DateOnly(to.Year, 1, 1);
        return command.ToUpperInvariant() switch
        {
            "RECVCOLLECTIONS" => await CollectionsAsync(companyId, from, to, ct).ConfigureAwait(false),
            "RECVAGEDDETAIL" => await AgedDetailAsync(companyId, asAt, ct).ConfigureAwait(false),
            "RECVAGEDBYREP" => await AgedByRepAsync(companyId, asAt, ct).ConfigureAwait(false),
            "RECVLEDGER" => await LedgerAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RECVDAYBOOK" => await DayBookAsync(companyId, from, to, ct).ConfigureAwait(false),
            "RECVSALESANALYSIS" => await SalesAnalysisAsync(companyId, from, to, ct).ConfigureAwait(false),
            "RECVSALESTREND" => await SalesTrendAsync(companyId, asAt, ct).ConfigureAwait(false),
            "RECVTOPCUSTOMERS" => await TopCustomersAsync(companyId, from, to, ct).ConfigureAwait(false),
            "RECVUNALLOCATED" => await UnallocatedAsync(companyId, asAt, ct).ConfigureAwait(false),
            "RECVDSO" => await DsoAsync(companyId, asAt, ct).ConfigureAwait(false),
            "RECVPROMISES" => await PromisesAsync(companyId, asAt, ct).ConfigureAwait(false),
            "RECVWRITEOFFS" => await WriteOffsAsync(companyId, from, to, ct).ConfigureAwait(false),
            "RECVLAYBYS" => await LaybysAsync(companyId, asAt, ct).ConfigureAwait(false),
            "RECVEXPOSURE" => await ExposureAsync(companyId, asAt, ct).ConfigureAwait(false),
            "RECVNEWDORMANT" => await NewDormantAsync(companyId, from, to, ct).ConfigureAwait(false),
            _ => [],
        };
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> AgedDetailAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        Dictionary<long, Customer> customers = await CustomersAsync(companyId, ct).ConfigureAwait(false);
        List<OpenItemAsAt> items = await OpenItemsAsAt.LoadAsync(_db, companyId, TradingLedger.AccountsReceivable, asAt, ct).ConfigureAwait(false);
        return [.. items.Where(i => customers.ContainsKey(i.Item.TraderId)).OrderBy(i => customers[i.Item.TraderId].Name, StringComparer.Ordinal).ThenBy(i => i.Item.DocumentDate)
            .Select(i =>
            {
                DateOnly basis = customers[i.Item.TraderId].AgeingBasis == AgeingBasis.InvoiceDate ? i.Item.DocumentDate : i.Item.DueDate;
                int band = DebtorAgeing.Band(basis, asAt);
                return Row(("Customer", customers[i.Item.TraderId].Name), ("Document", i.Item.DocumentNumber), ("Type", i.Item.ItemType.ToString()), ("Date", Date(i.Item.DocumentDate)),
                    ("Due", Date(i.Item.DueDate)), ("Days", Math.Max(0, asAt.DayNumber - basis.DayNumber)), ("Band", DebtorAgeing.Bands[band]), ("Outstanding", i.Signed(LedgerSign.Debit)));
            })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> AgedByRepAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        Dictionary<long, Customer> customers = await CustomersAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<long, string> reps = await _db.Set<SalesRep>().AsNoTracking().Where(r => r.CompanyId == companyId).ToDictionaryAsync(r => r.Id, r => r.Name, ct).ConfigureAwait(false);
        string RepOf(Customer c) => c.SalesRepId is long id ? reps.GetValueOrDefault(id, "?") : c.SalesAgent ?? "(none)";
        List<OpenItemAsAt> items = [.. (await OpenItemsAsAt.LoadAsync(_db, companyId, TradingLedger.AccountsReceivable, asAt, ct).ConfigureAwait(false)).Where(i => customers.ContainsKey(i.Item.TraderId))];
        return [.. items.GroupBy(i => (Rep: RepOf(customers[i.Item.TraderId]), Currency: i.Item.Currency.Value))
            .OrderBy(g => g.Key.Rep, StringComparer.Ordinal).ThenBy(g => g.Key.Currency, StringComparer.Ordinal)
            .Select(g =>
            {
                decimal[] bands = new decimal[DebtorAgeing.Bands.Count];
                foreach (OpenItemAsAt item in g)
                {
                    decimal signed = item.Signed(LedgerSign.Debit);
                    DateOnly basis = customers[item.Item.TraderId].AgeingBasis == AgeingBasis.InvoiceDate ? item.Item.DocumentDate : item.Item.DueDate;
                    bands[signed < 0m ? 0 : DebtorAgeing.Band(basis, asAt)] += signed;
                }

                return Row(("Rep", g.Key.Rep), ("Currency", g.Key.Currency), ("Customers", g.Select(i => i.Item.TraderId).Distinct().Count()), ("Current", Money(bands[0])), ("D30", Money(bands[1])),
                    ("D60", Money(bands[2])), ("D90", Money(bands[3])), ("D120", Money(bands[4])), ("Over120", Money(bands[5])), ("Total", bands.Sum()));
            })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> LedgerAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        string? code = parameters.TryGetValue("CustomerCode", out object? value) && value is string text && text.Trim().Length > 0 ? text.Trim() : null;
        Customer? customer = code is null ? null : await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.CompanyId == companyId && c.Code == code, ct).ConfigureAwait(false);
        if (customer is null)
        {
            return [];
        }

        List<OpenItem> items = await _db.OpenItems.AsNoTracking().Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable && i.TraderId == customer.Id)
            .OrderBy(i => i.DocumentDate).ThenBy(i => i.Id).ToListAsync(ct).ConfigureAwait(false);
        decimal balance = 0m;
        List<IReadOnlyDictionary<string, object?>> rows = [];
        foreach (OpenItem item in items)
        {
            bool debit = item.Sign == LedgerSign.Debit;
            balance += debit ? item.OriginalBase : -item.OriginalBase;
            rows.Add(Row(("Date", Date(item.DocumentDate)), ("Type", item.ItemType.ToString()), ("Document", item.DocumentNumber), ("Due", Date(item.DueDate)),
                ("Debit", debit ? Money(item.OriginalBase) : string.Empty), ("Credit", debit ? string.Empty : Money(item.OriginalBase)), ("Balance", Money(balance)),
                ("Outstanding", debit ? item.OutstandingBase : -item.OutstandingBase)));
        }

        return rows;
    }

    // Each receipt's journal gives the bank account debited (the method) and who posted it.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> CollectionsAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        var receipts = await (from o in _db.OpenItems.AsNoTracking()
                              join j in _db.Journals.AsNoTracking() on o.JournalEntryId equals j.Id
                              where o.CompanyId == companyId && o.Ledger == TradingLedger.AccountsReceivable && o.ItemType == OpenItemType.Receipt && o.DocumentDate >= fromDate && o.DocumentDate <= toDate
                              select new { o.DocumentDate, o.Currency, o.OriginalTransaction, o.OriginalBase, j.Id, j.CreatedByUserId }).ToListAsync(ct).ConfigureAwait(false);
        List<long> journalIds = [.. receipts.Select(r => r.Id)];
        Dictionary<long, string> banks = (await _db.Set<JournalLine>().AsNoTracking().Where(l => journalIds.Contains(l.JournalEntryId) && l.BaseDebit > 0m)
                .Select(l => new { l.JournalEntryId, l.AccountCode }).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(l => l.JournalEntryId).ToDictionary(g => g.Key, g => g.First().AccountCode);
        Dictionary<string, string> names = await _db.Accounts.AsNoTracking().Where(a => a.CompanyId == companyId).ToDictionaryAsync(a => a.AccountCode, a => a.Name, ct).ConfigureAwait(false);
        Dictionary<long, string> users = await _db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Username, ct).ConfigureAwait(false);
        return [.. receipts.GroupBy(r => (r.DocumentDate, Bank: banks.GetValueOrDefault(r.Id, "?"), r.Currency, r.CreatedByUserId))
            .OrderBy(g => g.Key.DocumentDate).ThenBy(g => g.Key.Bank, StringComparer.Ordinal).ThenBy(g => g.Key.Currency.ToString(), StringComparer.Ordinal)
            .Select(g => Row(("Date", Date(g.Key.DocumentDate)), ("Method", $"{g.Key.Bank} {names.GetValueOrDefault(g.Key.Bank, string.Empty)}".Trim()), ("Currency", g.Key.Currency.ToString()),
                ("User", users.GetValueOrDefault(g.Key.CreatedByUserId, g.Key.CreatedByUserId == 0 ? "system" : $"user {g.Key.CreatedByUserId}")), ("Count", g.Count()),
                ("Amount", Money(g.Sum(r => Math.Abs(r.OriginalTransaction)))), ("Base", g.Sum(r => Math.Abs(r.OriginalBase)))))];
    }

    private static readonly OpenItemType[] SalesTypes = [OpenItemType.Invoice, OpenItemType.CreditNote, OpenItemType.DebitNote];

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> DayBookAsync(long companyId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        Dictionary<long, Customer> customers = await CustomersAsync(companyId, ct).ConfigureAwait(false);
        List<OpenItem> items = await _db.OpenItems.AsNoTracking().Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable && SalesTypes.Contains(i.ItemType)
            && i.DocumentDate >= from && i.DocumentDate <= to).OrderBy(i => i.DocumentDate).ThenBy(i => i.DocumentNumber).ToListAsync(ct).ConfigureAwait(false);
        return [.. items.Select(i =>
        {
            decimal sign = i.ItemType == OpenItemType.CreditNote ? -1m : 1m;
            return Row(("Date", Date(i.DocumentDate)), ("Document", i.DocumentNumber), ("Type", i.ItemType.ToString()), ("Customer", customers.GetValueOrDefault(i.TraderId)?.Name ?? "?"),
                ("Currency", i.Currency.Value), ("Transaction", Money(sign * i.OriginalTransaction)), ("Base", sign * i.OriginalBase));
        })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> TopCustomersAsync(long companyId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        Dictionary<long, Customer> customers = await CustomersAsync(companyId, ct).ConfigureAwait(false);
        var sales = await _db.OpenItems.AsNoTracking().Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable && SalesTypes.Contains(i.ItemType)
            && i.DocumentDate >= from && i.DocumentDate <= to)
            .GroupBy(i => i.TraderId).Select(g => new { g.Key, Count = g.Count(), Net = g.Sum(i => i.ItemType == OpenItemType.CreditNote ? -i.OriginalBase : i.OriginalBase) })
            .ToListAsync(ct).ConfigureAwait(false);
        decimal total = sales.Sum(s => s.Net);
        decimal running = 0m;
        int rank = 0;
        return [.. sales.OrderByDescending(s => s.Net).ThenBy(s => customers.GetValueOrDefault(s.Key)?.Name, StringComparer.Ordinal).Select(s =>
        {
            running += s.Net;
            return Row(("Rank", ++rank), ("Customer", customers.GetValueOrDefault(s.Key)?.Name ?? "?"), ("Documents", s.Count), ("Share", Percent(s.Net, total)), ("Cumulative", Percent(running, total)),
                ("Sales", s.Net));
        })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> UnallocatedAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        Dictionary<long, Customer> customers = await CustomersAsync(companyId, ct).ConfigureAwait(false);
        List<OpenItemAsAt> items = await OpenItemsAsAt.LoadAsync(_db, companyId, TradingLedger.AccountsReceivable, asAt, ct).ConfigureAwait(false);
        return [.. items.Where(i => i.Item.Sign == LedgerSign.Credit && customers.ContainsKey(i.Item.TraderId) && i.OutstandingBase != 0m)
            .OrderBy(i => i.Item.DocumentDate).ThenBy(i => i.Item.DocumentNumber)
            .Select(i => Row(("Customer", customers[i.Item.TraderId].Name), ("Document", i.Item.DocumentNumber), ("Type", i.Item.ItemType.ToString()), ("Date", Date(i.Item.DocumentDate)),
                ("Days", Math.Max(0, asAt.DayNumber - i.Item.DocumentDate.DayNumber)), ("Unallocated", i.OutstandingBase)))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> DsoAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        Dictionary<long, Customer> customers = await CustomersAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<long, decimal> owing = (await OpenItemsAsAt.LoadAsync(_db, companyId, TradingLedger.AccountsReceivable, asAt, ct).ConfigureAwait(false))
            .GroupBy(i => i.Item.TraderId).ToDictionary(g => g.Key, g => g.Sum(i => i.Signed(LedgerSign.Debit)));
        DateOnly from = asAt.AddDays(-89);
        Dictionary<long, decimal> sales = await _db.OpenItems.AsNoTracking().Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable && SalesTypes.Contains(i.ItemType)
            && i.DocumentDate >= from && i.DocumentDate <= asAt).GroupBy(i => i.TraderId)
            .Select(g => new { g.Key, Net = g.Sum(i => i.ItemType == OpenItemType.CreditNote ? -i.OriginalBase : i.OriginalBase) }).ToDictionaryAsync(x => x.Key, x => x.Net, ct).ConfigureAwait(false);
        return [.. owing.Where(o => o.Value > 0m && customers.ContainsKey(o.Key)).OrderBy(o => customers[o.Key].Name, StringComparer.Ordinal).Select(o =>
        {
            decimal recent = sales.GetValueOrDefault(o.Key);
            return Row(("Customer", customers[o.Key].Name), ("Sales90", Money(recent)), ("Dso", recent <= 0m ? "no sales" : Math.Round(o.Value / recent * 90m).ToString(CultureInfo.InvariantCulture)),
                ("Terms", customers[o.Key].PaymentTermsDays), ("Owing", o.Value));
        })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> PromisesAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        Dictionary<long, Customer> customers = await CustomersAsync(companyId, ct).ConfigureAwait(false);
        List<PaymentPromise> promises = await _db.Set<PaymentPromise>().AsNoTracking().Where(p => p.CompanyId == companyId && p.MadeOn <= asAt).OrderBy(p => p.PayBy).ThenBy(p => p.Id)
            .ToListAsync(ct).ConfigureAwait(false);
        return [.. promises.Select(p => Row(("PayBy", Date(p.PayBy)), ("Customer", customers.GetValueOrDefault(p.CustomerId)?.Name ?? "?"), ("Made", Date(p.MadeOn)),
            ("Status", p.Status == PromiseStatus.Open && p.PayBy < asAt ? "Open (overdue)" : p.Status.ToString()), ("Resolved", p.ResolvedOn is DateOnly r ? Date(r) : "—"),
            ("Note", p.Note ?? string.Empty), ("Amount", p.Amount)))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> WriteOffsAsync(long companyId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        Dictionary<long, Customer> customers = await CustomersAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<long, string> users = await _db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Username, ct).ConfigureAwait(false);
        Dictionary<long, DebtCase> cases = (await _db.Set<DebtCase>().AsNoTracking().Where(c => c.CompanyId == companyId && c.Stage == DebtCaseStage.WrittenOff).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(c => c.CustomerId).ToDictionary(g => g.Key, g => g.OrderBy(c => c.ClosedOn).Last());
        List<OpenItem> items = await _db.OpenItems.AsNoTracking().Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable && i.ItemType == OpenItemType.WriteOff
            && i.DocumentDate >= from && i.DocumentDate <= to).OrderBy(i => i.DocumentDate).ThenBy(i => i.DocumentNumber).ToListAsync(ct).ConfigureAwait(false);
        return [.. items.Select(i => Row(("Date", Date(i.DocumentDate)), ("Customer", customers.GetValueOrDefault(i.TraderId)?.Name ?? "?"), ("Document", i.DocumentNumber),
            ("Case", cases.TryGetValue(i.TraderId, out DebtCase? c)
                ? $"{c.CaseNumber}, approved by {(c.WriteOffApprovedByUserId is long u ? users.GetValueOrDefault(u, $"user {u}") : "?")}" : "—"),
            ("Amount", i.OriginalBase)))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> LaybysAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        Dictionary<long, Customer> customers = await CustomersAsync(companyId, ct).ConfigureAwait(false);
        List<LaybyPlan> plans = await _db.Set<LaybyPlan>().AsNoTracking().Include(p => p.Lines).Where(p => p.CompanyId == companyId && p.OpenedOn <= asAt).OrderBy(p => p.FinalDate).ThenBy(p => p.PlanNumber)
            .ToListAsync(ct).ConfigureAwait(false);
        return [.. plans.Select(p =>
        {
            decimal price = p.Lines.Sum(l => Math.Round(l.Quantity * l.UnitPrice, 2, MidpointRounding.AwayFromZero));
            string due = p.Status != LaybyStatus.Open ? "—" : p.FinalDate < asAt ? $"{asAt.DayNumber - p.FinalDate.DayNumber} overdue" : $"{p.FinalDate.DayNumber - asAt.DayNumber} to go";
            return Row(("Plan", p.PlanNumber), ("Customer", customers.GetValueOrDefault(p.CustomerId)?.Name ?? "?"), ("Opened", Date(p.OpenedOn)), ("Final", Date(p.FinalDate)), ("Status", p.Status.ToString()),
                ("Price", Money(price)), ("Paid", Money(p.Paid)), ("Due", due), ("Balance", p.Status == LaybyStatus.Open ? price - p.Paid : 0m));
        })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ExposureAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        List<OpenItemAsAt> items = await OpenItemsAsAt.LoadAsync(_db, companyId, TradingLedger.AccountsReceivable, asAt, ct).ConfigureAwait(false);
        decimal total = items.Sum(i => i.Signed(LedgerSign.Debit));
        return [.. items.GroupBy(i => i.Item.Currency.Value).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g =>
        {
            decimal baseAmount = g.Sum(i => i.Signed(LedgerSign.Debit));
            return Row(("Currency", g.Key), ("Customers", g.Select(i => i.Item.TraderId).Distinct().Count()), ("Transaction", Money(g.Sum(i => i.SignedTransaction(LedgerSign.Debit)))),
                ("Share", Percent(baseAmount, total)), ("Base", baseAmount));
        })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> NewDormantAsync(long companyId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        Dictionary<long, Customer> customers = await CustomersAsync(companyId, ct).ConfigureAwait(false);
        var history = await _db.OpenItems.AsNoTracking().Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable && i.ItemType == OpenItemType.Invoice && i.DocumentDate <= to)
            .GroupBy(i => i.TraderId).Select(g => new { g.Key, First = g.Min(i => i.DocumentDate), Last = g.Max(i => i.DocumentDate), Total = g.Sum(i => i.OriginalBase) })
            .ToListAsync(ct).ConfigureAwait(false);
        DateOnly dormantSince = to.AddDays(-90);
        return [.. history.Where(h => customers.ContainsKey(h.Key) && (h.First >= from || h.Last < dormantSince))
            .Select(h => (Status: h.First >= from ? "New" : "Dormant", h))
            .OrderBy(x => x.Status, StringComparer.Ordinal).ThenBy(x => customers[x.h.Key].Name, StringComparer.Ordinal)
            .Select(x => Row(("Status", x.Status), ("Customer", customers[x.h.Key].Name), ("First", Date(x.h.First)), ("Last", Date(x.h.Last)), ("Lifetime", x.h.Total)))];
    }

    // Invoiced sales-order lines (orders dated in the period) and completed POS sales (as cash sales), costed at the item's current average cost.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> SalesAnalysisAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        Dictionary<long, Customer> customers = await CustomersAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<long, string> items = await _db.Items.AsNoTracking().IgnoreQueryFilters().Where(i => i.CompanyId == companyId).ToDictionaryAsync(i => i.Id, i => i.Code + " " + i.Description, ct).ConfigureAwait(false);
        Dictionary<long, decimal> averageCost = (await _db.StockBalances.AsNoTracking().Where(b => b.CompanyId == companyId).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(b => b.ItemId).ToDictionary(g => g.Key, g => g.Sum(b => b.QuantityOnHand) == 0m ? g.Average(b => b.AverageCost) : g.Sum(b => b.TotalValue) / g.Sum(b => b.QuantityOnHand));
        var orders = await (from l in _db.Set<Core.SalesOrders.SalesOrderLine>().AsNoTracking() join o in _db.SalesOrders.AsNoTracking() on l.SalesOrderId equals o.Id
                            where o.CompanyId == companyId && o.OrderDate >= fromDate && o.OrderDate <= toDate && l.InvoicedQuantity > 0m
                            select new { o.CustomerId, l.ItemId, Quantity = l.InvoicedQuantity, Net = l.InvoicedQuantity * l.UnitPrice }).ToListAsync(ct).ConfigureAwait(false);
        var pos = await (from l in _db.Set<Core.Pos.SaleLine>().AsNoTracking() join s in _db.Sales.AsNoTracking() on l.SaleId equals s.Id
                         where s.CompanyId == companyId && s.Status == Core.Pos.SaleStatus.Completed && s.SaleDate >= fromDate && s.SaleDate <= toDate
                         select new { l.ItemId, l.Quantity, Net = l.LineNet }).ToListAsync(ct).ConfigureAwait(false);
        return [.. orders.Select(o => (Customer: customers.GetValueOrDefault(o.CustomerId)?.Name ?? "?", o.ItemId, o.Quantity, o.Net))
            .Concat(pos.Select(p => (Customer: "Cash sales (POS)", p.ItemId, p.Quantity, p.Net)))
            .GroupBy(x => (x.Customer, x.ItemId)).Select(g => (g.Key.Customer, g.Key.ItemId, Quantity: g.Sum(x => x.Quantity), Net: Math.Round(g.Sum(x => x.Net), 2, MidpointRounding.AwayFromZero)))
            .Select(x => (x.Customer, x.ItemId, x.Quantity, x.Net, Cost: Math.Round(x.Quantity * averageCost.GetValueOrDefault(x.ItemId), 2, MidpointRounding.AwayFromZero)))
            .OrderBy(x => x.Customer, StringComparer.Ordinal).ThenByDescending(x => x.Net)
            .Select(x => Row(("Customer", x.Customer), ("Item", items.GetValueOrDefault(x.ItemId, "?")), ("Quantity", x.Quantity.ToString("0.###", CultureInfo.InvariantCulture)), ("Sales", Money(x.Net)),
                ("Cost", Money(x.Cost)), ("MarginPercent", Percent(x.Net - x.Cost, x.Net)), ("Margin", x.Net - x.Cost)))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> SalesTrendAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        Dictionary<long, Customer> customers = await CustomersAsync(companyId, ct).ConfigureAwait(false);
        DateOnly earliest = new DateOnly(asAt.Year - 1, 1, 1) < asAt.AddMonths(-6) ? new DateOnly(asAt.Year - 1, 1, 1) : asAt.AddMonths(-6);
        var sales = await _db.OpenItems.AsNoTracking().Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable && SalesTypes.Contains(i.ItemType) && i.DocumentDate >= earliest && i.DocumentDate <= asAt)
            .Select(i => new { i.TraderId, i.DocumentDate, Net = i.ItemType == OpenItemType.CreditNote ? -i.OriginalBase : i.OriginalBase }).ToListAsync(ct).ConfigureAwait(false);
        DateOnly yearStart = new(asAt.Year, 1, 1), lastYearStart = yearStart.AddYears(-1), lastYearTo = asAt.AddYears(-1), threeAgo = asAt.AddMonths(-3), sixAgo = asAt.AddMonths(-6);
        return [.. sales.GroupBy(s => s.TraderId).Where(g => customers.ContainsKey(g.Key)).Select(g =>
            {
                decimal Sum(DateOnly start, DateOnly end) => g.Where(s => s.DocumentDate >= start && s.DocumentDate <= end).Sum(s => s.Net);
                return (Customer: customers[g.Key].Name, This: Sum(yearStart, asAt), Last: Sum(lastYearStart, lastYearTo), Last3: Sum(threeAgo.AddDays(1), asAt), Prior3: Sum(sixAgo.AddDays(1), threeAgo));
            })
            .OrderByDescending(x => x.This).ThenBy(x => x.Customer, StringComparer.Ordinal)
            .Select(x => Row(("Customer", x.Customer), ("LastYear", Money(x.Last)), ("Change", x.Last == 0m ? "—" : ((x.This - x.Last) * 100m / x.Last).ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + "%"),
                ("Last3", Money(x.Last3)), ("Prior3", Money(x.Prior3)), ("Trend", x.Last3 > x.Prior3 ? "▲" : x.Last3 < x.Prior3 ? "▼" : "→"), ("ThisYear", x.This)))];
    }

    private Task<Dictionary<long, Customer>> CustomersAsync(long companyId, CancellationToken ct)
        => _db.Customers.AsNoTracking().IgnoreQueryFilters().Where(c => c.CompanyId == companyId).ToDictionaryAsync(c => c.Id, ct);

    private static DateOnly? DateParameter(IReadOnlyDictionary<string, object?> parameters, string name) => parameters.TryGetValue(name, out object? value) && value is DateOnly date ? date : null;

    private static string Percent(decimal part, decimal total) => total == 0m ? "—" : (part * 100m / total).ToString("0.0", CultureInfo.InvariantCulture) + "%";

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Money(decimal amount) => amount.ToString("N2", CultureInfo.InvariantCulture);

    private static Dictionary<string, object?> Row(params (string Key, object? Value)[] cells)
    {
        Dictionary<string, object?> row = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, object? value) in cells)
        {
            row[key] = value;
        }

        return row;
    }
}
