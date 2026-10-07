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
    public RetailReportDataProvider(One4AllDbContext db, Application.Abstractions.Pos.IPosReceiptService? receipts = null,
        Application.Abstractions.Accounting.IFiscalDeviceProvider? fiscal = null)
    {
        _db = db;
        _receipts = receipts;
        _fiscal = fiscal;
    }

    private readonly Application.Abstractions.Pos.IPosReceiptService? _receipts;
    private readonly Application.Abstractions.Accounting.IFiscalDeviceProvider? _fiscal;

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
            "RETLTABS" => await TabsAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "RETLSLIP" => await SlipAsync(companyId, SaleNumber(parameters), parameters.TryGetValue("Paper", out object? paper) ? paper as string : null, ct).ConfigureAwait(false),
            "RETLTAXINVOICE" => await TaxInvoiceAsync(companyId, SaleNumber(parameters), ct).ConfigureAwait(false),
            "RETLKITCHEN" => await KitchenAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
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

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> TabsAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        List<Core.Restaurant.TableOrder> orders = await _db.TableOrders.AsNoTracking().Include(o => o.Lines)
            .Where(o => o.CompanyId == companyId && o.OpenedOn >= fromDate && o.OpenedOn <= toDate && o.Status != Core.Restaurant.TableOrderStatus.Cancelled).ToListAsync(ct).ConfigureAwait(false);
        List<long> ids = [.. orders.Select(o => o.Id)], waiters = [.. orders.Select(o => o.WaiterUserId).Distinct()];
        List<Core.Restaurant.RestaurantBill> bills = await _db.RestaurantBills.AsNoTracking().Where(b => ids.Contains(b.TableOrderId) && !b.IsReversed).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, string> tables = await _db.RestaurantTables.AsNoTracking().IgnoreQueryFilters().Where(t => t.CompanyId == companyId).ToDictionaryAsync(t => t.Id, t => t.TableNumber, ct).ConfigureAwait(false);
        Dictionary<long, string> users = await _db.Users.AsNoTracking().Where(u => waiters.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Username, ct).ConfigureAwait(false);
        string Waiter(long id) => users.GetValueOrDefault(id, $"user {id}");
        static decimal Spend(Core.Restaurant.TableOrder o) => o.Lines.Where(l => !l.IsVoided && !l.IsComped).Sum(l => Math.Round((l.Quantity * l.UnitPrice) + l.TaxAmount, 2, MidpointRounding.AwayFromZero));
        List<IReadOnlyDictionary<string, object?>> rows = [.. orders.Where(o => o.Status == Core.Restaurant.TableOrderStatus.Open).OrderBy(o => o.CreatedOnUtc)
            .Select(o => Row(("Section", "Open tab"), ("Waiter", Waiter(o.WaiterUserId)), ("Tab", o.TableId is long t ? $"{o.OrderNumber} @ {tables.GetValueOrDefault(t, "?")}" : o.OrderNumber),
                ("Opened", o.CreatedOnUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)), ("Tabs", o.PartySize), ("Minutes", string.Empty), ("Spend", Spend(o)), ("Tips", 0m)))];
        rows.AddRange(orders.GroupBy(o => o.WaiterUserId).OrderBy(g => Waiter(g.Key), StringComparer.Ordinal).Select(g =>
        {
            List<(Core.Restaurant.TableOrder Order, Core.Restaurant.RestaurantBill Bill)> closed = [.. g.Join(bills, o => o.Id, b => b.TableOrderId, (o, b) => (o, b))];
            double minutes = closed.Count == 0 ? 0 : closed.Average(x => Math.Max(0, (x.Bill.CreatedOnUtc - x.Order.CreatedOnUtc).TotalMinutes));
            return Row(("Section", "Waiter"), ("Waiter", Waiter(g.Key)), ("Tab", $"{g.Count(o => o.Status == Core.Restaurant.TableOrderStatus.Open)} still open"), ("Opened", string.Empty),
                ("Tabs", g.Count()), ("Minutes", closed.Count == 0 ? "—" : Math.Round(minutes).ToString(CultureInfo.InvariantCulture)),
                ("Spend", closed.Count == 0 ? 0m : Math.Round(closed.Average(x => x.Bill.Gross - x.Bill.Tip), 2, MidpointRounding.AwayFromZero)), ("Tips", Math.Round(closed.Sum(x => x.Bill.Tip), 2, MidpointRounding.AwayFromZero)));
        }));
        return rows;
    }

    // Lines fired to the kitchen and later voided are the "void after print" exposure.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> KitchenAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        List<Core.Restaurant.KitchenTicket> tickets = await _db.KitchenTickets.AsNoTracking().Include(t => t.Lines)
            .Where(t => t.CompanyId == companyId && t.SentOn >= fromDate && t.SentOn <= toDate).ToListAsync(ct).ConfigureAwait(false);
        List<long> orderIds = [.. tickets.Select(t => t.TableOrderId).Distinct()];
        List<Core.Restaurant.TableOrder> orders = await _db.TableOrders.AsNoTracking().Include(o => o.Lines).Where(o => orderIds.Contains(o.Id)).ToListAsync(ct).ConfigureAwait(false);
        string Course(Core.Restaurant.MenuCourse? course) => course?.ToString() ?? "All courses";

        // Each voided line counts against the ticket that carried it: the order's earliest ticket for its course (or for all courses).
        var voids = orders.SelectMany(o => o.Lines.Where(l => l.Fired && l.IsVoided).Select(l =>
        {
            Core.Restaurant.KitchenTicket? ticket = tickets.Where(t => t.TableOrderId == o.Id && (t.Course == null || t.Course == l.Course)).OrderBy(t => t.SentOn).ThenBy(t => t.Id).FirstOrDefault();
            return new
            {
                Date = ticket?.SentOn ?? o.OpenedOn, Course = ticket is null ? l.Course.ToString() : Course(ticket.Course), l.Quantity, l.VoidReason,
                Value = Math.Round((l.Quantity * l.UnitPrice) + l.TaxAmount, 2, MidpointRounding.AwayFromZero),
            };
        })).ToList();
        var keys = tickets.Select(t => (t.SentOn, Course: Course(t.Course))).Concat(voids.Select(v => (SentOn: v.Date, v.Course))).Distinct()
            .OrderBy(k => k.SentOn).ThenBy(k => k.Course, StringComparer.Ordinal);
        static string Count(decimal quantity) => quantity.ToString("0.###", CultureInfo.InvariantCulture);
        return [.. keys.Select(k =>
        {
            List<Core.Restaurant.KitchenTicket> mine = [.. tickets.Where(t => t.SentOn == k.SentOn && Course(t.Course) == k.Course)];
            var voided = voids.Where(v => v.Date == k.SentOn && v.Course == k.Course).ToList();
            return Row(("Date", Date(k.SentOn)), ("Course", k.Course), ("Tickets", mine.Count), ("Items", Count(mine.Sum(t => t.Lines.Sum(l => l.Quantity)))),
                ("Served", mine.Count(t => t.Status == Core.Restaurant.KitchenTicketStatus.Served)), ("Voided", Count(voided.Sum(v => v.Quantity))),
                ("Reasons", string.Join("; ", voided.Select(v => v.VoidReason).Where(r => !string.IsNullOrWhiteSpace(r)).Distinct())), ("VoidValue", voided.Sum(v => v.Value)));
        })];
    }

    private static string? SaleNumber(IReadOnlyDictionary<string, object?> parameters)
        => parameters.TryGetValue("SaleNumber", out object? value) && value is string text && text.Trim().Length > 0 ? text.Trim() : null;

    /// <summary>Characters per line for each paper: 58 mm and 80 mm roll printers at their usual font, and an A4 page.</summary>
    internal static int PaperWidth(string? paper) => (paper ?? "80mm").Trim().ToUpperInvariant().Replace(" ", string.Empty, StringComparison.Ordinal) switch
    {
        "58MM" or "58" => 32,
        "A4" => 96,
        _ => 48,
    };

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> SlipAsync(long companyId, string? saleNumber, string? paper, CancellationToken ct)
    {
        Core.Pos.Sale? sale = saleNumber is null || _receipts is null ? null
            : await _db.Sales.AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == companyId && s.SaleNumber == saleNumber, ct).ConfigureAwait(false);
        if (sale is null)
        {
            return [];
        }

        int width = PaperWidth(paper);
        Core.Common.Result<string> text = await _receipts!.RenderReceiptTextAsync(companyId, sale.Id, width, ct).ConfigureAwait(false);
        if (text.IsFailure)
        {
            return [];
        }

        List<string> lines = [.. text.Value.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n')];
        List<Core.Pos.PosEvent> refunds = await _db.PosEvents.AsNoTracking().Where(e => e.CompanyId == companyId && e.SaleNumber == sale.SaleNumber && e.Kind == Core.Pos.PosEventKind.Refund)
            .OrderBy(e => e.OccurredOnUtc).ToListAsync(ct).ConfigureAwait(false);
        if (refunds.Count > 0)
        {
            lines.Add(new string('=', width));
            lines.Add("REFUND SLIP".PadLeft((width + 11) / 2));
            foreach (Core.Pos.PosEvent refund in refunds)
            {
                string left = $"{refund.EventDate:yyyy-MM-dd} {refund.Description}", right = refund.Quantity is decimal q ? $"x{q.ToString("0.###", CultureInfo.InvariantCulture)}" : string.Empty;
                left = left.Length + right.Length + 1 > width ? left[..Math.Max(0, width - right.Length - 1)] : left;
                lines.Add(left + new string(' ', Math.Max(1, width - left.Length - right.Length)) + right);
            }
        }

        return [.. lines.Select((l, i) => Row(("Line", i + 1), ("Text", l)))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> TaxInvoiceAsync(long companyId, string? saleNumber, CancellationToken ct)
    {
        Core.Pos.Sale? sale = saleNumber is null ? null : await _db.Sales.AsNoTracking().Include(s => s.Lines).Include(s => s.Tenders)
            .FirstOrDefaultAsync(s => s.CompanyId == companyId && s.SaleNumber == saleNumber, ct).ConfigureAwait(false);
        if (sale is null)
        {
            return [];
        }

        Core.System.Company company = await _db.Companies.AsNoTracking().FirstAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        List<long> itemIds = [.. sale.Lines.Select(l => l.ItemId).Distinct()];
        Dictionary<long, string?> taxCodes = await _db.Items.AsNoTracking().IgnoreQueryFilters().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.TaxCode, ct).ConfigureAwait(false);
        int sameNumber = await _db.Sales.AsNoTracking().CountAsync(s => s.CompanyId == companyId && s.SaleNumber == sale.SaleNumber, ct).ConfigureAwait(false);
        decimal net = sale.Lines.Sum(l => l.LineNet), tax = sale.Lines.Sum(l => l.TaxAmount), gross = net + tax;
        decimal tendered = sale.Tenders.Sum(t => t.IsChange ? -t.BaseAmount : t.BaseAmount);
        static string M(decimal amount) => amount.ToString("N2", CultureInfo.InvariantCulture);
        static IReadOnlyDictionary<string, object?> R(string section, string item, string value, string check = "") => Row(("Section", section), ("Item", item), ("Value", value), ("Check", check));

        List<IReadOnlyDictionary<string, object?>> rows =
        [
            R("Header", "Document", tax == 0m ? "RECEIPT" : "TAX INVOICE / FISCAL RECEIPT"),
            R("Header", "Supplier", company.Name),
            R("Header", "Supplier tax number", company.TaxNumber ?? "(none)"),
            R("Header", "Number", sale.SaleNumber),
            R("Header", "Date and time", $"{sale.SaleDate:yyyy-MM-dd} {sale.CreatedOnUtc.ToLocalTime():HH:mm}"),
            R("Header", "Buyer", "Cash sale (buyer details not captured at the till)"),
        ];
        rows.AddRange(sale.Lines.Select(l => R("Line", $"{l.Description} — {l.Quantity.ToString("0.###", CultureInfo.InvariantCulture)} × {M(l.UnitPrice)}",
            $"net {M(l.LineNet)}, {taxCodes.GetValueOrDefault(l.ItemId) ?? "no tax code"} {M(l.TaxAmount)}, gross {M(l.LineNet + l.TaxAmount)}")));
        rows.AddRange(sale.Lines.GroupBy(l => taxCodes.GetValueOrDefault(l.ItemId) ?? "(none)").OrderBy(g => g.Key, StringComparer.Ordinal).Select(g =>
        {
            decimal n = g.Sum(l => l.LineNet), t = g.Sum(l => l.TaxAmount);
            return R("Tax summary", g.Key, $"net {M(n)}, tax {M(t)}" + (n == 0m ? string.Empty : $" ({(t * 100m / n).ToString("0.##", CultureInfo.InvariantCulture)}%)"));
        }));
        rows.Add(R("Totals", "Net / tax / gross", $"{M(net)} / {M(tax)} / {M(gross)} {sale.BaseCurrency.Value}"));
        rows.Add(R("Totals", "Tendered", M(tendered)));
        rows.Add(R("Fiscal", "Fiscal device", _fiscal is { IsEnabled: true } ? "enabled — the receipt was fiscalised at the sale" : "not enabled"));

        string Ok(bool pass) => pass ? "PASS" : "FAIL";
        rows.Add(R("Compliance", "Supplier tax number shown", company.TaxNumber ?? "missing", Ok(!string.IsNullOrWhiteSpace(company.TaxNumber))));
        rows.Add(R("Compliance", "Document number unique", $"{sameNumber} sale(s) with this number", Ok(sameNumber == 1)));
        rows.Add(R("Compliance", "Every line has a tax code", $"{sale.Lines.Count(l => taxCodes.GetValueOrDefault(l.ItemId) is null)} line(s) without", Ok(sale.Lines.All(l => taxCodes.GetValueOrDefault(l.ItemId) is not null))));
        rows.Add(R("Compliance", "Gross = net + tax", $"{M(net)} + {M(tax)} = {M(gross)}", Ok(true)));
        rows.Add(R("Compliance", "Payment covers the total", $"{M(tendered)} against {M(gross + sale.CashRounding)}", Ok(tendered >= gross + sale.CashRounding - 0.01m)));
        rows.Add(R("Compliance", "Sale completed (not voided or parked)", sale.Status.ToString(), Ok(sale.Status == Core.Pos.SaleStatus.Completed)));
        return rows;
    }
}
