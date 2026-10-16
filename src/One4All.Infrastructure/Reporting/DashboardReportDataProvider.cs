using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Housing;
using One4All.Core.Accounting;
using One4All.Core.Municipal;
using One4All.Core.Pos;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Trading;

namespace One4All.Infrastructure.Reporting;

/// <summary>Serves the DB-numbered dashboards (brief §14 Q): every data command whose name starts with <c>Dash</c>.</summary>
public sealed class DashboardReportDataProvider : IReportDataProvider
{
    private readonly One4AllDbContext _db;
    private readonly IWorkingCapitalService _workingCapital;

    /// <summary>Creates the provider.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="workingCapital">Debtor, creditor and stock days.</param>
    public DashboardReportDataProvider(One4AllDbContext db, IWorkingCapitalService workingCapital)
    {
        _db = db;
        _workingCapital = workingCapital;
    }

    /// <inheritdoc />
    public bool CanHandle(string command) => command.StartsWith("Dash", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetRowsAsync(long companyId, string command, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct = default)
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        DateOnly asAt = DateParameter(parameters, "AsAtDate") ?? today;
        DateOnly toDate = DateParameter(parameters, "ToDate") ?? today;
        DateOnly fromDate = DateParameter(parameters, "FromDate") ?? new DateOnly(toDate.Year, 1, 1);
        return command.ToUpperInvariant() switch
        {
            "DASHEXECUTIVE" => await ExecutiveAsync(companyId, asAt, ct).ConfigureAwait(false),
            "DASHFLASH" => await FlashAsync(companyId, asAt, ct).ConfigureAwait(false),
            "DASHKPI" => await KpiAsync(companyId, asAt, ct).ConfigureAwait(false),
            "DASHBRANCHES" => await BranchesAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "DASHTREND" => await TrendAsync(companyId, asAt, ct).ConfigureAwait(false),
            "DASHMUNICIPAL" => await MunicipalAsync(companyId, asAt, ct).ConfigureAwait(false),
            _ => [],
        };
    }

    // Income and expense for a range, credit-positive for income and debit-positive for expense; cost of sales by report category.
    private async Task<(decimal Revenue, decimal Expenses, decimal CostOfSales)> ResultAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        var lines = await (from l in _db.Set<JournalLine>().AsNoTracking()
                           join j in _db.Journals.AsNoTracking() on l.JournalEntryId equals j.Id
                           join a in _db.Accounts.AsNoTracking() on new { j.CompanyId, l.AccountCode } equals new { a.CompanyId, a.AccountCode }
                           where j.CompanyId == companyId && (j.Status == JournalStatus.Posted || j.Status == JournalStatus.Reversed) && j.PostingDate >= fromDate && j.PostingDate <= toDate
                               && (a.Type == AccountType.Income || a.Type == AccountType.Expense)
                           group l by new { a.Type, a.ReportCategory } into g
                           select new { g.Key.Type, g.Key.ReportCategory, Net = g.Sum(l => l.BaseDebit - l.BaseCredit) }).ToListAsync(ct).ConfigureAwait(false);
        decimal revenue = -lines.Where(l => l.Type == AccountType.Income).Sum(l => l.Net);
        decimal expenses = lines.Where(l => l.Type == AccountType.Expense).Sum(l => l.Net);
        decimal cos = lines.Where(l => l.Type == AccountType.Expense && l.ReportCategory.Contains("Cost of", StringComparison.OrdinalIgnoreCase)).Sum(l => l.Net);
        return (revenue, expenses, cos);
    }

    private async Task<decimal> CashAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        List<string> accounts = await _db.BankAccounts.AsNoTracking().Where(b => b.CompanyId == companyId).Select(b => b.GlAccountCode).Distinct().ToListAsync(ct).ConfigureAwait(false);
        return await (from l in _db.Set<JournalLine>().AsNoTracking() join j in _db.Journals.AsNoTracking() on l.JournalEntryId equals j.Id
                      where j.CompanyId == companyId && (j.Status == JournalStatus.Posted || j.Status == JournalStatus.Reversed) && j.PostingDate <= asAt && accounts.Contains(l.AccountCode)
                      select l.BaseDebit - l.BaseCredit).SumAsync(ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ExecutiveAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        DateOnly yearStart = new(asAt.Year, 1, 1);
        (decimal revenue, decimal expenses, decimal cos) = await ResultAsync(companyId, yearStart, asAt, ct).ConfigureAwait(false);
        decimal cash = await CashAsync(companyId, asAt, ct).ConfigureAwait(false);
        decimal debtors = (await OpenItemsAsAt.LoadAsync(_db, companyId, TradingLedger.AccountsReceivable, asAt, ct).ConfigureAwait(false)).Sum(i => i.Signed(LedgerSign.Debit));
        decimal creditors = (await OpenItemsAsAt.LoadAsync(_db, companyId, TradingLedger.AccountsPayable, asAt, ct).ConfigureAwait(false)).Sum(i => i.Signed(LedgerSign.Credit));
        var metrics = await _workingCapital.ComputeAsync(companyId, yearStart, asAt, ct).ConfigureAwait(false);
        decimal stock = metrics.IsSuccess ? metrics.Value.InventoryBalance : 0m;
        List<(string, string)> measures =
        [
            ($"Revenue {asAt.Year} to date", Money(revenue)), ("Expenses to date", Money(expenses)), ("Net profit to date", Money(revenue - expenses)),
            ("Gross margin", revenue == 0m ? "—" : Percent(revenue - cos, revenue)), ("Cash & bank", Money(cash)), ("Debtors", Money(debtors)), ("Creditors", Money(creditors)),
            ("Stock", Money(stock)), ("Working capital (cash + debtors + stock − creditors)", Money(cash + debtors + stock - creditors)),
        ];
        if (metrics.IsSuccess)
        {
            measures.Add(("Debtor days (DSO)", metrics.Value.DaysSalesOutstanding.ToString("0.0", CultureInfo.InvariantCulture)));
            measures.Add(("Creditor days (DPO)", metrics.Value.DaysPayableOutstanding.ToString("0.0", CultureInfo.InvariantCulture)));
            measures.Add(("Stock days (DIO)", metrics.Value.DaysInventoryOutstanding.ToString("0.0", CultureInfo.InvariantCulture)));
            measures.Add(("Cash conversion cycle (days)", metrics.Value.CashConversionCycle.ToString("0.0", CultureInfo.InvariantCulture)));
        }

        return [.. measures.Select(m => Row(("Measure", m.Item1), ("Value", m.Item2)))];
    }

    // Sales = customer invoices and completed POS sales (net); receipts = customer receipts and POS takings; payments = supplier payments.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> FlashAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        DateOnly monthStart = new(asAt.Year, asAt.Month, 1), lastWeek = asAt.AddDays(-7), earliest = monthStart < lastWeek ? monthStart : lastWeek;
        var items = await _db.OpenItems.AsNoTracking().Where(i => i.CompanyId == companyId && i.DocumentDate >= earliest && i.DocumentDate <= asAt
                && ((i.Ledger == TradingLedger.AccountsReceivable && (i.ItemType == OpenItemType.Invoice || i.ItemType == OpenItemType.Receipt))
                    || (i.Ledger == TradingLedger.AccountsPayable && i.ItemType == OpenItemType.Payment)))
            .Select(i => new { i.Ledger, i.ItemType, i.DocumentDate, i.OriginalBase }).ToListAsync(ct).ConfigureAwait(false);
        var pos = await _db.Sales.AsNoTracking().Include(s => s.Lines).Include(s => s.Tenders)
            .Where(s => s.CompanyId == companyId && s.Status == SaleStatus.Completed && s.SaleDate >= earliest && s.SaleDate <= asAt).ToListAsync(ct).ConfigureAwait(false);
        decimal Sales(Func<DateOnly, bool> on) => items.Where(i => i.ItemType == OpenItemType.Invoice && on(i.DocumentDate)).Sum(i => i.OriginalBase)
            + pos.Where(s => on(s.SaleDate)).SelectMany(s => s.Lines).Sum(l => l.LineNet);
        decimal Receipts(Func<DateOnly, bool> on) => items.Where(i => i.Ledger == TradingLedger.AccountsReceivable && i.ItemType == OpenItemType.Receipt && on(i.DocumentDate)).Sum(i => i.OriginalBase)
            + pos.Where(s => on(s.SaleDate)).SelectMany(s => s.Tenders).Sum(t => t.IsChange ? -t.BaseAmount : t.BaseAmount);
        decimal Payments(Func<DateOnly, bool> on) => items.Where(i => i.Ledger == TradingLedger.AccountsPayable && on(i.DocumentDate)).Sum(i => i.OriginalBase);
        bool Today(DateOnly d) => d == asAt;
        bool LastWeek(DateOnly d) => d == lastWeek;
        bool Month(DateOnly d) => d >= monthStart && d <= asAt;
        IReadOnlyDictionary<string, object?> Line(string measure, Func<Func<DateOnly, bool>, decimal> value)
            => Row(("Measure", measure), ("Today", Money(value(Today))), ("LastWeek", Money(value(LastWeek))), ("MonthToDate", Money(value(Month))));
        decimal cash = await CashAsync(companyId, asAt, ct).ConfigureAwait(false);
        return
        [
            Line("Sales (invoices + POS, net)", Sales), Line("Receipts", Receipts), Line("Supplier payments", Payments),
            Row(("Measure", "Cash & bank at close"), ("Today", Money(cash)), ("LastWeek", Money(await CashAsync(companyId, lastWeek, ct).ConfigureAwait(false))), ("MonthToDate", "—")),
        ];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> KpiAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        DateOnly thisStart = new(asAt.Year, asAt.Month, 1), lastStart = thisStart.AddMonths(-1), lastEnd = thisStart.AddDays(-1);
        var now = await ResultAsync(companyId, thisStart, asAt, ct).ConfigureAwait(false);
        var before = await ResultAsync(companyId, lastStart, lastEnd, ct).ConfigureAwait(false);
        var nowWc = await _workingCapital.ComputeAsync(companyId, thisStart, asAt, ct).ConfigureAwait(false);
        var beforeWc = await _workingCapital.ComputeAsync(companyId, lastStart, lastEnd, ct).ConfigureAwait(false);
        static decimal? Margin((decimal Revenue, decimal Expenses, decimal CostOfSales) r) => r.Revenue == 0m ? null : (r.Revenue - r.CostOfSales) * 100m / r.Revenue;
        IReadOnlyDictionary<string, object?> Kpi(string name, decimal? current, decimal? prior, bool higherIsBetter, string format)
        {
            string trend = current is null || prior is null || current == prior ? "→" : (current > prior) == higherIsBetter ? "▲ better" : "▼ worse";
            return Row(("Kpi", name), ("ThisMonth", current?.ToString(format, CultureInfo.InvariantCulture) ?? "—"), ("LastMonth", prior?.ToString(format, CultureInfo.InvariantCulture) ?? "—"), ("Trend", trend));
        }

        return
        [
            Kpi("Revenue", now.Revenue, before.Revenue, true, "N2"),
            Kpi("Net profit", now.Revenue - now.Expenses, before.Revenue - before.Expenses, true, "N2"),
            Kpi("Gross margin %", Margin(now), Margin(before), true, "0.0"),
            Kpi("Debtor days (DSO)", nowWc.IsSuccess ? nowWc.Value.DaysSalesOutstanding : null, beforeWc.IsSuccess ? beforeWc.Value.DaysSalesOutstanding : null, false, "0.0"),
            Kpi("Creditor days (DPO)", nowWc.IsSuccess ? nowWc.Value.DaysPayableOutstanding : null, beforeWc.IsSuccess ? beforeWc.Value.DaysPayableOutstanding : null, true, "0.0"),
            Kpi("Stock days (DIO)", nowWc.IsSuccess ? nowWc.Value.DaysInventoryOutstanding : null, beforeWc.IsSuccess ? beforeWc.Value.DaysInventoryOutstanding : null, false, "0.0"),
            Kpi("Cash conversion cycle", nowWc.IsSuccess ? nowWc.Value.CashConversionCycle : null, beforeWc.IsSuccess ? beforeWc.Value.CashConversionCycle : null, false, "0.0"),
            Kpi("Cash & bank", await CashAsync(companyId, asAt, ct).ConfigureAwait(false), await CashAsync(companyId, lastEnd, ct).ConfigureAwait(false), true, "N2"),
        ];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> BranchesAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        Dictionary<long, string> branches = await _db.Branches.AsNoTracking().Where(b => b.CompanyId == companyId).ToDictionaryAsync(b => b.Id, b => b.Name, ct).ConfigureAwait(false);
        Dictionary<long, long> terminals = await _db.PosTerminals.AsNoTracking().IgnoreQueryFilters([Persistence.One4AllDbContext.SoftDeleteFilter]).Where(t => t.CompanyId == companyId).ToDictionaryAsync(t => t.Id, t => t.BranchId, ct).ConfigureAwait(false);
        var sales = (await _db.Sales.AsNoTracking().Include(s => s.Lines).Where(s => s.CompanyId == companyId && s.Status == SaleStatus.Completed && s.SaleDate >= fromDate && s.SaleDate <= toDate)
                .ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(s => terminals.GetValueOrDefault(s.TerminalId)).Select(g => (Branch: g.Key, Count: g.Count(), Net: g.SelectMany(s => s.Lines).Sum(l => l.LineNet))).OrderByDescending(x => x.Net).ToList();
        decimal total = sales.Sum(s => s.Net);
        int rank = 0;
        return [.. sales.Select(s => Row(("Rank", ++rank), ("Branch", branches.GetValueOrDefault(s.Branch, "?")), ("Transactions", s.Count), ("AverageBasket", Money(s.Net / s.Count)),
            ("Share", Percent(s.Net, total)), ("Sales", s.Net)))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> TrendAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        DateOnly first = new DateOnly(asAt.Year, asAt.Month, 1).AddMonths(-35);
        var monthly = (await (from l in _db.Set<JournalLine>().AsNoTracking()
                              join j in _db.Journals.AsNoTracking() on l.JournalEntryId equals j.Id
                              join a in _db.Accounts.AsNoTracking() on new { j.CompanyId, l.AccountCode } equals new { a.CompanyId, a.AccountCode }
                              where j.CompanyId == companyId && (j.Status == JournalStatus.Posted || j.Status == JournalStatus.Reversed) && j.PostingDate >= first && j.PostingDate <= asAt
                                  && (a.Type == AccountType.Income || a.Type == AccountType.Expense)
                              select new { j.PostingDate, a.Type, Net = l.BaseDebit - l.BaseCredit }).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(x => (x.PostingDate.Year, x.PostingDate.Month))
            .ToDictionary(g => g.Key, g => (Revenue: -g.Where(x => x.Type == AccountType.Income).Sum(x => x.Net), Expenses: g.Where(x => x.Type == AccountType.Expense).Sum(x => x.Net)));
        List<IReadOnlyDictionary<string, object?>> rows = [];
        for (int back = 23; back >= 0; back--)
        {
            DateOnly month = new DateOnly(asAt.Year, asAt.Month, 1).AddMonths(-back);
            var current = monthly.GetValueOrDefault((month.Year, month.Month));
            var prior = monthly.GetValueOrDefault((month.Year - 1, month.Month));
            rows.Add(Row(("Month", month.ToString("yyyy-MM", CultureInfo.InvariantCulture)), ("Revenue", Money(current.Revenue)), ("Expenses", Money(current.Expenses)), ("LastYear", Money(prior.Revenue)),
                ("YearOnYear", prior.Revenue == 0m ? "—" : ((current.Revenue - prior.Revenue) * 100m / prior.Revenue).ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + "%"),
                ("Profit", current.Revenue - current.Expenses)));
        }

        return rows;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> MunicipalAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        DateOnly yearStart = new(asAt.Year, 1, 1);
        HashSet<long> ratepayers = [.. await _db.Properties.AsNoTracking().Where(p => p.CompanyId == companyId).Select(p => p.RatepayerCustomerId).Distinct().ToListAsync(ct).ConfigureAwait(false)];
        decimal billed = (await _db.Set<BillingRun>().AsNoTracking().Include(r => r.Lines)
                .Where(r => r.CompanyId == companyId && r.Status == BillingRunStatus.Posted && r.BillDate >= yearStart && r.BillDate <= asAt).ToListAsync(ct).ConfigureAwait(false))
            .SelectMany(r => r.Lines).Where(l => l.Included).Sum(l => l.Charge);
        List<long> payers = [.. ratepayers];
        decimal collected = await _db.OpenItems.AsNoTracking().Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable && i.ItemType == OpenItemType.Receipt
            && payers.Contains(i.TraderId) && i.DocumentDate >= yearStart && i.DocumentDate <= asAt).SumAsync(i => i.OriginalBase, ct).ConfigureAwait(false);
        List<OpenItemAsAt> items = [.. (await OpenItemsAsAt.LoadAsync(_db, companyId, TradingLedger.AccountsReceivable, asAt, ct).ConfigureAwait(false)).Where(i => ratepayers.Contains(i.Item.TraderId))];
        decimal debtors = items.Sum(i => i.Signed(LedgerSign.Debit));
        decimal over90 = items.Where(i => i.Signed(LedgerSign.Debit) > 0m && DebtorAgeing.Band(i.Item.DueDate, asAt) >= 4).Sum(i => i.Signed(LedgerSign.Debit));
        int indigent = await _db.Set<IndigentRegistration>().CountAsync(r => r.CompanyId == companyId && r.Status == IndigentStatus.Active, ct).ConfigureAwait(false);
        int disconnections = await _db.Set<ServiceOrder>().CountAsync(o => o.CompanyId == companyId && o.Kind == ServiceOrderKind.Disconnect && o.Status == ServiceOrderStatus.Open, ct).ConfigureAwait(false);
        int faults = await _db.Set<ServiceTicket>().CountAsync(t => t.CompanyId == companyId && (t.Status == TicketStatus.Open || t.Status == TicketStatus.Assigned), ct).ConfigureAwait(false);
        DateOnly soon = asAt.AddDays(30);
        int licences = await _db.Set<CouncilLicence>().CountAsync(l => l.CompanyId == companyId && l.Status == CouncilLicenceStatus.Active && l.ExpiresOn >= asAt && l.ExpiresOn <= soon, ct).ConfigureAwait(false);
        decimal roll = await _db.Properties.AsNoTracking().Where(p => p.CompanyId == companyId).SumAsync(p => p.Valuation, ct).ConfigureAwait(false);

        // Water losses: what the bulk-supply meters took in this year against what consumer water meters were billed for. Bulk meters are typed "bulk".
        HashSet<long> waterMeters = [.. await _db.Properties.AsNoTracking().Where(p => p.CompanyId == companyId).SelectMany(p => p.Services)
            .Where(s => s.ServiceType == ServiceType.Water && s.MeterId != null).Select(s => s.MeterId!.Value).Distinct().ToListAsync(ct).ConfigureAwait(false)];
        Dictionary<long, bool> bulk = await _db.Set<Meter>().AsNoTracking().Where(m => m.CompanyId == companyId && m.MeterType != null)
            .ToDictionaryAsync(m => m.Id, m => m.MeterType!.Contains("bulk", StringComparison.OrdinalIgnoreCase), ct).ConfigureAwait(false);
        var readings = await _db.MeterReadings.AsNoTracking().Where(r => r.CompanyId == companyId && r.Accepted && r.ReadingDate >= yearStart && r.ReadingDate <= asAt)
            .Select(r => new { r.MeterId, r.Consumption, r.BilledConsumption }).ToListAsync(ct).ConfigureAwait(false);
        decimal bulkSupplied = readings.Where(r => bulk.GetValueOrDefault(r.MeterId)).Sum(r => r.Consumption);
        decimal metered = readings.Where(r => !bulk.GetValueOrDefault(r.MeterId) && waterMeters.Contains(r.MeterId)).Sum(r => r.BilledConsumption);
        decimal lost = bulkSupplied - metered;

        // Arrears trend: ratepayer debtors over 30 days at each of the last six month-ends.
        List<IReadOnlyDictionary<string, object?>> trend = [];
        for (int back = 5; back >= 0; back--)
        {
            DateOnly monthEnd = new DateOnly(asAt.Year, asAt.Month, 1).AddMonths(-back + 1).AddDays(-1);
            if (monthEnd > asAt)
            {
                monthEnd = asAt;
            }

            List<OpenItemAsAt> then = back == 0 ? items : [.. (await OpenItemsAsAt.LoadAsync(_db, companyId, TradingLedger.AccountsReceivable, monthEnd, ct).ConfigureAwait(false)).Where(i => ratepayers.Contains(i.Item.TraderId))];
            decimal arrears = then.Where(i => i.Signed(LedgerSign.Debit) > 0m && DebtorAgeing.Band(i.Item.DueDate, monthEnd) >= 2).Sum(i => i.Signed(LedgerSign.Debit));
            trend.Add(Row(("Measure", $"Arrears (over 30 days) at {monthEnd:yyyy-MM-dd}"), ("Value", Money(arrears))));
        }

        return
        [
            Row(("Measure", $"Billed {asAt.Year} to date"), ("Value", Money(billed))), Row(("Measure", "Collected from ratepayers to date"), ("Value", Money(collected))),
            Row(("Measure", "Collection rate"), ("Value", billed == 0m ? "—" : Percent(collected, billed))), Row(("Measure", "Ratepayer debtors"), ("Value", Money(debtors))),
            Row(("Measure", "of which over 90 days"), ("Value", Money(over90))), Row(("Measure", "Valuation roll total"), ("Value", Money(roll))),
            Row(("Measure", "Indigent households"), ("Value", indigent.ToString(CultureInfo.InvariantCulture))), Row(("Measure", "Disconnection orders open"), ("Value", disconnections.ToString(CultureInfo.InvariantCulture))),
            Row(("Measure", "Open fault tickets"), ("Value", faults.ToString(CultureInfo.InvariantCulture))), Row(("Measure", "Licences expiring within 30 days"), ("Value", licences.ToString(CultureInfo.InvariantCulture))),
            Row(("Measure", $"Water bought in bulk {asAt.Year} to date (units)"), ("Value", Money(bulkSupplied))), Row(("Measure", "Water metered to consumers to date (units)"), ("Value", Money(metered))),
            Row(("Measure", "Water losses (units)"), ("Value", Money(lost))), Row(("Measure", "Water losses as a share of supply"), ("Value", bulkSupplied == 0m ? "—" : Percent(lost, bulkSupplied))),
            .. trend,
        ];
    }

    private static DateOnly? DateParameter(IReadOnlyDictionary<string, object?> parameters, string name) => parameters.TryGetValue(name, out object? value) && value is DateOnly date ? date : null;

    private static string Percent(decimal part, decimal total) => total == 0m ? "—" : (part * 100m / total).ToString("0.0", CultureInfo.InvariantCulture) + "%";

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
