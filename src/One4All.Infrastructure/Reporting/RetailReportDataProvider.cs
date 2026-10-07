using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Pos;
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
            "RETLZREAD" => await ZReadAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "RETLCASHIERS" => await CashiersAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "RETLCASHUP" => await CashUpAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "RETLHEATMAP" => await HeatmapAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "RETLPRODUCTMIX" => await ProductMixAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "RETLPROMOTIONS" => await PromotionsAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "RETLTENDERS" => await TendersAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "RETLVOIDS" => await EventsAsync(companyId, fromDate, toDate, [PosEventKind.LineVoid, PosEventKind.SaleVoid], (e, n) => Row(("Date", Date(e.EventDate)), ("Till", n.Till(e)), ("Cashier", n.Cashier(e)),
                ("Kind", e.Kind == PosEventKind.SaleVoid ? "Sale" : "Line"), ("Sale", e.SaleNumber ?? "—"), ("Description", e.Description), ("Reason", e.Reason ?? string.Empty), ("AuthorisedBy", n.Authoriser(e)), ("Amount", e.Amount)), ct)
                .ConfigureAwait(false),
            "RETLREFUNDS" => await EventsAsync(companyId, fromDate, toDate, [PosEventKind.Refund], (e, n) => Row(("Date", Date(e.EventDate)), ("Till", n.Till(e)), ("Cashier", n.Cashier(e)), ("Sale", e.SaleNumber ?? "—"),
                ("Description", e.Description), ("Quantity", Qty(e.Quantity ?? 0m)), ("Reason", e.Reason ?? string.Empty), ("AuthorisedBy", n.Authoriser(e)), ("Journal", e.Reference ?? "—"), ("Restock", e.Outcome ?? string.Empty),
                ("Amount", e.Amount)), ct).ConfigureAwait(false),
            "RETLDISCOUNTS" => await EventsAsync(companyId, fromDate, toDate, [PosEventKind.LineDiscount], (e, n) => Row(("Date", Date(e.EventDate)), ("Till", n.Till(e)), ("Cashier", n.Cashier(e)), ("Sale", e.SaleNumber ?? "—"),
                ("Description", e.Description), ("List", Money(e.OriginalAmount ?? 0m)), ("Rate", Percent(e.Amount, e.OriginalAmount ?? 0m)), ("Reason", e.Reason ?? string.Empty), ("Amount", e.Amount)), ct).ConfigureAwait(false),
            "RETLOVERRIDES" => await EventsAsync(companyId, fromDate, toDate, [PosEventKind.PriceOverride], (e, n) => Row(("Date", Date(e.EventDate)), ("Till", n.Till(e)), ("Cashier", n.Cashier(e)), ("AuthorisedBy", n.Authoriser(e)),
                ("Sale", e.SaleNumber ?? "—"), ("Description", e.Description), ("List", Money(e.OriginalAmount ?? 0m)), ("Keyed", Money(e.Amount)), ("Reason", e.Reason ?? string.Empty), ("Difference", e.Amount - (e.OriginalAmount ?? 0m))), ct)
                .ConfigureAwait(false),
            "RETLNOSALE" => await NoSaleAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "RETLPARKED" => await ParkedAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "RETLLOYALTY" => await LoyaltyAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "RETLREPRINTS" => await EventsAsync(companyId, fromDate, toDate, [PosEventKind.Reprint], (e, n) => Row(("Printed", Stamp(e.OccurredOnUtc)), ("Till", n.Till(e)), ("Cashier", n.Cashier(e)), ("Sale", e.SaleNumber ?? "—"),
                ("Reason", e.Reason ?? string.Empty), ("Amount", e.Amount)), ct).ConfigureAwait(false),
            "RETLFISCAL" => await EventsAsync(companyId, fromDate, toDate, [PosEventKind.FiscalSubmission], (e, n) => Row(("Submitted", Stamp(e.OccurredOnUtc)), ("Till", n.Till(e)), ("Sale", e.SaleNumber ?? "—"),
                ("Reference", e.Reference ?? "—"), ("Outcome", e.Outcome ?? "Pending"), ("Amount", e.Amount)), ct).ConfigureAwait(false),
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

    // Resolves till and user names for event rows.
    private sealed record EventNames(Dictionary<long, string> Tills, Dictionary<long, string> Users)
    {
        public string Till(PosEvent e) => Tills.GetValueOrDefault(e.TerminalId, "?");

        public string Cashier(PosEvent e) => Users.GetValueOrDefault(e.CashierUserId, $"user {e.CashierUserId}");

        public string Authoriser(PosEvent e) => e.AuthorisedByUserId is long id ? Users.GetValueOrDefault(id, $"user {id}") : "—";
    }

    private async Task<(List<PosEvent> Events, EventNames Names)> LoadEventsAsync(long companyId, DateOnly fromDate, DateOnly toDate, PosEventKind[] kinds, CancellationToken ct)
    {
        List<PosEvent> events = await _db.PosEvents.AsNoTracking().Where(e => e.CompanyId == companyId && kinds.Contains(e.Kind) && e.EventDate >= fromDate && e.EventDate <= toDate)
            .OrderBy(e => e.OccurredOnUtc).ThenBy(e => e.Id).ToListAsync(ct).ConfigureAwait(false);
        return (events, new EventNames(await TillsAsync(companyId, ct).ConfigureAwait(false), await UsersAsync(ct).ConfigureAwait(false)));
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> EventsAsync(long companyId, DateOnly fromDate, DateOnly toDate, PosEventKind[] kinds,
        Func<PosEvent, EventNames, IReadOnlyDictionary<string, object?>> row, CancellationToken ct)
    {
        (List<PosEvent> events, EventNames names) = await LoadEventsAsync(companyId, fromDate, toDate, kinds, ct).ConfigureAwait(false);
        return [.. events.Select(e => row(e, names))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> NoSaleAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        (List<PosEvent> events, EventNames names) = await LoadEventsAsync(companyId, fromDate, toDate, [PosEventKind.NoSale], ct).ConfigureAwait(false);
        Dictionary<long, int> perCashier = events.GroupBy(e => e.CashierUserId).ToDictionary(g => g.Key, g => g.Count());
        return [.. events.Select(e => Row(("Opened", Stamp(e.OccurredOnUtc)), ("Till", names.Till(e)), ("Cashier", names.Cashier(e)), ("Reason", e.Reason ?? string.Empty), ("AuthorisedBy", names.Authoriser(e)),
            ("CashierCount", perCashier[e.CashierUserId])))];
    }

    // A parked sale is resumed when a later ResumedSale event names the same sale number; otherwise it was abandoned.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ParkedAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        (List<PosEvent> parked, EventNames names) = await LoadEventsAsync(companyId, fromDate, toDate, [PosEventKind.ParkedSale], ct).ConfigureAwait(false);
        List<string> numbers = [.. parked.Where(e => e.SaleNumber != null).Select(e => e.SaleNumber!).Distinct()];
        List<PosEvent> resumed = await _db.PosEvents.AsNoTracking().Where(e => e.CompanyId == companyId && e.Kind == PosEventKind.ResumedSale && numbers.Contains(e.SaleNumber!))
            .ToListAsync(ct).ConfigureAwait(false);
        return [.. parked.Select(e =>
        {
            PosEvent? back = resumed.Where(r => r.SaleNumber == e.SaleNumber && r.OccurredOnUtc >= e.OccurredOnUtc).OrderBy(r => r.OccurredOnUtc).FirstOrDefault();
            return Row(("Parked", Stamp(e.OccurredOnUtc)), ("Till", names.Till(e)), ("Cashier", names.Cashier(e)), ("Sale", e.SaleNumber ?? "—"), ("Status", back is null ? "Abandoned" : "Resumed"),
                ("Resumed", back is null ? "—" : Stamp(back.OccurredOnUtc)), ("Waited", back is null ? "—" : $"{(int)(back.OccurredOnUtc - e.OccurredOnUtc).TotalMinutes} min"), ("Amount", e.Amount));
        })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> LoyaltyAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        List<PosEvent> events = await _db.PosEvents.AsNoTracking()
            .Where(e => e.CompanyId == companyId && (e.Kind == PosEventKind.LoyaltyEarned || e.Kind == PosEventKind.LoyaltyRedeemed) && e.EventDate <= toDate && e.CustomerReference != null)
            .ToListAsync(ct).ConfigureAwait(false);
        return [.. events.GroupBy(e => e.CustomerReference!).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g =>
        {
            List<PosEvent> inPeriod = [.. g.Where(e => e.EventDate >= fromDate)];
            var period = PosEventRules.Loyalty(inPeriod.Select(e => (e.Kind, e.Amount)));
            var toDateTotals = PosEventRules.Loyalty(g.Select(e => (e.Kind, e.Amount)));
            return Row(("Customer", g.Key), ("Transactions", inPeriod.Count), ("Earned", Qty(period.Earned)), ("Redeemed", Qty(period.Redeemed)), ("LastActivity", Date(g.Max(e => e.EventDate))),
                ("Balance", toDateTotals.Balance));
        })];
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

    // One row per session and tender (a session with no sales still shows, with its float).
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ZReadAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        DateTimeOffset start = new(fromDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), end = new(toDate.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
        List<Core.Pos.TillSession> sessions = await _db.TillSessions.AsNoTracking().Where(s => s.CompanyId == companyId && s.OpenedOnUtc >= start && s.OpenedOnUtc <= end)
            .OrderBy(s => s.OpenedOnUtc).ToListAsync(ct).ConfigureAwait(false);
        List<long> ids = [.. sessions.Select(s => s.Id)];
        List<Core.Pos.Sale> sales = await _db.Sales.AsNoTracking().Include(s => s.Tenders).Where(s => ids.Contains(s.TillSessionId) && s.Status == Core.Pos.SaleStatus.Completed)
            .ToListAsync(ct).ConfigureAwait(false);
        List<long> terminals = [.. sessions.Select(s => s.TerminalId).Distinct()], cashiers = [.. sessions.Select(s => s.CashierUserId).Distinct()];
        Dictionary<long, string> tills = await _db.PosTerminals.AsNoTracking().Where(t => terminals.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Code, ct).ConfigureAwait(false);
        Dictionary<long, string> users = await _db.Users.AsNoTracking().Where(u => cashiers.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Username, ct).ConfigureAwait(false);
        List<IReadOnlyDictionary<string, object?>> rows = [];
        foreach (Core.Pos.TillSession session in sessions)
        {
            List<Core.Pos.Sale> mine = [.. sales.Where(s => s.TillSessionId == session.Id)];
            var tenders = mine.SelectMany(s => s.Tenders).GroupBy(t => (t.TenderType, Currency: t.Currency.Value))
                .Select(g => new { g.Key.TenderType, g.Key.Currency, Amount = g.Sum(t => t.IsChange ? -t.Amount : t.Amount), Base = g.Sum(t => t.IsChange ? -t.BaseAmount : t.BaseAmount) })
                .OrderBy(t => t.TenderType).ThenBy(t => t.Currency).ToList();
            string read = session.ZReadNumber is int z ? $"Z{z}" : "X (open)";
            IReadOnlyDictionary<string, object?> Line(string tender, string currency, decimal amount, decimal baseAmount) => Row(("Till", tills.GetValueOrDefault(session.TerminalId, "?")),
                ("Cashier", users.GetValueOrDefault(session.CashierUserId, "?")), ("Opened", session.OpenedOnUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)),
                ("Closed", session.ClosedOnUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "—"), ("Read", read), ("Float", session.OpeningFloat), ("Sales", mine.Count),
                ("Tender", tender), ("Currency", currency), ("Amount", amount), ("Base", baseAmount));
            if (tenders.Count == 0)
            {
                rows.Add(Line("—", string.Empty, 0m, 0m));
            }

            rows.AddRange(tenders.Select(t => Line(t.TenderType.ToString(), t.Currency, t.Amount, t.Base)));
        }

        return rows;
    }
}
