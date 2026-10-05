using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Core.Pos;
using One4All.Core.Vouchers;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Reporting;

/// <summary>Serves the POS-numbered retail reports (brief §14 F): every data command whose name starts with <c>Retl</c>.</summary>
public sealed class RetailReportDataProvider : IReportDataProvider
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the provider.</summary>
    /// <param name="db">The database context.</param>
    public RetailReportDataProvider(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public bool CanHandle(string command) => command.StartsWith("Retl", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetRowsAsync(long companyId, string command, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct = default)
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        DateOnly toDate = DateParameter(parameters, "ToDate") ?? today;
        DateOnly fromDate = DateParameter(parameters, "FromDate") ?? new DateOnly(toDate.Year, 1, 1);
        return command.ToUpperInvariant() switch
        {
            "RETLDAILY" => await DailyAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "RETLCASHIERS" => await CashiersAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "RETLCASHUP" => await CashUpAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "RETLHEATMAP" => await HeatmapAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "RETLPRODUCTMIX" => await ProductMixAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "RETLPROMOTIONS" => await PromotionsAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "RETLTENDERS" => await TendersAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "RETLOFFLINE" => await OfflineAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            _ => [],
        };
    }

    private Task<List<Sale>> SalesAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
        => _db.Sales.AsNoTracking().Include(s => s.Lines).Include(s => s.Tenders).Where(s => s.CompanyId == companyId && s.SaleDate >= fromDate && s.SaleDate <= toDate)
            .OrderBy(s => s.SaleDate).ThenBy(s => s.SaleNumber).ToListAsync(ct);

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> DailyAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        List<Sale> sales = await SalesAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false);
        Dictionary<long, string> tills = await TillsAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<long, string> users = await UsersAsync(ct).ConfigureAwait(false);
        return [.. sales.Where(s => s.Status != SaleStatus.Parked).GroupBy(s => (s.SaleDate, s.TerminalId, s.CashierUserId))
            .OrderBy(g => g.Key.SaleDate).ThenBy(g => tills.GetValueOrDefault(g.Key.TerminalId), StringComparer.Ordinal).ThenBy(g => users.GetValueOrDefault(g.Key.CashierUserId), StringComparer.Ordinal)
            .Select(g =>
            {
                List<Sale> completed = [.. g.Where(s => s.Status == SaleStatus.Completed)];
                decimal net = completed.SelectMany(s => s.Lines).Sum(l => l.LineNet), tax = completed.SelectMany(s => s.Lines).Sum(l => l.TaxAmount);
                string tenders = string.Join(", ", completed.SelectMany(s => s.Tenders).GroupBy(t => t.TenderType)
                    .Select(t => $"{t.Key} {Money(t.Sum(x => x.IsChange ? -x.BaseAmount : x.BaseAmount))}").DefaultIfEmpty("—"));
                return Row(("Date", Date(g.Key.SaleDate)), ("Till", tills.GetValueOrDefault(g.Key.TerminalId, "?")), ("Cashier", users.GetValueOrDefault(g.Key.CashierUserId, $"user {g.Key.CashierUserId}")),
                    ("Sales", completed.Count), ("Voided", g.Count(s => s.Status == SaleStatus.Voided)), ("Net", Money(net)), ("Tax", Money(tax)), ("Tenders", tenders), ("Gross", net + tax));
            })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> CashiersAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        List<Sale> sales = await SalesAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false);
        Dictionary<long, string> users = await UsersAsync(ct).ConfigureAwait(false);
        List<CashUpRow> cashUps = await CashUpRowsAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false);
        return [.. sales.GroupBy(s => s.CashierUserId).OrderBy(g => users.GetValueOrDefault(g.Key), StringComparer.Ordinal).Select(g =>
        {
            List<Sale> completed = [.. g.Where(s => s.Status == SaleStatus.Completed)];
            decimal net = completed.SelectMany(s => s.Lines).Sum(l => l.LineNet);
            return Row(("Cashier", users.GetValueOrDefault(g.Key, $"user {g.Key}")), ("Sessions", g.Select(s => s.TillSessionId).Distinct().Count()), ("Sales", completed.Count),
                ("Items", Qty(completed.SelectMany(s => s.Lines).Sum(l => l.Quantity))), ("AverageBasket", completed.Count == 0 ? "—" : Money(net / completed.Count)),
                ("Voids", g.Count(s => s.Status == SaleStatus.Voided)), ("Variance", Money(cashUps.Where(c => c.CashierUserId == g.Key).Sum(c => c.Variance))), ("Net", net));
        })];
    }

    private sealed record CashUpRow(long SessionId, long TerminalId, long CashierUserId, DateTimeOffset Closed, decimal Float, decimal CashIn, decimal Change, decimal Variance);

    // Expected cash = float + cash taken − change; the variance is the PosCashUp journal for the session (cash line: debit over, credit short).
    private async Task<List<CashUpRow>> CashUpRowsAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        DateTimeOffset start = new(fromDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), end = new(toDate.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
        List<TillSession> sessions = await _db.TillSessions.AsNoTracking().Where(s => s.CompanyId == companyId && s.ClosedOnUtc != null && s.ClosedOnUtc >= start && s.ClosedOnUtc <= end)
            .OrderBy(s => s.ClosedOnUtc).ToListAsync(ct).ConfigureAwait(false);
        List<long> ids = [.. sessions.Select(s => s.Id)];
        var cash = await (from t in _db.Set<SaleTender>().AsNoTracking() join s in _db.Sales.AsNoTracking() on t.SaleId equals s.Id
                          where ids.Contains(s.TillSessionId) && s.Status == SaleStatus.Completed && t.TenderType == TenderType.Cash
                          select new { s.TillSessionId, t.IsChange, t.BaseAmount }).ToListAsync(ct).ConfigureAwait(false);
        List<string> numbers = [.. ids.Select(id => $"CASHUP-{id}")];
        List<JournalEntry> journals = await _db.Journals.AsNoTracking().Include(j => j.Lines)
            .Where(j => j.CompanyId == companyId && j.DocumentType == "PosCashUp" && numbers.Contains(j.DocumentNumber!)).ToListAsync(ct).ConfigureAwait(false);
        return [.. sessions.Select(s =>
        {
            JournalEntry? journal = journals.FirstOrDefault(j => j.DocumentNumber == $"CASHUP-{s.Id}");
            decimal variance = 0m;
            if (journal is not null)
            {
                decimal amount = journal.Lines.Sum(l => l.BaseDebit);
                bool over = journal.Narrative?.Contains("(over", StringComparison.Ordinal) == true;
                variance = over ? amount : -amount;
            }

            return new CashUpRow(s.Id, s.TerminalId, s.CashierUserId, s.ClosedOnUtc!.Value, s.OpeningFloat, cash.Where(c => c.TillSessionId == s.Id && !c.IsChange).Sum(c => c.BaseAmount),
                cash.Where(c => c.TillSessionId == s.Id && c.IsChange).Sum(c => c.BaseAmount), variance);
        })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> CashUpAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        Dictionary<long, string> tills = await TillsAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<long, string> users = await UsersAsync(ct).ConfigureAwait(false);
        return [.. (await CashUpRowsAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false)).Select(c =>
        {
            decimal expected = c.Float + c.CashIn - c.Change;
            return Row(("Session", c.SessionId), ("Till", tills.GetValueOrDefault(c.TerminalId, "?")), ("Cashier", users.GetValueOrDefault(c.CashierUserId, $"user {c.CashierUserId}")),
                ("Closed", c.Closed.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)), ("Float", Money(c.Float)), ("CashIn", Money(c.CashIn)), ("Change", Money(c.Change)),
                ("Expected", Money(expected)), ("Counted", Money(expected + c.Variance)), ("Variance", c.Variance));
        })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> HeatmapAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        List<Sale> completed = [.. (await SalesAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false)).Where(s => s.Status == SaleStatus.Completed)];
        return [.. completed.GroupBy(s => (Day: s.SaleDate.DayOfWeek, Hour: s.CreatedOnUtc.Hour))
            .OrderBy(g => ((int)g.Key.Day + 6) % 7).ThenBy(g => g.Key.Hour)
            .Select(g =>
            {
                decimal net = g.SelectMany(s => s.Lines).Sum(l => l.LineNet);
                return Row(("Weekday", g.Key.Day.ToString()), ("Hour", $"{g.Key.Hour:00}:00"), ("Sales", g.Count()),
                    ("ItemsPerBasket", (g.SelectMany(s => s.Lines).Sum(l => l.Quantity) / g.Count()).ToString("0.0", CultureInfo.InvariantCulture)), ("AverageBasket", Money(net / g.Count())), ("Net", net));
            })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ProductMixAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        List<Sale> completed = [.. (await SalesAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false)).Where(s => s.Status == SaleStatus.Completed)];
        Dictionary<long, string> items = await _db.Items.AsNoTracking().IgnoreQueryFilters().Where(i => i.CompanyId == companyId).ToDictionaryAsync(i => i.Id, i => i.Code + " " + i.Description, ct).ConfigureAwait(false);
        var mix = completed.SelectMany(s => s.Lines).GroupBy(l => l.ItemId).Select(g => (Id: g.Key, Quantity: g.Sum(l => l.Quantity), Net: g.Sum(l => l.LineNet))).OrderByDescending(x => x.Net).ToList();
        decimal total = mix.Sum(x => x.Net), running = 0m;
        int rank = 0;
        return [.. mix.Select(x =>
        {
            running += x.Net;
            return Row(("Rank", ++rank), ("Item", items.GetValueOrDefault(x.Id, "?")), ("Quantity", Qty(x.Quantity)), ("Share", Percent(x.Net, total)), ("Cumulative", Percent(running, total)), ("Net", x.Net));
        })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> PromotionsAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        List<PromoCampaign> campaigns = await _db.Set<PromoCampaign>().AsNoTracking().Where(c => c.CompanyId == companyId).OrderBy(c => c.Code).ToListAsync(ct).ConfigureAwait(false);
        var redemptions = await _db.Set<PromoRedemption>().AsNoTracking().Where(r => r.CompanyId == companyId && r.Date >= fromDate && r.Date <= toDate)
            .GroupBy(r => r.PromoCampaignId).Select(g => new { g.Key, Count = g.Count(), Spend = g.Sum(r => r.Spend), Discount = g.Sum(r => r.Discount) })
            .ToDictionaryAsync(x => x.Key, ct).ConfigureAwait(false);
        return [.. campaigns.Where(c => redemptions.ContainsKey(c.Id) || (c.ValidFrom <= toDate && c.ValidTo >= fromDate)).Select(c =>
        {
            var used = redemptions.GetValueOrDefault(c.Id);
            int count = used?.Count ?? 0;
            decimal spend = used?.Spend ?? 0m, discount = used?.Discount ?? 0m;
            return Row(("Campaign", c.Code), ("Name", c.Name), ("Valid", $"{Date(c.ValidFrom)} to {Date(c.ValidTo)}"), ("Redemptions", count), ("Spend", Money(spend)),
                ("AverageSpend", count == 0 ? "—" : Money(spend / count)), ("DiscountRate", Percent(discount, spend)), ("Discount", discount));
        })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> TendersAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        List<SaleTender> tenders = [.. (await SalesAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false)).Where(s => s.Status == SaleStatus.Completed).SelectMany(s => s.Tenders)];
        decimal total = tenders.Sum(t => t.IsChange ? -t.BaseAmount : t.BaseAmount);
        return [.. tenders.GroupBy(t => (t.TenderType, Currency: t.Currency.Value)).OrderBy(g => g.Key.TenderType).ThenBy(g => g.Key.Currency, StringComparer.Ordinal).Select(g =>
        {
            decimal baseAmount = g.Sum(t => t.IsChange ? -t.BaseAmount : t.BaseAmount);
            return Row(("Tender", g.Key.TenderType.ToString()), ("Currency", g.Key.Currency), ("Count", g.Count(t => !t.IsChange)), ("Amount", Money(g.Sum(t => t.IsChange ? -t.Amount : t.Amount))),
                ("Share", Percent(baseAmount, total)), ("Base", baseAmount));
        })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> OfflineAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        DateTimeOffset start = new(fromDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), end = new(toDate.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
        return [.. (await _db.OfflineOperations.AsNoTracking().Where(o => o.CompanyId == companyId && o.CreatedUtc >= start && o.CreatedUtc <= end).OrderBy(o => o.CreatedUtc).ToListAsync(ct).ConfigureAwait(false))
            .Select(o => Row(("Created", Stamp(o.CreatedUtc)), ("Kind", o.Kind), ("Status", o.Status.ToString()), ("Attempts", o.Attempts), ("Synced", o.SyncedUtc is DateTimeOffset s ? Stamp(s) : "—"),
                ("Error", o.LastError ?? string.Empty)))];
    }

    private Task<Dictionary<long, string>> TillsAsync(long companyId, CancellationToken ct)
        => _db.PosTerminals.AsNoTracking().IgnoreQueryFilters().Where(t => t.CompanyId == companyId).ToDictionaryAsync(t => t.Id, t => t.Code, ct);

    private Task<Dictionary<long, string>> UsersAsync(CancellationToken ct) => _db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Username, ct);

    private static DateOnly? DateParameter(IReadOnlyDictionary<string, object?> parameters, string name) => parameters.TryGetValue(name, out object? value) && value is DateOnly date ? date : null;

    private static string Percent(decimal part, decimal total) => total == 0m ? "—" : (part * 100m / total).ToString("0.0", CultureInfo.InvariantCulture) + "%";

    private static string Qty(decimal quantity) => quantity.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Stamp(DateTimeOffset at) => at.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

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
