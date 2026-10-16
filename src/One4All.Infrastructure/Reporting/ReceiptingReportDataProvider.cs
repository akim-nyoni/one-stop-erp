using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Receipting;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Reporting;

/// <summary>
/// Supplies live rows for the Cashiering &amp; Receipting (<c>RC</c>) reports. A representative set is backed
/// by real queries today (receipt day book, suspense register, deposits held, agent reconciliation,
/// stationery control, import batches); the remaining RC commands return no rows so their layout still
/// renders until a dedicated query is wired.
/// </summary>
public sealed class ReceiptingReportDataProvider : IReportDataProvider
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the provider.</summary>
    /// <param name="db">The database context.</param>
    public ReceiptingReportDataProvider(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public bool CanHandle(string command) => command.StartsWith("RC", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetRowsAsync(long companyId, string command, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct = default)
    {
        return command.ToUpperInvariant() switch
        {
            "RC3" => await DailyCollectionSummaryAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC4" => await ReceiptDayBookAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC5" => await CollectionByVoteAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC6" => await CollectionByServiceAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC7" => await CollectionByTenderAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC8" => await CollectionByCurrencyAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC9" => await CollectionByWardAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC10" => await HourlyVolumeAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC11" => await CashierProductivityAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC12" => await GlReconciliationAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC13" => await CashControlReconciliationAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC14" => await TillVarianceAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC15" => await CancelledReceiptsAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC16" => await ReprintLogAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC17" => await GapAnalysisAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC18" => await BackDatedReceiptsAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC20" => await StationeryControlAsync(companyId, ct).ConfigureAwait(false),
            "RC21" => await AuditTrailAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC22" => await SodViolationsAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC23" => await RelatedPartyAttemptsAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC25" => await DailyBankingSummaryAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC26" => await CashInTransitAgeingAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC27" => await UnbankedCollectionsAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC28" => await CashMovementsAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC29" => await SuspenseRegisterAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC30" => await SuspenseAllocationLogAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC31" => await ImportBatchesAsync(companyId, ct).ConfigureAwait(false),
            "RC32" => await ImportExceptionsAsync(companyId, ct).ConfigureAwait(false),
            "RC33" => await PendingTendersAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC34" => await DishonoursAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC38" => await PaymentHistoryAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC39" => await CreditBalancesAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC40" => await DepositsHeldAsync(companyId, ct).ConfigureAwait(false),
            "RC41" => await RefundsRegisterAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC42" => await ArrangementsAsync(companyId, ct).ConfigureAwait(false),
            "RC43" => await TopPayersAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC44" => await DormantAccountsAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC45" => await AgentReconciliationAsync(companyId, ct).ConfigureAwait(false),
            "RC46" => await AgentAgeingAsync(companyId, ct).ConfigureAwait(false),
            "RC47" => await FieldCollectorReconciliationAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC48" => await RoutePerformanceAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC19" => await AllocationOverridesAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC49" => await CollectionEfficiencyAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC50" => await CollectionVsBudgetAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC51" => await CollectionTrendAsync(companyId, parameters, ct).ConfigureAwait(false),
            "RC52" => await PrepaidTokensAsync(companyId, parameters, ct).ConfigureAwait(false),
            _ => [],
        };
    }

    // RC19: receipts the cashier allocated against the default (oldest first), with how, why and to whom.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> AllocationOverridesAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        DateOnly from = ReportParameters.TryDate(parameters, "FromDate") ?? DateOnly.MinValue, to = ReportParameters.TryDate(parameters, "ToDate") ?? DateOnly.MaxValue;
        var receipts = await _db.Set<Receipt>().AsNoTracking().Include(r => r.Lines)
            .Where(r => r.CompanyId == companyId && r.AllocationOverride != null && r.Date >= from && r.Date <= to)
            .OrderBy(r => r.Date).ThenBy(r => r.ReceiptNumber).ToListAsync(ct).ConfigureAwait(false);
        List<long> payers = [.. receipts.Where(r => r.PayerAccountId != null).Select(r => r.PayerAccountId!.Value).Distinct()];
        Dictionary<long, string> names = await _db.Customers.AsNoTracking().Where(c => payers.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Code + " — " + c.Name, ct).ConfigureAwait(false);
        return [.. receipts.Select(r => Row(new()
        {
            ["ReceiptNumber"] = r.ReceiptNumber,
            ["Account"] = r.PayerAccountId is long id ? names.GetValueOrDefault(id, id.ToString(System.Globalization.CultureInfo.InvariantCulture)) : "-",
            ["Reason"] = $"{r.AllocationOverride}: {r.AllocationOverrideReason}",
            ["Amount"] = r.Lines.Where(l => l.Type == ReceiptLineType.DebtorPayment).Sum(l => l.Amount),
        }))];
    }

    // RC47: each field collector's day — receipts issued, their value, and the cash-up variance of the session (0 while it is still open).
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> FieldCollectorReconciliationAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        DateOnly date = ReportParameters.Date(parameters, "Date");
        List<CashierStation> collectors = await _db.CashierStations.AsNoTracking().Where(s => s.CompanyId == companyId && s.IsFieldCollector).OrderBy(s => s.Code).ToListAsync(ct).ConfigureAwait(false);
        List<long> ids = [.. collectors.Select(c => c.Id)];
        List<CashierSession> sessions = await _db.CashierSessions.AsNoTracking().Where(s => s.CompanyId == companyId && ids.Contains(s.StationId) && s.OpenedOn == date).ToListAsync(ct).ConfigureAwait(false);
        List<Receipt> receipts = (await CommittedReceiptsAsync(companyId, date, date, ct).ConfigureAwait(false)).Where(r => ids.Contains(r.StationId)).ToList();
        return [.. collectors.Select(c =>
        {
            List<Receipt> issued = [.. receipts.Where(r => r.StationId == c.Id)];
            List<CashierSession> theirs = [.. sessions.Where(s => s.StationId == c.Id)];
            string state = theirs.Count == 0 ? "no session" : theirs.All(s => s.ClosedOn != null) ? "returned" : "out";
            return Row(new()
            {
                ["Collector"] = $"{c.Code} — {c.Name}" + (c.RouteCode is null ? string.Empty : $" ({c.RouteCode})") + $" · {state}",
                ["Receipts"] = issued.Count,
                ["Value"] = issued.Sum(r => r.TenderTotal),
                ["Variance"] = theirs.Sum(s => s.CashUpVarianceBase),
            });
        })];
    }

    // RC48: collections by route over the period (receipts of the field collectors assigned to each route).
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> RoutePerformanceAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        (DateOnly from, DateOnly to) = Range(parameters);
        Dictionary<long, string> routes = await _db.CashierStations.AsNoTracking().Where(s => s.CompanyId == companyId && s.IsFieldCollector)
            .ToDictionaryAsync(s => s.Id, s => s.RouteCode ?? "(no route)", ct).ConfigureAwait(false);
        List<Receipt> receipts = (await CommittedReceiptsAsync(companyId, from, to, ct).ConfigureAwait(false)).Where(r => routes.ContainsKey(r.StationId)).ToList();
        return [.. receipts.GroupBy(r => routes[r.StationId]).OrderByDescending(g => g.Sum(r => r.TenderTotal))
            .Select(g => Row(new() { ["Route"] = g.Key, ["Collections"] = g.Count(), ["Amount"] = g.Sum(r => r.TenderTotal) }))];
    }

    // RC50: what was collected against each income vote (sundry and deposit receipt lines coded to it) against the in-force revenue budget for the
    // fiscal periods the dates fall in; a vote with a budget but nothing collected still shows, with its shortfall.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> CollectionVsBudgetAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        (DateOnly from, DateOnly to) = Range(parameters);
        Dictionary<string, string> names = await AccountNamesAsync(companyId, ct).ConfigureAwait(false);
        HashSet<string> income = [.. await _db.Accounts.Where(a => a.CompanyId == companyId && a.Type == Core.Accounting.AccountType.Income).Select(a => a.AccountCode).ToListAsync(ct).ConfigureAwait(false)];

        Dictionary<string, decimal> actual = (await CommittedReceiptsAsync(companyId, from, to, ct).ConfigureAwait(false)).SelectMany(r => r.Lines)
            .Where(l => l.Type != ReceiptLineType.DebtorPayment && l.AccountCode is not null && income.Contains(l.AccountCode))
            .GroupBy(l => l.AccountCode!).ToDictionary(g => g.Key, g => g.Sum(l => l.Amount));

        // The fiscal periods the range covers, year by year, then the in-force budgets' income lines for those periods.
        Dictionary<string, decimal> budget = new(StringComparer.Ordinal);
        List<Core.System.FiscalYear> years = await _db.FiscalYears.Include(y => y.Periods).Where(y => y.CompanyId == companyId && y.StartDate <= to && y.EndDate >= from).ToListAsync(ct).ConfigureAwait(false);
        foreach (Core.System.FiscalYear year in years)
        {
            HashSet<int> periods = [.. year.Periods.Where(p => p.StartDate <= to && p.EndDate >= from).Select(p => p.PeriodNumber)];
            List<Core.Budgeting.Budget> inForce = await _db.Budgets.Include(b => b.Lines)
                .Where(b => b.CompanyId == companyId && b.FiscalYearId == year.Id && b.IsActive && b.Type != Core.Budgeting.BudgetType.DraftSubmission
                    && (b.Status == Core.Budgeting.BudgetStatus.Approved || b.Status == Core.Budgeting.BudgetStatus.Active)).ToListAsync(ct).ConfigureAwait(false);
            foreach (Core.Budgeting.BudgetLine line in inForce.SelectMany(b => b.Lines).Where(l => periods.Contains(l.Period) && income.Contains(l.AccountCode)))
            {
                budget[line.AccountCode] = budget.GetValueOrDefault(line.AccountCode) + line.Amount;
            }
        }

        return [.. budget.Keys.Union(actual.Keys).OrderBy(code => code, StringComparer.Ordinal).Select(code => Row(new()
        {
            ["Vote"] = $"{code} — {names.GetValueOrDefault(code, string.Empty)}",
            ["Budget"] = budget.GetValueOrDefault(code),
            ["Actual"] = actual.GetValueOrDefault(code),
            ["Variance"] = actual.GetValueOrDefault(code) - budget.GetValueOrDefault(code),
        }))];
    }

    private async Task<List<Receipt>> CommittedReceiptsAsync(long companyId, DateOnly from, DateOnly to, CancellationToken ct) =>
        await _db.Set<Receipt>().Include(r => r.Lines).Include(r => r.Tenders)
            .Where(r => r.CompanyId == companyId && r.Status == ReceiptStatus.Committed && r.Date >= from && r.Date <= to)
            .ToListAsync(ct).ConfigureAwait(false);

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> DailyCollectionSummaryAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        DateOnly date = ReportParameters.Date(parameters, "Date");
        List<Receipt> receipts = await CommittedReceiptsAsync(companyId, date, date, ct).ConfigureAwait(false);
        return receipts.SelectMany(r => r.Tenders)
            .GroupBy(t => t.Type)
            .OrderBy(g => g.Key.ToString())
            .Select(g => Row(new() { ["Category"] = g.Key.ToString(), ["Count"] = g.Count(), ["Amount"] = g.Sum(t => t.Amount) }))
            .ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> CollectionByVoteAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        (DateOnly from, DateOnly to) = Range(parameters);
        List<Receipt> receipts = await CommittedReceiptsAsync(companyId, from, to, ct).ConfigureAwait(false);
        Dictionary<string, string> names = await AccountNamesAsync(companyId, ct).ConfigureAwait(false);
        return receipts.SelectMany(r => r.Lines)
            .Where(l => l.Type == ReceiptLineType.Sundry && l.AccountCode is not null)
            .GroupBy(l => l.AccountCode!)
            .OrderBy(g => g.Key)
            .Select(g => Row(new() { ["AccountCode"] = g.Key, ["AccountName"] = names.GetValueOrDefault(g.Key, string.Empty), ["Amount"] = g.Sum(l => l.Amount) }))
            .ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> CollectionByServiceAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        (DateOnly from, DateOnly to) = Range(parameters);
        List<Receipt> receipts = await CommittedReceiptsAsync(companyId, from, to, ct).ConfigureAwait(false);
        return receipts.SelectMany(r => r.Lines)
            .Where(l => !string.IsNullOrWhiteSpace(l.Service))
            .GroupBy(l => l.Service!)
            .OrderBy(g => g.Key)
            .Select(g => Row(new() { ["Service"] = g.Key, ["Amount"] = g.Sum(l => l.Amount) }))
            .ToList();
    }

    // Collections by the payer's ward (their first property); money from payers with no property shows apart.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> CollectionByWardAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        (DateOnly from, DateOnly to) = Range(parameters);
        return [.. (await CollectionAnalysis.ComputeAsync(_db, companyId, from, to, ct).ConfigureAwait(false)).Where(c => c.Collected != 0m)
            .GroupBy(c => c.Ward).OrderBy(g => g.Key == CollectionAnalysis.NoWard).ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => Row(new() { ["Area"] = g.Key, ["Amount"] = g.Sum(c => c.Collected) }))];
    }

    // Billed against collected per service over the period.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> CollectionEfficiencyAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        (DateOnly from, DateOnly to) = Range(parameters);
        return [.. (await CollectionAnalysis.ComputeAsync(_db, companyId, from, to, ct).ConfigureAwait(false))
            .GroupBy(c => c.Service).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => (Service: g.Key, Billed: g.Sum(c => c.Billed), Collected: g.Sum(c => c.Collected)))
            .Select(s => Row(new()
            {
                ["Service"] = s.Service, ["Billed"] = s.Billed, ["Collected"] = s.Collected,
                ["Percent"] = s.Billed == 0m ? "—" : (s.Collected * 100m / s.Billed).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "%",
            }))];
    }

    // Receipts refused because the account is the cashier's own or related (logged by the receipting service).
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> RelatedPartyAttemptsAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        (DateOnly from, DateOnly to) = Range(parameters);
        DateTimeOffset start = new(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), end = new(to.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
        var attempts = await _db.ChangeLog.AsNoTracking().Where(c => c.CompanyId == companyId && c.EntityType == "RelatedAccountReceipt" && c.OccurredOnUtc >= start && c.OccurredOnUtc <= end)
            .OrderBy(c => c.Id).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, string> users = await _db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Username, ct).ConfigureAwait(false);
        List<long> ids = [.. attempts.Select(a => long.TryParse(a.EntityId, out long id) ? id : 0L).Distinct()];
        Dictionary<long, string> accounts = await _db.Customers.AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Code + " " + c.Name, ct).ConfigureAwait(false);
        return [.. attempts.Select(a => Row(new()
        {
            ["Cashier"] = a.UserId is long u ? users.GetValueOrDefault(u, $"user {u}") : "?",
            ["Account"] = long.TryParse(a.EntityId, out long id) ? accounts.GetValueOrDefault(id, a.EntityId!) : a.EntityId,
            ["Outcome"] = $"Refused ({a.Reason})",
            ["Amount"] = decimal.TryParse(a.NewValues, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out decimal amount) ? amount : 0m,
        }))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> CollectionByTenderAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        (DateOnly from, DateOnly to) = Range(parameters);
        List<Receipt> receipts = await CommittedReceiptsAsync(companyId, from, to, ct).ConfigureAwait(false);
        return receipts.SelectMany(r => r.Tenders)
            .GroupBy(t => t.Type)
            .OrderBy(g => g.Key.ToString())
            .Select(g => Row(new() { ["Tender"] = g.Key.ToString(), ["Channel"] = Channel(g.Key), ["Amount"] = g.Sum(t => t.Amount) }))
            .ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> CollectionByCurrencyAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        (DateOnly from, DateOnly to) = Range(parameters);
        List<Receipt> receipts = await CommittedReceiptsAsync(companyId, from, to, ct).ConfigureAwait(false);
        return receipts.SelectMany(r => r.Tenders)
            .GroupBy(t => t.Currency.ToString())
            .OrderBy(g => g.Key)
            .Select(g => Row(new() { ["Currency"] = g.Key, ["Transaction"] = g.Sum(t => t.Amount), ["Rate"] = string.Empty, ["BaseAmount"] = g.Sum(t => t.Amount) }))
            .ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> HourlyVolumeAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        DateOnly date = ReportParameters.Date(parameters, "Date");
        List<Receipt> receipts = await CommittedReceiptsAsync(companyId, date, date, ct).ConfigureAwait(false);
        Dictionary<long, string> stations = await _db.Set<CashierStation>()
            .Where(s => s.CompanyId == companyId)
            .ToDictionaryAsync(s => s.Id, s => s.Code, ct).ConfigureAwait(false);

        // Hour comes from the capture timestamp (receipts carry a business Date only); the handling time is from the till's start stamp
        // (the payer chosen or the first amount keyed) to the receipt's capture, averaged over the receipts that carry one.
        return receipts
            .GroupBy(r => new { Hour = r.CreatedOnUtc.UtcDateTime.Hour, r.StationId })
            .OrderBy(g => g.Key.Hour).ThenBy(g => g.Key.StationId)
            .Select(g => Row(new()
            {
                ["Hour"] = $"{g.Key.Hour:D2}:00",
                ["Station"] = stations.GetValueOrDefault(g.Key.StationId, g.Key.StationId.ToString()),
                ["Transactions"] = g.Count(),
                ["AvgSeconds"] = AverageHandlingSeconds(g),
            }))
            .ToList();
    }

    /// <summary>The average seconds from the till's start stamp to capture over the receipts that carry one (null when none do).</summary>
    /// <param name="receipts">The receipts.</param>
    /// <returns>The average, rounded, or null.</returns>
    public static int? AverageHandlingSeconds(IEnumerable<Receipt> receipts)
    {
        List<double> timed = [.. receipts.Where(r => r.StartedOnUtc is not null && r.CreatedOnUtc >= r.StartedOnUtc).Select(r => (r.CreatedOnUtc - r.StartedOnUtc!.Value).TotalSeconds)];
        return timed.Count == 0 ? null : (int)Math.Round(timed.Average());
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> CashierProductivityAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        (DateOnly from, DateOnly to) = Range(parameters);
        List<Receipt> receipts = await CommittedReceiptsAsync(companyId, from, to, ct).ConfigureAwait(false);
        Dictionary<long, int> variances = (await _db.Set<CashierSession>()
                .Where(s => s.CompanyId == companyId && s.CashUpVarianceBase != 0m)
                .Select(s => s.CashierUserId)
                .ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(id => id).ToDictionary(g => g.Key, g => g.Count());

        return receipts.GroupBy(r => r.CashierUserId)
            .OrderBy(g => g.Key)
            .Select(g => Row(new() { ["Cashier"] = $"#{g.Key}", ["Receipts"] = g.Count(), ["Value"] = g.Sum(r => r.LineTotal), ["Variances"] = variances.GetValueOrDefault(g.Key) }))
            .ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GlReconciliationAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        (DateOnly from, DateOnly to) = Range(parameters);
        List<Receipt> receipts = await CommittedReceiptsAsync(companyId, from, to, ct).ConfigureAwait(false);
        decimal Sum(ReceiptLineType type) => receipts.SelectMany(r => r.Lines).Where(l => l.Type == type).Sum(l => l.Amount);
        return
        [
            Row(new() { ["Component"] = "Debtor payments (Cr debtor control)", ["Amount"] = Sum(ReceiptLineType.DebtorPayment) }),
            Row(new() { ["Component"] = "Sundry income", ["Amount"] = Sum(ReceiptLineType.Sundry) }),
            Row(new() { ["Component"] = "Deposits (liability)", ["Amount"] = Sum(ReceiptLineType.Deposit) }),
            Row(new() { ["Component"] = "Suspense (unallocated)", ["Amount"] = Sum(ReceiptLineType.Suspense) }),
        ];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> CashControlReconciliationAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        (string Param, string Label)[] stages =
        [
            ("CashAccount", "Cash on hand"),
            ("SafeAccount", "Cash in safe"),
            ("TransitAccount", "Cash in transit"),
            ("BankAccount", "Bank"),
        ];

        // Sum GL period-balance movement per supplied cash account (debit/credit, base currency).
        Dictionary<string, (decimal Debit, decimal Credit)> balances = new(StringComparer.Ordinal);
        List<string> codes = stages.Select(s => ReportParameters.String(parameters, s.Param)).Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
        if (codes.Count > 0)
        {
            var rows = await _db.Set<Core.Accounting.GlPeriodBalance>()
                .Where(b => b.CompanyId == companyId && codes.Contains(b.AccountCode))
                .GroupBy(b => b.AccountCode)
                .Select(g => new { AccountCode = g.Key, Debit = g.Sum(x => x.BaseDebit), Credit = g.Sum(x => x.BaseCredit) })
                .ToListAsync(ct).ConfigureAwait(false);
            foreach (var r in rows)
            {
                balances[r.AccountCode] = (r.Debit, r.Credit);
            }
        }

        List<IReadOnlyDictionary<string, object?>> result = [];
        foreach ((string param, string label) in stages)
        {
            string code = ReportParameters.String(parameters, param);
            if (string.IsNullOrWhiteSpace(code))
            {
                continue;
            }

            (decimal debit, decimal credit) = balances.GetValueOrDefault(code);
            result.Add(Row(new() { ["Stage"] = $"{label} ({code})", ["Debit"] = debit, ["Credit"] = credit }));
        }

        return result;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> TillVarianceAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        (DateOnly from, DateOnly to) = Range(parameters);
        List<CashierSession> sessions = await _db.Set<CashierSession>()
            .Where(s => s.CompanyId == companyId && s.CashUpVarianceBase != 0m && s.ClosedOn != null && s.ClosedOn >= from && s.ClosedOn <= to)
            .OrderBy(s => s.Id)
            .ToListAsync(ct).ConfigureAwait(false);
        return sessions.Select(s => Row(new() { ["Cashier"] = $"#{s.CashierUserId}", ["Session"] = s.Id.ToString(), ["Variance"] = s.CashUpVarianceBase, ["Status"] = s.Status.ToString() })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> CancelledReceiptsAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        (DateOnly from, DateOnly to) = Range(parameters);
        List<Receipt> receipts = await _db.Set<Receipt>().Include(r => r.Lines)
            .Where(r => r.CompanyId == companyId && r.Status == ReceiptStatus.Cancelled && r.Date >= from && r.Date <= to)
            .OrderBy(r => r.ReceiptNumber)
            .ToListAsync(ct).ConfigureAwait(false);
        return receipts.Select(r => Row(new() { ["ReceiptNumber"] = r.ReceiptNumber, ["Reason"] = r.CancellationReason ?? string.Empty, ["Supervisor"] = r.CancelledByUserId is { } uid ? $"#{uid}" : string.Empty, ["Amount"] = r.LineTotal })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ReprintLogAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        DateOnly from = ReportParameters.TryDate(parameters, "FromDate") ?? DateOnly.MinValue;
        DateOnly to = ReportParameters.TryDate(parameters, "ToDate") ?? DateOnly.MaxValue;
        List<ReceiptReprint> reprints = await _db.Set<ReceiptReprint>()
            .Where(r => r.CompanyId == companyId && r.PrintedOn >= from && r.PrintedOn <= to)
            .OrderBy(r => r.PrintedOn).ThenBy(r => r.Id)
            .ToListAsync(ct).ConfigureAwait(false);
        return reprints.Select(r => Row(new()
        {
            ["ReceiptNumber"] = $"{r.ReceiptNumber} (copy {r.CopyNumber})",
            ["PrintedOn"] = r.PrintedOn.ToDateTime(TimeOnly.MinValue),
            ["PrintedBy"] = $"#{r.PrintedByUserId}",
        })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GapAnalysisAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        _ = parameters;
        var rows = await _db.Set<Receipt>()
            .Where(r => r.CompanyId == companyId)
            .Select(r => new { r.StationId, r.ReceiptNumber })
            .ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, string> stationCodes = await _db.Set<CashierStation>()
            .Where(s => s.CompanyId == companyId)
            .ToDictionaryAsync(s => s.Id, s => s.Code, ct).ConfigureAwait(false);

        return rows.GroupBy(r => r.StationId)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                List<long> seqs = g.Select(r => Suffix(r.ReceiptNumber)).Where(n => n > 0).OrderBy(n => n).ToList();
                long min = seqs.Count > 0 ? seqs[0] : 0;
                long max = seqs.Count > 0 ? seqs[^1] : 0;
                long span = seqs.Count > 0 ? max - min + 1 : 0;
                long gaps = span - seqs.Count;
                return Row(new() { ["Station"] = stationCodes.GetValueOrDefault(g.Key, g.Key.ToString()), ["FromNumber"] = min, ["ToNumber"] = max, ["Gaps"] = gaps });
            })
            .ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> RefundsRegisterAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        DateOnly from = ReportParameters.TryDate(parameters, "FromDate") ?? DateOnly.MinValue;
        DateOnly to = ReportParameters.TryDate(parameters, "ToDate") ?? DateOnly.MaxValue;
        List<RefundRecord> refunds = await _db.Set<RefundRecord>()
            .Where(r => r.CompanyId == companyId && r.Date >= from && r.Date <= to)
            .OrderBy(r => r.Date).ThenBy(r => r.Id)
            .ToListAsync(ct).ConfigureAwait(false);
        return refunds.Select(r => Row(new()
        {
            ["Payer"] = r.PayerName,
            ["Status"] = "Paid",
            ["Days"] = 0,
            ["Amount"] = r.Amount,
        })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> CashMovementsAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        DateOnly from = ReportParameters.TryDate(parameters, "FromDate") ?? DateOnly.MinValue;
        DateOnly to = ReportParameters.TryDate(parameters, "ToDate") ?? DateOnly.MaxValue;
        List<SessionCashMovement> movements = await _db.Set<SessionCashMovement>()
            .Where(m => m.CompanyId == companyId && m.Date >= from && m.Date <= to)
            .OrderBy(m => m.Date).ThenBy(m => m.Id)
            .ToListAsync(ct).ConfigureAwait(false);
        return movements.Select(m => Row(new()
        {
            ["When"] = m.Date.ToDateTime(TimeOnly.MinValue),
            ["Type"] = m.Type.ToString(),
            ["Reason"] = m.Reason,
            ["Amount"] = m.Amount,
        })).ToList();
    }

    private static readonly string[] ReceiptingEntityTypes =
    [
        "Receipt", "CashierSession", "BankDeposit", "RefundRecord", "ReceiptReprint",
        "PaymentArrangement", "SuspenseAllocation", "DishonouredPayment", "ImportBatch",
        "Agent", "StationeryBook", "SessionCashMovement",
    ];

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> SodViolationsAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        DateOnly from = ReportParameters.TryDate(parameters, "FromDate") ?? DateOnly.MinValue;
        DateOnly to = ReportParameters.TryDate(parameters, "ToDate") ?? DateOnly.MaxValue;
        List<SegregationViolation> violations = await _db.Set<SegregationViolation>()
            .Where(v => v.CompanyId == companyId && v.OccurredOn >= from && v.OccurredOn <= to)
            .OrderBy(v => v.OccurredOn).ThenBy(v => v.Id)
            .ToListAsync(ct).ConfigureAwait(false);
        return violations.Select(v => Row(new()
        {
            ["User"] = $"#{v.UserId}",
            ["Conflict"] = v.Conflict,
            ["When"] = v.OccurredOn.ToDateTime(TimeOnly.MinValue),
        })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> AuditTrailAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        DateOnly from = ReportParameters.TryDate(parameters, "FromDate") ?? DateOnly.MinValue;
        DateOnly to = ReportParameters.TryDate(parameters, "ToDate") ?? DateOnly.MaxValue;
        DateTimeOffset fromUtc = new(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        DateTimeOffset toUtc = new(to.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

        var entries = await _db.Set<Core.Audit.ChangeLogEntry>()
            .Where(c => c.CompanyId == companyId && ReceiptingEntityTypes.Contains(c.EntityType) && c.OccurredOnUtc >= fromUtc && c.OccurredOnUtc <= toUtc)
            .OrderBy(c => c.Id)
            .Select(c => new { c.OccurredOnUtc, c.Action, c.UserName, c.EntityType, c.EntityId })
            .ToListAsync(ct).ConfigureAwait(false);

        return entries.Select(c => Row(new()
        {
            ["When"] = c.OccurredOnUtc.UtcDateTime,
            ["Action"] = c.Action.ToString(),
            ["User"] = string.IsNullOrEmpty(c.UserName) ? "system" : c.UserName,
            ["Detail"] = $"{c.EntityType} {c.EntityId}",
        })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> BackDatedReceiptsAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        DateOnly from = ReportParameters.TryDate(parameters, "FromDate") ?? DateOnly.MinValue;
        DateOnly to = ReportParameters.TryDate(parameters, "ToDate") ?? DateOnly.MaxValue;
        List<Receipt> receipts = await _db.Set<Receipt>().Include(r => r.Lines)
            .Where(r => r.CompanyId == companyId && r.Date >= from && r.Date <= to)
            .OrderBy(r => r.ReceiptNumber)
            .ToListAsync(ct).ConfigureAwait(false);

        // Back-dated = the business date precedes the date the receipt was actually captured.
        return receipts
            .Where(r => r.Date < DateOnly.FromDateTime(r.CreatedOnUtc.UtcDateTime))
            .Select(r => Row(new()
            {
                ["ReceiptNumber"] = r.ReceiptNumber,
                ["ReceiptDate"] = r.Date.ToDateTime(TimeOnly.MinValue),
                ["CapturedOn"] = r.CreatedOnUtc.UtcDateTime.Date,
                ["Amount"] = r.LineTotal,
            }))
            .ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> DailyBankingSummaryAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        DateOnly date = ReportParameters.Date(parameters, "Date");
        List<BankDeposit> deposits = await _db.Set<BankDeposit>()
            .Where(d => d.CompanyId == companyId && d.DepositDate == date)
            .ToListAsync(ct).ConfigureAwait(false);
        return deposits.GroupBy(d => d.Status)
            .OrderBy(g => g.Key.ToString())
            .Select(g => Row(new() { ["Stage"] = g.Key.ToString(), ["Amount"] = g.Sum(d => d.Amount) }))
            .ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> CashInTransitAgeingAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        DateOnly asAt = ReportParameters.Date(parameters, "AsAtDate");
        List<BankDeposit> deposits = await _db.Set<BankDeposit>()
            .Where(d => d.CompanyId == companyId && d.Status == BankDepositStatus.InTransit)
            .OrderBy(d => d.DepositDate)
            .ToListAsync(ct).ConfigureAwait(false);
        return deposits.Select(d => Row(new() { ["Reference"] = d.Reference, ["DepositDate"] = d.DepositDate.ToDateTime(TimeOnly.MinValue), ["Days"] = Math.Max(0, asAt.DayNumber - d.DepositDate.DayNumber), ["Amount"] = d.Amount })).ToList();
    }

    // Cash taken at the counters and not yet banked: the cash tenders of each session to the date, with the deposits made to the date set
    // against the oldest sessions first; what is left of each session is still unbanked.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> UnbankedCollectionsAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        DateOnly asAt = ReportParameters.Date(parameters, "AsAtDate");
        var taken = await (from r in _db.Set<Receipt>().AsNoTracking()
                           from t in r.Tenders
                           where r.CompanyId == companyId && r.Status == ReceiptStatus.Committed && r.Date <= asAt && t.Type == TenderType.Cash
                           group t by r.CashierSessionId into g
                           select new { SessionId = g.Key, Cash = g.Sum(t => t.Amount) }).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, (long Station, DateOnly Opened)> sessions = await _db.Set<CashierSession>().AsNoTracking().Where(s => s.CompanyId == companyId)
            .ToDictionaryAsync(s => s.Id, s => (s.StationId, s.OpenedOn), ct).ConfigureAwait(false);
        Dictionary<long, string> stations = await _db.Set<CashierStation>().AsNoTracking().Where(s => s.CompanyId == companyId).ToDictionaryAsync(s => s.Id, s => s.Code, ct).ConfigureAwait(false);
        decimal deposited = await _db.Set<BankDeposit>().AsNoTracking().Where(d => d.CompanyId == companyId && d.DepositDate <= asAt).SumAsync(d => (decimal?)d.Amount, ct).ConfigureAwait(false) ?? 0m;
        List<IReadOnlyDictionary<string, object?>> rows = [];
        foreach (var session in taken.Where(t => sessions.ContainsKey(t.SessionId)).OrderBy(t => sessions[t.SessionId].Opened).ThenBy(t => t.SessionId))
        {
            decimal covered = Math.Min(deposited, session.Cash);
            deposited -= covered;
            if (session.Cash - covered > 0m)
            {
                rows.Add(Row(new()
                {
                    ["Station"] = stations.GetValueOrDefault(sessions[session.SessionId].Station, "?"),
                    ["Session"] = $"{session.SessionId} ({sessions[session.SessionId].Opened:yyyy-MM-dd})",
                    ["Amount"] = session.Cash - covered,
                }));
            }
        }

        return rows;
    }

    // Prepaid token sales by day: the value issued and the part of each sale taken to the ratepayer's arrears.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> PrepaidTokensAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        (DateOnly from, DateOnly to) = Range(parameters);
        return [.. (await _db.Set<Core.Municipal.PrepaidSale>().AsNoTracking().Where(s => s.CompanyId == companyId && s.SoldOn >= from && s.SoldOn <= to)
                .GroupBy(s => s.SoldOn).Select(g => new { Date = g.Key, Value = g.Sum(s => s.Amount - s.ToArrears), Arrears = g.Sum(s => s.ToArrears) }).ToListAsync(ct).ConfigureAwait(false))
            .OrderBy(d => d.Date).Select(d => Row(new() { ["Date"] = d.Date.ToDateTime(TimeOnly.MinValue), ["TokenValue"] = d.Value, ["ArrearsRecovered"] = d.Arrears }))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> PendingTendersAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        DateOnly asAt = ReportParameters.Date(parameters, "AsAtDate");
        List<PendingTender> pending = await _db.Set<PendingTender>()
            .Where(t => t.CompanyId == companyId && t.Status == PendingTenderStatus.Pending)
            .OrderBy(t => t.Date)
            .ToListAsync(ct).ConfigureAwait(false);
        return pending.Select(t => Row(new()
        {
            ["Reference"] = t.Reference ?? t.ReceiptNumber,
            ["Tender"] = t.TenderType.ToString(),
            ["Days"] = Math.Max(0, asAt.DayNumber - t.Date.DayNumber),
            ["Amount"] = t.Amount,
        })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> DishonoursAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        DateOnly from = ReportParameters.TryDate(parameters, "FromDate") ?? DateOnly.MinValue;
        DateOnly to = ReportParameters.TryDate(parameters, "ToDate") ?? DateOnly.MaxValue;
        List<DishonouredPayment> dishonours = await _db.Set<DishonouredPayment>()
            .Where(d => d.CompanyId == companyId && d.Date >= from && d.Date <= to)
            .OrderBy(d => d.Date).ThenBy(d => d.Id)
            .ToListAsync(ct).ConfigureAwait(false);
        return dishonours.Select(d => Row(new()
        {
            ["Reference"] = d.Reference,
            ["Drawer"] = d.Drawer,
            ["Penalty"] = d.Penalty,
            ["Amount"] = d.Amount,
        })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> SuspenseAllocationLogAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        DateOnly from = ReportParameters.TryDate(parameters, "FromDate") ?? DateOnly.MinValue;
        DateOnly to = ReportParameters.TryDate(parameters, "ToDate") ?? DateOnly.MaxValue;
        List<SuspenseAllocation> allocations = await _db.Set<SuspenseAllocation>()
            .Where(a => a.CompanyId == companyId && a.Date >= from && a.Date <= to)
            .OrderBy(a => a.Date).ThenBy(a => a.Id)
            .ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, string> customers = await _db.Set<Core.Trading.Customer>()
            .Where(c => c.CompanyId == companyId)
            .ToDictionaryAsync(c => c.Id, c => $"{c.Code} — {c.Name}", ct).ConfigureAwait(false);

        return allocations.Select(a => Row(new()
        {
            ["When"] = a.Date.ToDateTime(TimeOnly.MinValue),
            ["Account"] = customers.GetValueOrDefault(a.CustomerId, $"#{a.CustomerId}"),
            ["Reason"] = a.Reason,
            ["Amount"] = a.Amount,
        })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ImportExceptionsAsync(long companyId, CancellationToken ct)
    {
        var lines = await _db.Set<ImportBatch>()
            .Where(b => b.CompanyId == companyId)
            .SelectMany(b => b.Lines)
            .Where(l => l.Status == ImportLineStatus.Unmatched)
            .Select(l => new { l.PayerReference, l.PayerName, l.Amount })
            .ToListAsync(ct).ConfigureAwait(false);
        return lines.Select(l => Row(new() { ["PayerReference"] = l.PayerReference, ["PayerName"] = l.PayerName, ["Reason"] = "No account matched", ["Amount"] = l.Amount })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> PaymentHistoryAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        long account = ReportParameters.Long(parameters, "Account");
        List<Receipt> receipts = await _db.Set<Receipt>().Include(r => r.Lines)
            .Where(r => r.CompanyId == companyId && r.PayerAccountId == account)
            .OrderBy(r => r.Date).ThenBy(r => r.ReceiptNumber)
            .ToListAsync(ct).ConfigureAwait(false);
        return receipts.Select(r => Row(new() { ["Date"] = r.Date.ToDateTime(TimeOnly.MinValue), ["ReceiptNumber"] = r.ReceiptNumber, ["Type"] = r.Status.ToString(), ["Amount"] = r.LineTotal })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> CreditBalancesAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        DateOnly asAt = ReportParameters.Date(parameters, "AsAtDate");
        var credits = await _db.Set<Core.Trading.OpenItem>()
            .Where(i => i.CompanyId == companyId && i.Ledger == Core.Trading.TradingLedger.AccountsReceivable && i.Sign == Core.Trading.LedgerSign.Credit && i.OutstandingTransaction > 0m)
            .Select(i => new { i.TraderId, i.OutstandingTransaction, i.DocumentDate })
            .ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, (string Code, string Name)> customers = await _db.Set<Core.Trading.Customer>()
            .Where(c => c.CompanyId == companyId)
            .ToDictionaryAsync(c => c.Id, c => (c.Code, c.Name), ct).ConfigureAwait(false);

        return credits.GroupBy(c => c.TraderId)
            .Select(g =>
            {
                (string code, string name) = customers.GetValueOrDefault(g.Key, (g.Key.ToString(), string.Empty));
                long days = Math.Max(0, asAt.DayNumber - g.Min(x => x.DocumentDate).DayNumber);
                return Row(new() { ["Account"] = code, ["Name"] = name, ["Days"] = days, ["Credit"] = g.Sum(x => x.OutstandingTransaction) });
            })
            .ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ArrangementsAsync(long companyId, CancellationToken ct)
    {
        List<PaymentArrangement> arrangements = await _db.Set<PaymentArrangement>()
            .Where(a => a.CompanyId == companyId)
            .OrderBy(a => a.StartDate).ThenBy(a => a.Id)
            .ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, string> customers = await _db.Set<Core.Trading.Customer>()
            .Where(c => c.CompanyId == companyId)
            .ToDictionaryAsync(c => c.Id, c => $"{c.Code} — {c.Name}", ct).ConfigureAwait(false);

        return arrangements.Select(a => Row(new()
        {
            ["Account"] = customers.GetValueOrDefault(a.CustomerId, $"#{a.CustomerId}"),
            ["StartDate"] = a.StartDate.ToDateTime(TimeOnly.MinValue),
            ["Status"] = a.Status.ToString(),
            ["Instalment"] = a.InstalmentAmount,
        })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> TopPayersAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        (DateOnly from, DateOnly to) = Range(parameters);
        List<Receipt> receipts = await CommittedReceiptsAsync(companyId, from, to, ct).ConfigureAwait(false);
        return receipts.GroupBy(r => new { r.PayerKind, r.PayerAccountId })
            .Select(g => Row(new() { ["Payer"] = g.Key.PayerKind == PayerKind.Anonymous ? "(sundry)" : $"{g.Key.PayerKind} #{g.Key.PayerAccountId}", ["Count"] = g.Count(), ["Amount"] = g.Sum(r => r.LineTotal) }))
            .OrderByDescending(r => (decimal)r["Amount"]!)
            .Take(20)
            .ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> DormantAccountsAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        DateOnly asAt = ReportParameters.Date(parameters, "AsAtDate");
        DateOnly cutoff = asAt.AddMonths(-3); // no payment in the last 3 months

        // Outstanding net balance per debtor (debits − credits) from the AR sub-ledger.
        var balances = await _db.Set<Core.Trading.OpenItem>()
            .Where(i => i.CompanyId == companyId && i.Ledger == Core.Trading.TradingLedger.AccountsReceivable && i.OutstandingTransaction > 0m)
            .Select(i => new { i.TraderId, i.Sign, i.OutstandingTransaction })
            .ToListAsync(ct).ConfigureAwait(false);

        Dictionary<long, decimal> netByTrader = balances
            .GroupBy(b => b.TraderId)
            .ToDictionary(
                g => g.Key,
                g => g.Where(x => x.Sign == Core.Trading.LedgerSign.Debit).Sum(x => x.OutstandingTransaction)
                   - g.Where(x => x.Sign == Core.Trading.LedgerSign.Credit).Sum(x => x.OutstandingTransaction));

        // Last committed receipt date per payer.
        var lastPayments = await _db.Set<Receipt>()
            .Where(r => r.CompanyId == companyId && r.Status == ReceiptStatus.Committed && r.PayerAccountId != null)
            .GroupBy(r => r.PayerAccountId!.Value)
            .Select(g => new { TraderId = g.Key, Last = g.Max(r => r.Date) })
            .ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, DateOnly> lastByTrader = lastPayments.ToDictionary(x => x.TraderId, x => x.Last);

        Dictionary<long, (string Code, string Name)> customers = await _db.Set<Core.Trading.Customer>()
            .Where(c => c.CompanyId == companyId)
            .ToDictionaryAsync(c => c.Id, c => (c.Code, c.Name), ct).ConfigureAwait(false);

        List<IReadOnlyDictionary<string, object?>> result = [];
        foreach ((long traderId, decimal balance) in netByTrader.Where(kv => kv.Value > 0m).OrderBy(kv => kv.Key))
        {
            bool hasRecent = lastByTrader.TryGetValue(traderId, out DateOnly last) && last >= cutoff;
            if (hasRecent)
            {
                continue;
            }

            (string code, string name) = customers.GetValueOrDefault(traderId, (traderId.ToString(), string.Empty));
            result.Add(Row(new()
            {
                ["Account"] = code,
                ["Name"] = name,
                ["LastPayment"] = lastByTrader.TryGetValue(traderId, out DateOnly lp) ? lp.ToDateTime(TimeOnly.MinValue) : (object?)"never",
                ["Balance"] = balance,
            }));
        }

        return result;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> AgentAgeingAsync(long companyId, CancellationToken ct)
    {
        List<Agent> agents = await _db.Set<Agent>()
            .Where(a => a.CompanyId == companyId)
            .OrderBy(a => a.Code)
            .ToListAsync(ct).ConfigureAwait(false);
        return agents.Where(a => a.Outstanding > 0m)
            .Select(a => Row(new() { ["Agent"] = $"{a.Code} — {a.Name}", ["Days"] = 0, ["Outstanding"] = a.Outstanding }))
            .ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> CollectionTrendAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        (DateOnly from, DateOnly to) = Range(parameters);
        List<Receipt> receipts = await CommittedReceiptsAsync(companyId, from, to, ct).ConfigureAwait(false);
        return receipts.GroupBy(r => $"{r.Date.Year:D4}-{r.Date.Month:D2}")
            .OrderBy(g => g.Key)
            .Select(g => Row(new() { ["Period"] = g.Key, ["Amount"] = g.Sum(r => r.LineTotal) }))
            .ToList();
    }

    private async Task<Dictionary<string, string>> AccountNamesAsync(long companyId, CancellationToken ct) =>
        await _db.Accounts.Where(a => a.CompanyId == companyId).ToDictionaryAsync(a => a.AccountCode, a => a.Name, ct).ConfigureAwait(false);

    private static (DateOnly From, DateOnly To) Range(IReadOnlyDictionary<string, object?> parameters) =>
        (ReportParameters.TryDate(parameters, "FromDate") ?? DateOnly.MinValue, ReportParameters.TryDate(parameters, "ToDate") ?? DateOnly.MaxValue);

    private static string Channel(TenderType type) => type switch
    {
        TenderType.Cash => "Counter",
        TenderType.Card => "Card terminal",
        TenderType.MobileMoney => "Mobile money",
        TenderType.BankTransfer => "Bank",
        TenderType.Cheque => "Cheque",
        _ => "Other",
    };

    private static long Suffix(string receiptNumber)
    {
        int dash = receiptNumber.LastIndexOf('-');
        string tail = dash >= 0 ? receiptNumber[(dash + 1)..] : receiptNumber;
        return long.TryParse(tail, out long value) ? value : 0;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ReceiptDayBookAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        DateOnly from = ReportParameters.TryDate(parameters, "FromDate") ?? DateOnly.MinValue;
        DateOnly to = ReportParameters.TryDate(parameters, "ToDate") ?? DateOnly.MaxValue;
        List<Receipt> receipts = await _db.Set<Receipt>().Include(r => r.Lines).Include(r => r.Tenders)
            .Where(r => r.CompanyId == companyId && r.Date >= from && r.Date <= to)
            .OrderBy(r => r.ReceiptNumber)
            .ToListAsync(ct).ConfigureAwait(false);

        return receipts.Select(r => Row(new()
        {
            ["ReceiptNumber"] = r.ReceiptNumber,
            ["Payer"] = r.PayerKind == PayerKind.Anonymous ? "(sundry)" : $"{r.PayerKind} #{r.PayerAccountId}",
            ["Tender"] = r.Tenders.FirstOrDefault()?.Type.ToString() ?? string.Empty,
            ["Amount"] = r.Lines.Sum(l => l.Amount),
        })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> SuspenseRegisterAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        DateOnly asAt = ReportParameters.Date(parameters, "AsAtDate");
        var lines = await _db.Set<ImportBatch>()
            .Where(b => b.CompanyId == companyId)
            .SelectMany(b => b.Lines)
            .Where(l => l.Status == ImportLineStatus.Unmatched)
            .Select(l => new { l.PayerReference, l.PayerName, l.Amount, l.ValueDate })
            .ToListAsync(ct).ConfigureAwait(false);

        return lines.Select(l => Row(new()
        {
            ["Reference"] = l.PayerReference,
            ["PayerName"] = l.PayerName,
            ["Days"] = Math.Max(0, asAt.DayNumber - l.ValueDate.DayNumber),
            ["Amount"] = l.Amount,
        })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ImportBatchesAsync(long companyId, CancellationToken ct)
    {
        List<ImportBatch> batches = await _db.Set<ImportBatch>().Include(b => b.Lines)
            .Where(b => b.CompanyId == companyId)
            .OrderByDescending(b => b.ImportDate)
            .ToListAsync(ct).ConfigureAwait(false);

        return batches.Select(b => Row(new()
        {
            ["FileReference"] = b.FileReference,
            ["Matched"] = b.MatchedCount,
            ["Unmatched"] = b.UnmatchedCount,
            ["TotalAmount"] = b.TotalAmount,
        })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> DepositsHeldAsync(long companyId, CancellationToken ct)
    {
        // Deposits held = committed receipt lines of type Deposit (the deposit liability was raised on them).
        var deposits = await _db.Set<Receipt>()
            .Where(r => r.CompanyId == companyId && r.Status == ReceiptStatus.Committed)
            .SelectMany(r => r.Lines.Where(l => l.Type == ReceiptLineType.Deposit).Select(l => new { r.ReceiptNumber, r.PayerKind, r.PayerAccountId, r.Date, l.Amount, l.Narrative }))
            .ToListAsync(ct).ConfigureAwait(false);

        return deposits.Select(d => Row(new()
        {
            ["Payer"] = d.PayerKind == PayerKind.Anonymous ? d.ReceiptNumber : $"{d.PayerKind} #{d.PayerAccountId}",
            ["Type"] = d.Narrative ?? "Deposit",
            ["ReceivedOn"] = d.Date.ToDateTime(TimeOnly.MinValue),
            ["Amount"] = d.Amount,
        })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> AgentReconciliationAsync(long companyId, CancellationToken ct)
    {
        List<Agent> agents = await _db.Set<Agent>()
            .Where(a => a.CompanyId == companyId)
            .OrderBy(a => a.Code)
            .ToListAsync(ct).ConfigureAwait(false);

        return agents.Select(a => Row(new()
        {
            ["Agent"] = $"{a.Code} — {a.Name}",
            ["Collected"] = a.CollectedToDate,
            ["Remitted"] = a.RemittedToDate,
            ["Outstanding"] = a.Outstanding,
        })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> StationeryControlAsync(long companyId, CancellationToken ct)
    {
        List<StationeryBook> books = await _db.Set<StationeryBook>().Include(b => b.Captures)
            .Where(b => b.CompanyId == companyId)
            .OrderBy(b => b.BookNumber)
            .ToListAsync(ct).ConfigureAwait(false);

        return books.Select(b => Row(new()
        {
            ["BookNumber"] = b.BookNumber,
            ["Range"] = $"{b.FromSerial}-{b.ToSerial}",
            ["Used"] = b.Captures.Count,
            ["Unaccounted"] = b.UnusedSerials().Count,
        })).ToList();
    }

    private static IReadOnlyDictionary<string, object?> Row(Dictionary<string, object?> values) => values;
}
