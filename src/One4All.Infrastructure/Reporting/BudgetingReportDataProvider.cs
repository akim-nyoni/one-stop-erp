using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Budgeting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Core.Budgeting;
using One4All.Core.Procurement;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Reporting;

/// <summary>Serves the B-coded budgeting reports (brief §14 A-B): every data command whose name starts with <c>Bgt</c>.</summary>
public sealed class BudgetingReportDataProvider : IReportDataProvider
{
    private static readonly BudgetStatus[] InForce = [BudgetStatus.Approved, BudgetStatus.Active];
    private static readonly string[] BudgetEntities = ["Budget", "BudgetLine", "BudgetVirement", "BudgetCommitment", "VarianceComment"];

    private readonly One4AllDbContext _db;
    private readonly IBudgetControlService _control;

    /// <summary>Creates the provider.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="control">Budget control (seal verification).</param>
    public BudgetingReportDataProvider(One4AllDbContext db, IBudgetControlService control)
    {
        _db = db;
        _control = control;
    }

    /// <inheritdoc />
    public bool CanHandle(string command) => command.StartsWith("Bgt", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetRowsAsync(long companyId, string command, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct = default)
    {
        DateOnly asAt = parameters.TryGetValue("AsAtDate", out object? value) && value is DateOnly date ? date : DateOnly.FromDateTime(DateTime.Today);
        FiscalYear? year = await YearAsync(companyId, asAt, ct).ConfigureAwait(false);
        if (year is null)
        {
            return [];
        }

        Context context = new(companyId, asAt, year, PeriodOf(year, asAt));
        return command.ToUpperInvariant() switch
        {
            "BGTCAPITALPHASING" => await CapitalPhasingAsync(context, ct).ConfigureAwait(false),
            "BGTAPPROVALAUDIT" => await ApprovalAuditAsync(context, ct).ConfigureAwait(false),
            "BGTREPHASING" => await RephasingAsync(context, ct).ConfigureAwait(false),
            "BGTCAPITALUNSPENT" => await CapitalUnspentAsync(context, ct).ConfigureAwait(false),
            "BGTROLLING" => await RollingAsync(context, parameters.TryGetValue("Months", out object? months) && months is not null and not "" ? Convert.ToInt32(months, CultureInfo.InvariantCulture) : 3, ct)
                .ConfigureAwait(false),
            "BGTCEILINGGAP" => await CeilingGapAsync(context, ct).ConfigureAwait(false),
            "BGTHIERARCHY" => await HierarchyAsync(context, parameters.TryGetValue("Level", out object? level) && level is string l && l.Length > 0 ? l : "Directorate", ct).ConfigureAwait(false),
            "BGTDRIVERS" => await DriversAsync(context, ct).ConfigureAwait(false),
            "BGTWHATIF" => await WhatIfAsync(context, parameters.TryGetValue("DriverCode", out object? code) ? code as string : null,
                parameters.TryGetValue("NewValue", out object? nv) && nv is not null and not "" ? Convert.ToDecimal(nv, CultureInfo.InvariantCulture) : null, ct).ConfigureAwait(false),
            "BGTBASIS" => await BasisAsync(context, ct).ConfigureAwait(false),
            "BGTSTAFF" => await StaffAsync(context, parameters.TryGetValue("StaffAccount", out object? sa) && sa is string account && account.Length > 0 ? account : "6100",
                parameters.TryGetValue("VacancyPercent", out object? vp) && vp is not null and not "" ? Convert.ToDecimal(vp, CultureInfo.InvariantCulture) : 0m, ct).ConfigureAwait(false),
            "BGTREVENUEBUILD" => await RevenueBuildAsync(context, ct).ConfigureAwait(false),
            "BGTHOLDERS" => await HoldersAsync(context, ct).ConfigureAwait(false),
            "BGTPREPSTATUS" => await PrepStatusAsync(context, parameters.TryGetValue("Deadline", out object? deadline) && deadline is DateOnly d ? d : null, ct).ConfigureAwait(false),
            "BGTCUTSTRAIL" => await CutsTrailAsync(context, ct).ConfigureAwait(false),
            "BGTAPPROVED" => await ApprovedAsync(context, ct).ConfigureAwait(false),
            "BGTRECONCILIATION" => await ReconciliationAsync(context, ct).ConfigureAwait(false),
            "BGTSUPPLEMENTARY" => await SupplementaryAsync(context, ct).ConfigureAwait(false),
            "BGTCARRYFORWARD" => await CarryForwardAsync(context, ct).ConfigureAwait(false),
            "BGTOUTTURN" => await OutturnAsync(context, ct).ConfigureAwait(false),
            "BGTFORECASTMOVEMENT" => await ForecastMovementAsync(context, ct).ConfigureAwait(false),
            "BGTCASH" => await CashAsync(context, ct).ConfigureAwait(false),
            "BGTCAPITALPROJECTS" => await CapitalProjectsAsync(context, ct).ConfigureAwait(false),
            "BGTSUBMISSION" => await SubmissionAsync(context, ct).ConfigureAwait(false),
            "BGTVERSIONS" => await VersionsAsync(context, ct).ConfigureAwait(false),
            "BGTMULTIYEAR" => await MultiYearAsync(context, ct).ConfigureAwait(false),
            "BGTREGISTER" => await RegisterAsync(context, ct).ConfigureAwait(false),
            "BGTLINECHANGES" => await ChangesAsync(context, ["BudgetLine"], false, ct).ConfigureAwait(false),
            "BGTVIREMENTS" => await VirementsAsync(context, ct).ConfigureAwait(false),
            "BGTVOTEBOOK" => await VoteBookAsync(context, ct).ConfigureAwait(false),
            "BGTCOMMITMENTS" => await CommitmentsAsync(context, ct).ConfigureAwait(false),
            "BGTCOMMITMENTVSPO" => await CommitmentVsPoAsync(context, ct).ConfigureAwait(false),
            "BGTUNAPPROPRIATED" => await UnappropriatedAsync(context, ct).ConfigureAwait(false),
            "BGTUTILISATION" => await UtilisationAsync(context, exceptionsOnly: false, ct).ConfigureAwait(false),
            "BGTEXCEPTIONS" => await UtilisationAsync(context, exceptionsOnly: true, ct).ConfigureAwait(false),
            "BGTCOMMENTARY" => await CommentaryAsync(context, ct).ConfigureAwait(false),
            "BGTDEPARTMENTS" => await DepartmentsAsync(context, ct).ConfigureAwait(false),
            "BGTACCURACY" => await AccuracyAsync(context, ct).ConfigureAwait(false),
            "BGTAUDIT" => await ChangesAsync(context, BudgetEntities, true, ct).ConfigureAwait(false),
            _ => [],
        };
    }

    private sealed record Context(long CompanyId, DateOnly AsAt, FiscalYear Year, int Period);

    private Task<FiscalYear?> YearAsync(long companyId, DateOnly date, CancellationToken ct)
        => _db.FiscalYears.AsNoTracking().FirstOrDefaultAsync(y => y.CompanyId == companyId && y.StartDate <= date && y.EndDate >= date, ct);

    private static int PeriodOf(FiscalYear year, DateOnly date) => ((date.Year - year.StartDate.Year) * 12) + date.Month - year.StartDate.Month + 1;

    private Task<List<Budget>> BudgetsAsync(long companyId, long yearId, bool inForceOnly, CancellationToken ct)
        => _db.Budgets.AsNoTracking().IgnoreQueryFilters().Include(b => b.Lines)
            .Where(b => b.CompanyId == companyId && b.FiscalYearId == yearId && !b.IsDeleted && (!inForceOnly || (b.IsActive && InForce.Contains(b.Status)))).ToListAsync(ct);

    // Budget lines in force, by account and department, for periods up to one (the whole year by default).
    private async Task<Dictionary<(string Account, string Department), decimal>> BudgetedAsync(long companyId, long yearId, int throughPeriod, CancellationToken ct)
        => (await BudgetsAsync(companyId, yearId, true, ct).ConfigureAwait(false))
            .SelectMany(b => b.Lines.Where(l => l.Period <= throughPeriod).Select(l => (Key: (l.AccountCode, b.Department ?? string.Empty), l.Amount)))
            .GroupBy(x => x.Key).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

    // Actuals in natural sign (expense and asset debit-positive, income credit-positive), by account and department.
    private async Task<Dictionary<(string Account, string Department), (decimal Amount, int Postings)>> ActualsAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        var lines = await (from l in _db.Set<JournalLine>().AsNoTracking()
                           join j in _db.Journals.AsNoTracking() on l.JournalEntryId equals j.Id
                           join a in _db.Accounts.AsNoTracking() on new { j.CompanyId, l.AccountCode } equals new { a.CompanyId, a.AccountCode }
                           where j.CompanyId == companyId && (j.Status == JournalStatus.Posted || j.Status == JournalStatus.Reversed) && j.PostingDate >= fromDate && j.PostingDate <= toDate
                           group l by new { l.AccountCode, l.Department, a.Type } into g
                           select new { g.Key.AccountCode, g.Key.Department, g.Key.Type, Net = g.Sum(l => l.BaseDebit - l.BaseCredit), Count = g.Count() }).ToListAsync(ct).ConfigureAwait(false);
        return lines.GroupBy(l => (l.AccountCode, l.Department ?? string.Empty)).ToDictionary(g => g.Key,
            g => (g.Sum(l => l.Type is AccountType.Income or AccountType.Liability or AccountType.Equity ? -l.Net : l.Net), g.Sum(l => l.Count)));
    }

    // An account's actual for a budget key: the department's own lines, or all departments for a company-wide budget.
    private static decimal ActualFor(Dictionary<(string Account, string Department), (decimal Amount, int Postings)> actuals, (string Account, string Department) key)
        => key.Department.Length == 0 ? actuals.Where(a => a.Key.Account == key.Account).Sum(a => a.Value.Amount) : actuals.GetValueOrDefault(key).Amount;

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> SubmissionAsync(Context c, CancellationToken ct)
    {
        Dictionary<string, string> names = await NamesAsync(c.CompanyId, ct).ConfigureAwait(false);
        List<Budget> budgets = await BudgetsAsync(c.CompanyId, c.Year.Id, false, ct).ConfigureAwait(false);
        decimal Quarter(IEnumerable<BudgetLine> lines, int q) => lines.Where(l => (l.Period - 1) / 3 == q).Sum(l => l.Amount);
        return [.. budgets.OrderBy(b => b.Department, StringComparer.Ordinal).ThenBy(b => b.Label, StringComparer.Ordinal).ThenBy(b => b.VersionNumber)
            .SelectMany(b => b.Lines.GroupBy(l => l.AccountCode).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => Row(("Budget", b.Label), ("Version", b.VersionNumber), ("Status", b.Status.ToString()),
                ("Department", b.Department ?? "All"), ("Account", g.Key), ("Name", names.GetValueOrDefault(g.Key, "?")), ("Q1", Money(Quarter(g, 0))), ("Q2", Money(Quarter(g, 1))), ("Q3", Money(Quarter(g, 2))),
                ("Q4", Money(Quarter(g, 3))), ("Total", g.Sum(l => l.Amount)))))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> VersionsAsync(Context c, CancellationToken ct)
    {
        Dictionary<string, string> names = await NamesAsync(c.CompanyId, ct).ConfigureAwait(false);
        List<IReadOnlyDictionary<string, object?>> rows = [];
        foreach (IGrouping<(string Label, string? Department), Budget> group in (await BudgetsAsync(c.CompanyId, c.Year.Id, false, ct).ConfigureAwait(false))
                     .GroupBy(b => (b.Label, b.Department)).Where(g => g.Count() > 1).OrderBy(g => g.Key.Label, StringComparer.Ordinal))
        {
            List<Budget> ordered = [.. group.OrderBy(b => b.VersionNumber)];
            Budget previous = ordered[^2], current = ordered[^1];
            foreach (string account in previous.Lines.Select(l => l.AccountCode).Union(current.Lines.Select(l => l.AccountCode)).Order(StringComparer.Ordinal))
            {
                decimal before = previous.Lines.Where(l => l.AccountCode == account).Sum(l => l.Amount), after = current.Lines.Where(l => l.AccountCode == account).Sum(l => l.Amount);
                rows.Add(Row(("Budget", group.Key.Label + (group.Key.Department is null ? string.Empty : $" ({group.Key.Department})")), ("Versions", $"v{previous.VersionNumber} → v{current.VersionNumber}"),
                    ("Account", account), ("Name", names.GetValueOrDefault(account, "?")), ("Previous", Money(before)), ("Current", Money(after)),
                    ("Percent", before == 0m ? "—" : ((after - before) * 100m / before).ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + "%"), ("Change", after - before)));
            }
        }

        return rows;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> MultiYearAsync(Context c, CancellationToken ct)
    {
        Dictionary<string, string> names = await NamesAsync(c.CompanyId, ct).ConfigureAwait(false);
        List<(Dictionary<string, decimal> Budget, Dictionary<string, decimal> Actual)> years = [];
        for (int back = 2; back >= 0; back--)
        {
            FiscalYear? year = back == 0 ? c.Year : await YearAsync(c.CompanyId, c.Year.StartDate.AddYears(-back), ct).ConfigureAwait(false);
            if (year is null)
            {
                years.Add(([], []));
                continue;
            }

            DateOnly end = back == 0 ? c.AsAt : year.EndDate;
            years.Add(((await BudgetedAsync(c.CompanyId, year.Id, int.MaxValue, ct).ConfigureAwait(false)).GroupBy(b => b.Key.Account).ToDictionary(g => g.Key, g => g.Sum(x => x.Value)),
                (await ActualsAsync(c.CompanyId, year.StartDate, end, ct).ConfigureAwait(false)).GroupBy(a => a.Key.Account).ToDictionary(g => g.Key, g => g.Sum(x => x.Value.Amount))));
        }

        return [.. years.SelectMany(y => y.Budget.Keys).Distinct().Order(StringComparer.Ordinal).Select(account => Row(("Account", account), ("Name", names.GetValueOrDefault(account, "?")),
            ("Budget2", Money(years[0].Budget.GetValueOrDefault(account))), ("Actual2", Money(years[0].Actual.GetValueOrDefault(account))),
            ("Budget1", Money(years[1].Budget.GetValueOrDefault(account))), ("Actual1", Money(years[1].Actual.GetValueOrDefault(account))),
            ("Budget0", Money(years[2].Budget.GetValueOrDefault(account))), ("Actual0", years[2].Actual.GetValueOrDefault(account))))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> RegisterAsync(Context c, CancellationToken ct)
    {
        List<Budget> budgets = await BudgetsAsync(c.CompanyId, c.Year.Id, false, ct).ConfigureAwait(false);
        List<IReadOnlyDictionary<string, object?>> rows = [];
        foreach (Budget b in budgets.OrderBy(b => b.Label, StringComparer.Ordinal).ThenBy(b => b.VersionNumber))
        {
            string seal = "not sealed";
            if (b.SealHash is not null)
            {
                var verified = await _control.VerifySealAsync(c.CompanyId, b.Id, ct).ConfigureAwait(false);
                seal = verified.IsSuccess && verified.Value ? "verified" : "BROKEN";
            }

            rows.Add(Row(("Budget", b.Label + (b.Department is null ? string.Empty : $" ({b.Department})")), ("Year", c.Year.Code), ("Type", b.Type.ToString()), ("Version", b.VersionNumber),
                ("Status", b.Status.ToString()), ("Active", b.IsActive ? "Yes" : string.Empty), ("Reason", b.Reason ?? string.Empty), ("Seal", seal), ("Total", b.Lines.Sum(l => l.Amount))));
        }

        return rows;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ChangesAsync(Context c, string[] entities, bool showEntity, CancellationToken ct)
    {
        DateTimeOffset start = new(c.Year.StartDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), end = new(c.AsAt.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
        return [.. (await _db.ChangeLog.AsNoTracking().Where(l => (l.CompanyId == c.CompanyId || l.CompanyId == null) && entities.Contains(l.EntityType) && l.OccurredOnUtc >= start && l.OccurredOnUtc <= end)
                .OrderBy(l => l.Id).Take(10000).ToListAsync(ct).ConfigureAwait(false))
            .Select(l => showEntity
                ? Row(("When", l.OccurredOnUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)), ("User", l.UserName), ("Entity", l.EntityType), ("Id", l.EntityId ?? "—"), ("Action", l.Action.ToString()),
                    ("Changed", l.ChangedFields.Count == 0 ? "—" : string.Join(", ", l.ChangedFields)))
                : Row(("When", l.OccurredOnUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)), ("User", l.UserName), ("Id", l.EntityId ?? "—"), ("Action", l.Action.ToString()),
                    ("Changed", l.ChangedFields.Count == 0 ? "—" : string.Join(", ", l.ChangedFields))))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> VirementsAsync(Context c, CancellationToken ct)
    {
        Dictionary<long, string> users = await _db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Username, ct).ConfigureAwait(false);
        List<long> budgetIds = [.. (await BudgetsAsync(c.CompanyId, c.Year.Id, false, ct).ConfigureAwait(false)).Select(b => b.Id)];
        return [.. (await _db.Set<BudgetVirement>().AsNoTracking().Where(v => v.CompanyId == c.CompanyId && budgetIds.Contains(v.BudgetId)).OrderBy(v => v.CreatedOnUtc).ToListAsync(ct).ConfigureAwait(false))
            .Select(v => Row(("Requested", v.CreatedOnUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)), ("From", v.FromAccountCode), ("To", v.ToAccountCode), ("Period", v.Period),
                ("Justification", v.Justification), ("Status", v.Status.ToString()), ("RequestedBy", users.GetValueOrDefault(v.RequestedByUserId, $"user {v.RequestedByUserId}")),
                ("ReviewedBy", v.ReviewedByUserId is long r ? users.GetValueOrDefault(r, $"user {r}") : "—"), ("Amount", v.Amount)))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> VoteBookAsync(Context c, CancellationToken ct)
    {
        Dictionary<string, string> names = await NamesAsync(c.CompanyId, ct).ConfigureAwait(false);
        var budgeted = await BudgetedAsync(c.CompanyId, c.Year.Id, int.MaxValue, ct).ConfigureAwait(false);
        var actuals = await ActualsAsync(c.CompanyId, c.Year.StartDate, c.AsAt, ct).ConfigureAwait(false);
        List<BudgetCommitment> commitments = await OpenCommitmentsAsync(c, ct).ConfigureAwait(false);
        return [.. budgeted.OrderBy(b => b.Key.Account, StringComparer.Ordinal).ThenBy(b => b.Key.Department, StringComparer.Ordinal).Select(b =>
        {
            decimal actual = ActualFor(actuals, b.Key);
            decimal committed = commitments.Where(x => x.AccountCode == b.Key.Account && (b.Key.Department.Length == 0 || x.Department == b.Key.Department)).Sum(x => x.Amount);
            return Row(("Account", b.Key.Account), ("Name", names.GetValueOrDefault(b.Key.Account, "?")), ("Department", b.Key.Department.Length == 0 ? "All" : b.Key.Department), ("Budget", Money(b.Value)),
                ("Actual", Money(actual)), ("Committed", Money(committed)), ("Remaining", b.Value - actual - committed));
        })];
    }

    private Task<List<BudgetCommitment>> OpenCommitmentsAsync(Context c, CancellationToken ct)
        => _db.BudgetCommitments.AsNoTracking().Where(x => x.CompanyId == c.CompanyId && x.FiscalYearId == c.Year.Id && x.Status == CommitmentStatus.Open).ToListAsync(ct);

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> CommitmentsAsync(Context c, CancellationToken ct)
        => [.. (await _db.BudgetCommitments.AsNoTracking().Where(x => x.CompanyId == c.CompanyId && x.FiscalYearId == c.Year.Id).OrderBy(x => x.Period).ThenBy(x => x.Reference).ToListAsync(ct).ConfigureAwait(false))
            .Select(x => Row(("Reference", x.Reference), ("Account", x.AccountCode), ("Department", x.Department ?? "—"), ("Period", x.Period), ("Type", x.Type.ToString()),
                ("Status", x.CarriedFromId is null ? x.Status.ToString() : $"{x.Status} (carried forward)"), ("Amount", x.Amount)))];

    // The open value of an order is what is still to be received at the order price.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> CommitmentVsPoAsync(Context c, CancellationToken ct)
    {
        List<BudgetCommitment> commitments = await OpenCommitmentsAsync(c, ct).ConfigureAwait(false);
        List<string> references = [.. commitments.Select(x => x.Reference).Distinct()];
        Dictionary<string, PurchaseOrder> orders = (await _db.Set<PurchaseOrder>().AsNoTracking().Include(o => o.Lines).Where(o => o.CompanyId == c.CompanyId && references.Contains(o.OrderNumber))
            .ToListAsync(ct).ConfigureAwait(false)).GroupBy(o => o.OrderNumber).ToDictionary(g => g.Key, g => g.First());
        return [.. commitments.GroupBy(x => x.Reference).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g =>
        {
            PurchaseOrder? order = orders.GetValueOrDefault(g.Key);
            decimal open = order is null || order.Status is PurchaseOrderStatus.Cancelled or PurchaseOrderStatus.Closed or PurchaseOrderStatus.Received ? 0m
                : order.Lines.Sum(l => Math.Round((l.OrderedQuantity - l.ReceivedQuantity) * l.UnitPrice, 2, MidpointRounding.AwayFromZero));
            decimal committed = g.Sum(x => x.Amount);
            return Row(("Reference", g.Key), ("Order", order is null ? "no such order" : order.Status.ToString()), ("OpenValue", Money(open)), ("Committed", Money(committed)), ("Difference", committed - open));
        })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> UnappropriatedAsync(Context c, CancellationToken ct)
    {
        Dictionary<string, LedgerAccount> accounts = await _db.Accounts.AsNoTracking().Where(a => a.CompanyId == c.CompanyId && a.Type == AccountType.Expense).ToDictionaryAsync(a => a.AccountCode, ct).ConfigureAwait(false);
        HashSet<string> budgeted = [.. (await BudgetedAsync(c.CompanyId, c.Year.Id, int.MaxValue, ct).ConfigureAwait(false)).Keys.Select(k => k.Account)];
        return [.. (await ActualsAsync(c.CompanyId, c.Year.StartDate, c.AsAt, ct).ConfigureAwait(false)).Where(a => accounts.ContainsKey(a.Key.Account) && !budgeted.Contains(a.Key.Account))
            .GroupBy(a => a.Key.Account).Select(g => (Account: g.Key, Amount: g.Sum(x => x.Value.Amount), Postings: g.Sum(x => x.Value.Postings))).Where(x => x.Amount != 0m)
            .OrderByDescending(x => x.Amount).Select(x => Row(("Account", x.Account), ("Name", accounts[x.Account].Name), ("Postings", x.Postings), ("Actual", x.Amount)))];
    }

    private static string Rag(decimal? used) => used switch { null => "—", < 90m => "Green", <= 100m => "Amber", _ => "Red" };

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> UtilisationAsync(Context c, bool exceptionsOnly, CancellationToken ct)
    {
        Dictionary<string, string> names = await NamesAsync(c.CompanyId, ct).ConfigureAwait(false);
        Dictionary<string, decimal> budget = (await BudgetedAsync(c.CompanyId, c.Year.Id, c.Period, ct).ConfigureAwait(false)).GroupBy(b => b.Key.Account).ToDictionary(g => g.Key, g => g.Sum(x => x.Value));
        Dictionary<string, decimal> actual = (await ActualsAsync(c.CompanyId, c.Year.StartDate, c.AsAt, ct).ConfigureAwait(false)).GroupBy(a => a.Key.Account).ToDictionary(g => g.Key, g => g.Sum(x => x.Value.Amount));
        var rows = budget.Select(b => (Account: b.Key, Budget: b.Value, Actual: actual.GetValueOrDefault(b.Key), Used: b.Value == 0m ? (decimal?)null : actual.GetValueOrDefault(b.Key) * 100m / b.Value))
            .Where(x => !exceptionsOnly || Rag(x.Used) is "Amber" or "Red")
            .OrderBy(x => exceptionsOnly ? -(x.Used ?? 0m) : 0m).ThenBy(x => x.Account, StringComparer.Ordinal);
        return [.. rows.Select(x => exceptionsOnly
            ? Row(("Account", x.Account), ("Name", names.GetValueOrDefault(x.Account, "?")), ("Budget", Money(x.Budget)), ("Used", x.Used is decimal u ? u.ToString("0.0", CultureInfo.InvariantCulture) + "%" : "—"),
                ("Rag", Rag(x.Used)), ("Over", Math.Max(0m, x.Actual - x.Budget)))
            : Row(("Account", x.Account), ("Name", names.GetValueOrDefault(x.Account, "?")), ("Budget", Money(x.Budget)), ("Used", x.Used is decimal v ? v.ToString("0.0", CultureInfo.InvariantCulture) + "%" : "—"),
                ("Rag", Rag(x.Used)), ("Actual", x.Actual)))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> CommentaryAsync(Context c, CancellationToken ct)
    {
        Dictionary<string, string> names = await NamesAsync(c.CompanyId, ct).ConfigureAwait(false);
        Dictionary<long, string> users = await _db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Username, ct).ConfigureAwait(false);
        List<VarianceComment> comments = await _db.Set<VarianceComment>().AsNoTracking().Where(v => v.CompanyId == c.CompanyId && v.FiscalYearId == c.Year.Id).OrderBy(v => v.AsAtPeriod).ThenBy(v => v.AccountCode).ToListAsync(ct).ConfigureAwait(false);
        List<IReadOnlyDictionary<string, object?>> rows = [];
        foreach (VarianceComment comment in comments)
        {
            decimal budget = (await BudgetedAsync(c.CompanyId, c.Year.Id, comment.AsAtPeriod, ct).ConfigureAwait(false)).Where(b => b.Key.Account == comment.AccountCode).Sum(b => b.Value);
            DateOnly periodEnd = c.Year.StartDate.AddMonths(comment.AsAtPeriod).AddDays(-1);
            decimal actual = (await ActualsAsync(c.CompanyId, c.Year.StartDate, periodEnd, ct).ConfigureAwait(false)).Where(a => a.Key.Account == comment.AccountCode).Sum(a => a.Value.Amount);
            rows.Add(Row(("Period", comment.AsAtPeriod), ("Account", comment.AccountCode), ("Name", names.GetValueOrDefault(comment.AccountCode, "?")), ("Budget", Money(budget)), ("Actual", Money(actual)),
                ("Author", users.GetValueOrDefault(comment.AuthorUserId, $"user {comment.AuthorUserId}")), ("Comment", comment.Comment)));
        }

        return rows;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> DepartmentsAsync(Context c, CancellationToken ct)
    {
        Dictionary<string, AccountType> types = await _db.Accounts.AsNoTracking().Where(a => a.CompanyId == c.CompanyId).ToDictionaryAsync(a => a.AccountCode, a => a.Type, ct).ConfigureAwait(false);
        var budget = await BudgetedAsync(c.CompanyId, c.Year.Id, c.Period, ct).ConfigureAwait(false);
        var actuals = await ActualsAsync(c.CompanyId, c.Year.StartDate, c.AsAt, ct).ConfigureAwait(false);
        IEnumerable<string> departments = budget.Keys.Select(k => k.Department).Concat(actuals.Keys.Select(k => k.Department)).Where(d => d.Length > 0).Distinct().Order(StringComparer.Ordinal);
        decimal Sum<T>(IEnumerable<KeyValuePair<(string Account, string Department), T>> source, string department, AccountType type, Func<T, decimal> amount)
            => source.Where(x => x.Key.Department == department && types.GetValueOrDefault(x.Key.Account) == type).Sum(x => amount(x.Value));
        return [.. departments.Select(d =>
        {
            decimal income = Sum(actuals, d, AccountType.Income, v => v.Amount), expense = Sum(actuals, d, AccountType.Expense, v => v.Amount);
            return Row(("Department", d), ("IncomeBudget", Money(Sum(budget, d, AccountType.Income, v => v))), ("Income", Money(income)), ("ExpenseBudget", Money(Sum(budget, d, AccountType.Expense, v => v))),
                ("Expense", Money(expense)), ("Surplus", income - expense));
        })];
    }

    // Accuracy: within 5% excellent, within 10% good, within 20% fair, else poor.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> AccuracyAsync(Context c, CancellationToken ct)
    {
        FiscalYear? prior = await YearAsync(c.CompanyId, c.Year.StartDate.AddDays(-1), ct).ConfigureAwait(false);
        if (prior is null)
        {
            return [];
        }

        Dictionary<string, string> names = await NamesAsync(c.CompanyId, ct).ConfigureAwait(false);
        Dictionary<string, decimal> budget = (await BudgetedAsync(c.CompanyId, prior.Id, int.MaxValue, ct).ConfigureAwait(false)).GroupBy(b => b.Key.Account).ToDictionary(g => g.Key, g => g.Sum(x => x.Value));
        Dictionary<string, decimal> actual = (await ActualsAsync(c.CompanyId, prior.StartDate, prior.EndDate, ct).ConfigureAwait(false)).GroupBy(a => a.Key.Account).ToDictionary(g => g.Key, g => g.Sum(x => x.Value.Amount));
        return [.. budget.OrderBy(b => b.Key, StringComparer.Ordinal).Select(b =>
        {
            decimal a = actual.GetValueOrDefault(b.Key);
            decimal? percent = b.Value == 0m ? null : (a - b.Value) * 100m / b.Value;
            string accuracy = percent is not decimal p ? "—" : Math.Abs(p) <= 5m ? "Excellent" : Math.Abs(p) <= 10m ? "Good" : Math.Abs(p) <= 20m ? "Fair" : "Poor";
            return Row(("Account", b.Key), ("Name", names.GetValueOrDefault(b.Key, "?")), ("Budget", Money(b.Value)), ("Actual", Money(a)),
                ("Percent", percent is decimal q ? q.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + "%" : "—"), ("Accuracy", accuracy), ("Variance", a - b.Value));
        })];
    }

    private Task<Dictionary<string, string>> NamesAsync(long companyId, CancellationToken ct)
        => _db.Accounts.AsNoTracking().IgnoreQueryFilters().Where(a => a.CompanyId == companyId).ToDictionaryAsync(a => a.AccountCode, a => a.Name, ct);

    // A department's status is its latest draft submission's; approved once an in-force budget exists for it.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> PrepStatusAsync(Context c, DateOnly? deadline, CancellationToken ct)
    {
        List<Budget> budgets = await BudgetsAsync(c.CompanyId, c.Year.Id, false, ct).ConfigureAwait(false);
        List<string> establishment = await _db.EstablishmentPosts.AsNoTracking().Where(p => p.CompanyId == c.CompanyId).Select(p => p.Department).Distinct().ToListAsync(ct).ConfigureAwait(false);
        IEnumerable<string> departments = budgets.Where(b => b.Department != null).Select(b => b.Department!).Concat(establishment).Distinct().Order(StringComparer.Ordinal);
        return [.. departments.Select(d =>
        {
            Budget? draft = budgets.Where(b => b.Department == d && b.Type == BudgetType.DraftSubmission).OrderBy(b => b.VersionNumber).LastOrDefault();
            bool approved = budgets.Any(b => b.Department == d && b.Type != BudgetType.DraftSubmission && InForce.Contains(b.Status));
            string status = approved ? "Approved" : draft is null ? "Not started" : draft.Status switch
            {
                BudgetStatus.Draft => "In progress",
                BudgetStatus.Submitted => "Submitted",
                _ => draft.Status.ToString(),
            };
            DateTimeOffset? changed = draft is null ? null : (draft.ModifiedOnUtc ?? draft.CreatedOnUtc);
            bool done = status is "Submitted" or "Approved";
            return Row(("Department", d), ("Status", status), ("Version", draft?.VersionNumber.ToString(CultureInfo.InvariantCulture) ?? "—"),
                ("Updated", changed?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "—"), ("Deadline", deadline?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "—"),
                ("Days", deadline is DateOnly due && !done ? (due.DayNumber - c.AsAt.DayNumber).ToString(CultureInfo.InvariantCulture) : "—"), ("Requested", draft?.Lines.Sum(l => l.Amount) ?? 0m));
        })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> CutsTrailAsync(Context c, CancellationToken ct)
    {
        Dictionary<string, string> names = await NamesAsync(c.CompanyId, ct).ConfigureAwait(false);
        List<Budget> budgets = await BudgetsAsync(c.CompanyId, c.Year.Id, false, ct).ConfigureAwait(false);
        List<Budget> drafts = [.. budgets.Where(b => b.Type == BudgetType.DraftSubmission)];
        List<Budget> approved = [.. budgets.Where(b => b.Type != BudgetType.DraftSubmission && b.IsActive && InForce.Contains(b.Status))];
        var keys = drafts.SelectMany(b => b.Lines.Select(l => (Department: b.Department ?? string.Empty, l.AccountCode))).Distinct().OrderBy(k => k.Department, StringComparer.Ordinal).ThenBy(k => k.AccountCode, StringComparer.Ordinal);
        decimal Total(Budget? b, string account) => b?.Lines.Where(l => l.AccountCode == account).Sum(l => l.Amount) ?? 0m;
        return [.. keys.Select(k =>
        {
            List<Budget> own = [.. drafts.Where(b => (b.Department ?? string.Empty) == k.Department).OrderBy(b => b.VersionNumber)];
            decimal requested = Total(own.FirstOrDefault(), k.AccountCode), recommended = Total(own.LastOrDefault(), k.AccountCode);
            decimal granted = approved.Where(b => (b.Department ?? string.Empty) == k.Department).Sum(b => Total(b, k.AccountCode));
            return Row(("Department", k.Department.Length == 0 ? "All" : k.Department), ("Account", k.AccountCode), ("Name", names.GetValueOrDefault(k.AccountCode, "?")), ("Requested", Money(requested)),
                ("Recommended", Money(recommended)), ("Approved", Money(granted)), ("Cut", requested - granted));
        })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ApprovedAsync(Context c, CancellationToken ct)
    {
        Dictionary<string, string> names = await NamesAsync(c.CompanyId, ct).ConfigureAwait(false);
        List<Budget> budgets = await BudgetsAsync(c.CompanyId, c.Year.Id, true, ct).ConfigureAwait(false);
        decimal Quarter(IEnumerable<BudgetLine> lines, int q) => lines.Where(l => (l.Period - 1) / 3 == q).Sum(l => l.Amount);
        return [.. budgets.Where(b => b.Type != BudgetType.DraftSubmission).SelectMany(b => b.Lines.Select(l => (Department: b.Department ?? "All", Line: l)))
            .GroupBy(x => (x.Line.AccountCode, x.Department)).OrderBy(g => g.Key.AccountCode, StringComparer.Ordinal).ThenBy(g => g.Key.Department, StringComparer.Ordinal)
            .Select(g =>
            {
                List<BudgetLine> lines = [.. g.Select(x => x.Line)];
                return Row(("Account", g.Key.AccountCode), ("Name", names.GetValueOrDefault(g.Key.AccountCode, "?")), ("Department", g.Key.Department), ("Q1", Money(Quarter(lines, 0))), ("Q2", Money(Quarter(lines, 1))),
                    ("Q3", Money(Quarter(lines, 2))), ("Q4", Money(Quarter(lines, 3))), ("Total", lines.Sum(l => l.Amount)));
            })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ReconciliationAsync(Context c, CancellationToken ct)
        => [.. (await _control.VoteBookAsync(c.CompanyId, c.Year.Id, 12, ct).ConfigureAwait(false)).OrderBy(v => v.AccountCode, StringComparer.Ordinal)
            .Select(v => Row(("Account", v.AccountCode), ("Name", v.Name), ("Original", Money(v.Original)), ("Supplementary", Money(v.Supplementary)), ("Virements", Money(v.Virements)), ("Rephasing", "—"),
                ("Current", v.Appropriation)))];

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> SupplementaryAsync(Context c, CancellationToken ct)
    {
        List<Budget> budgets = await BudgetsAsync(c.CompanyId, c.Year.Id, false, ct).ConfigureAwait(false);
        Dictionary<string, decimal> original = budgets.Where(b => b.Type == BudgetType.OriginalApproved && b.Status != BudgetStatus.Draft && b.Status != BudgetStatus.Submitted)
            .OrderBy(b => b.VersionNumber).Take(1).SelectMany(b => b.Lines).Where(l => l.VirementId == null).GroupBy(l => l.AccountCode).ToDictionary(g => g.Key, g => g.Sum(l => l.Amount));
        return [.. budgets.Where(b => b.Type == BudgetType.SupplementaryRevised).OrderBy(b => b.VersionNumber).SelectMany(b => b.Lines.Where(l => l.VirementId == null).GroupBy(l => l.AccountCode)
            .OrderBy(g => g.Key, StringComparer.Ordinal).Select(g =>
            {
                decimal was = original.GetValueOrDefault(g.Key), now = g.Sum(l => l.Amount);
                return Row(("Budget", $"{b.Label} v{b.VersionNumber}"), ("Status", b.Status.ToString()), ("Account", g.Key), ("Original", Money(was)), ("Revised", Money(now)), ("Reason", b.Reason ?? string.Empty), ("Change", now - was));
            }))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> CarryForwardAsync(Context c, CancellationToken ct)
    {
        Dictionary<long, string> users = await _db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Username, ct).ConfigureAwait(false);
        List<BudgetCommitment> closed = await _db.BudgetCommitments.AsNoTracking()
            .Where(x => x.CompanyId == c.CompanyId && x.FiscalYearId == c.Year.Id && (x.Status == CommitmentStatus.Lapsed || x.Status == CommitmentStatus.CarriedForward)).OrderBy(x => x.Reference).ToListAsync(ct).ConfigureAwait(false);
        return [.. closed.Select(x => Row(("Reference", x.Reference), ("Account", x.AccountCode), ("Department", x.Department ?? "All"), ("Period", x.Period), ("Outcome", x.Status == CommitmentStatus.Lapsed ? "Lapsed" : "Carried forward"),
            ("ApprovedBy", x.YearEndApprovedByUserId is long u ? users.GetValueOrDefault(u, $"user {u}") : "—"), ("Amount", x.Amount)))];
    }

    // The forecast for the remaining periods comes from the latest reforecast round when there is one, else from the budget itself.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> OutturnAsync(Context c, CancellationToken ct)
    {
        Dictionary<string, string> names = await NamesAsync(c.CompanyId, ct).ConfigureAwait(false);
        List<Budget> budgets = await BudgetsAsync(c.CompanyId, c.Year.Id, false, ct).ConfigureAwait(false);
        List<Budget> approved = [.. budgets.Where(b => b.Type is BudgetType.OriginalApproved or BudgetType.SupplementaryRevised && b.IsActive && InForce.Contains(b.Status))];
        Budget? reforecast = budgets.Where(b => b.Type == BudgetType.Reforecast).OrderBy(b => b.VersionNumber).LastOrDefault();
        var actuals = await ActualsAsync(c.CompanyId, c.Year.StartDate, c.AsAt, ct).ConfigureAwait(false);
        var keys = approved.SelectMany(b => b.Lines.Select(l => (l.AccountCode, Department: b.Department ?? string.Empty))).Distinct().OrderBy(k => k.AccountCode, StringComparer.Ordinal).ThenBy(k => k.Department, StringComparer.Ordinal);
        return [.. keys.Select(k =>
        {
            List<BudgetLine> lines = [.. approved.Where(b => (b.Department ?? string.Empty) == k.Department).SelectMany(b => b.Lines).Where(l => l.AccountCode == k.AccountCode)];
            decimal budget = lines.Sum(l => l.Amount), actual = ActualFor(actuals, (k.AccountCode, k.Department));
            List<BudgetLine> forecastLines = [.. (reforecast?.Lines.Where(l => l.AccountCode == k.AccountCode && l.Period > c.Period) ?? []).Where(_ => (reforecast!.Department ?? string.Empty) == k.Department || reforecast!.Department == null)];
            decimal forecast = forecastLines.Count > 0 ? forecastLines.Sum(l => l.Amount) : lines.Where(l => l.Period > c.Period).Sum(l => l.Amount);
            return Row(("Account", k.AccountCode), ("Name", names.GetValueOrDefault(k.AccountCode, "?")), ("Department", k.Department.Length == 0 ? "All" : k.Department), ("Budget", Money(budget)), ("Actual", Money(actual)),
                ("Forecast", Money(forecast)), ("Outturn", Money(actual + forecast)), ("Variance", budget - actual - forecast));
        })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ForecastMovementAsync(Context c, CancellationToken ct)
    {
        Dictionary<string, string> names = await NamesAsync(c.CompanyId, ct).ConfigureAwait(false);
        List<Budget> rounds = [.. (await BudgetsAsync(c.CompanyId, c.Year.Id, false, ct).ConfigureAwait(false)).Where(b => b.Type == BudgetType.Reforecast).OrderBy(b => b.VersionNumber)];
        if (rounds.Count < 2)
        {
            return [];
        }

        Budget previous = rounds[^2], latest = rounds[^1];
        decimal Total(Budget b, string account) => b.Lines.Where(l => l.AccountCode == account).Sum(l => l.Amount);
        return [.. previous.Lines.Select(l => l.AccountCode).Concat(latest.Lines.Select(l => l.AccountCode)).Distinct().Order(StringComparer.Ordinal).Select(a => Row(("Account", a),
            ("Name", names.GetValueOrDefault(a, "?")), ("Previous", Money(Total(previous, a))), ("Latest", Money(Total(latest, a))), ("Reason", latest.Reason ?? string.Empty), ("Movement", Total(latest, a) - Total(previous, a))))];
    }

    // Cash budget lines carry the planned net cash per period; actual is the net movement on the bank accounts' ledger accounts.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> CashAsync(Context c, CancellationToken ct)
    {
        Budget? cash = (await BudgetsAsync(c.CompanyId, c.Year.Id, false, ct).ConfigureAwait(false)).Where(b => b.Type == BudgetType.Cash).OrderBy(b => b.VersionNumber).LastOrDefault();
        List<string> banks = [.. (await _db.BankAccounts.AsNoTracking().Where(b => b.CompanyId == c.CompanyId).Select(b => b.GlAccountCode).ToListAsync(ct).ConfigureAwait(false))
            .Concat(cash?.Lines.Select(l => l.AccountCode) ?? []).Distinct()];
        var movements = await (from l in _db.Set<JournalLine>().AsNoTracking()
                               join j in _db.Journals.AsNoTracking() on l.JournalEntryId equals j.Id
                               where j.CompanyId == c.CompanyId && j.Status == JournalStatus.Posted && j.PostingDate >= c.Year.StartDate && j.PostingDate <= c.AsAt && banks.Contains(l.AccountCode)
                               select new { j.PostingDate, Net = l.BaseDebit - l.BaseCredit }).ToListAsync(ct).ConfigureAwait(false);
        return [.. Enumerable.Range(1, c.Period).Select(p =>
        {
            DateOnly month = c.Year.StartDate.AddMonths(p - 1);
            decimal budget = cash?.Lines.Where(l => l.Period == p).Sum(l => l.Amount) ?? 0m;
            decimal actual = movements.Where(m => PeriodOf(c.Year, m.PostingDate) == p).Sum(m => m.Net);
            return Row(("Period", p), ("Month", month.ToString("yyyy-MM", CultureInfo.InvariantCulture)), ("Budget", Money(budget)), ("Actual", Money(actual)), ("Variance", Math.Round(actual - budget, 2)));
        })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> CapitalProjectsAsync(Context c, CancellationToken ct)
    {
        List<Core.FixedAssets.CapitalProject> projects = await _db.CapitalProjects.AsNoTracking().Include(p => p.Costs).Where(p => p.CompanyId == c.CompanyId && p.OpenedOn <= c.AsAt).OrderBy(p => p.Code)
            .ToListAsync(ct).ConfigureAwait(false);
        List<BudgetCommitment> commitments = await OpenCommitmentsAsync(c, ct).ConfigureAwait(false);
        return [.. projects.Select(p =>
        {
            decimal actual = p.Costs.Where(x => x.Date <= c.AsAt).Sum(x => x.Amount);
            decimal committed = commitments.Where(x => x.AccountCode == p.CwipAccountCode && x.Reference.StartsWith(p.Code, StringComparison.OrdinalIgnoreCase)).Sum(x => x.Amount);
            return Row(("Project", p.Code), ("Name", p.Name), ("Status", p.CapitalisedOn is DateOnly on && on <= c.AsAt ? "Capitalised" : "In progress"), ("Budget", Money(p.Budget)), ("Actual", Money(actual)),
                ("Committed", Money(committed)), ("Spent", p.Budget == 0m ? "—" : (actual * 100m / p.Budget).ToString("0.0", CultureInfo.InvariantCulture) + "%"), ("ToComplete", Math.Max(0m, p.Budget - actual - committed)));
        })];
    }

    private static Budget? LatestDraft(IEnumerable<Budget> budgets, string department)
        => budgets.Where(b => b.Department == department && b.Type == BudgetType.DraftSubmission).OrderBy(b => b.VersionNumber).LastOrDefault();

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> CeilingGapAsync(Context c, CancellationToken ct)
    {
        List<Budget> budgets = await BudgetsAsync(c.CompanyId, c.Year.Id, false, ct).ConfigureAwait(false);
        List<BudgetCeiling> ceilings = await _db.BudgetCeilings.AsNoTracking().Where(x => x.CompanyId == c.CompanyId && x.FiscalYearId == c.Year.Id).OrderBy(x => x.Department).ToListAsync(ct).ConfigureAwait(false);
        return [.. ceilings.Select(x =>
        {
            Budget? draft = LatestDraft(budgets, x.Department);
            decimal requested = draft?.Lines.Sum(l => l.Amount) ?? 0m, gap = requested - x.Amount;
            return Row(("Department", x.Department), ("Ceiling", Money(x.Amount)), ("Requested", draft is null ? "not submitted" : Money(requested)), ("Status", draft is null ? "No draft" : gap > 0m ? "Over ceiling" : "Within"),
                ("Gap", draft is null ? 0m : gap));
        })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> HierarchyAsync(Context c, string level, CancellationToken ct)
    {
        List<Budget> budgets = await BudgetsAsync(c.CompanyId, c.Year.Id, false, ct).ConfigureAwait(false);
        Dictionary<string, DepartmentUnit> units = await _db.DepartmentUnits.AsNoTracking().Where(u => u.CompanyId == c.CompanyId).ToDictionaryAsync(u => u.Department, ct).ConfigureAwait(false);
        List<(string Department, decimal Requested)> drafts = [.. budgets.Where(b => b.Type == BudgetType.DraftSubmission && b.Department != null).Select(b => b.Department!).Distinct()
            .Select(d => (d, LatestDraft(budgets, d)!.Lines.Sum(l => l.Amount)))];
        string Unit(string department) => level.ToUpperInvariant() switch
        {
            "DEPARTMENT" => department,
            "DIVISION" => units.GetValueOrDefault(department)?.Division ?? "(unplaced)",
            "COMPANY" => "Company",
            _ => units.GetValueOrDefault(department)?.Directorate ?? "(unplaced)",
        };
        return [.. drafts.GroupBy(d => Unit(d.Department)).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => Row(("Level", level), ("Unit", g.Key), ("Departments", g.Count()), ("Requested", g.Sum(d => d.Requested))))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> DriversAsync(Context c, CancellationToken ct)
    {
        List<BudgetLine> lines = [.. (await BudgetsAsync(c.CompanyId, c.Year.Id, false, ct).ConfigureAwait(false)).SelectMany(b => b.Lines).Where(l => l.DriverCode != null)];
        return [.. (await _db.BudgetDrivers.AsNoTracking().Where(d => d.CompanyId == c.CompanyId && d.FiscalYearId == c.Year.Id).OrderBy(d => d.Code).ToListAsync(ct).ConfigureAwait(false))
            .Select(d => Row(("Driver", d.Code), ("Name", d.Name), ("Unit", d.Unit), ("Value", d.Value.ToString("0.####", CultureInfo.InvariantCulture)),
                ("Lines", lines.Where(l => l.DriverCode == d.Code).Select(l => (l.BudgetId, l.AccountCode)).Distinct().Count()), ("Amount", lines.Where(l => l.DriverCode == d.Code).Sum(l => l.Amount))))];
    }

    // A driver-based line moves in proportion to its driver: revised = current × new value / current value.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> WhatIfAsync(Context c, string? driverCode, decimal? newValue, CancellationToken ct)
    {
        string code = (driverCode ?? string.Empty).Trim().ToUpperInvariant();
        BudgetDriver? driver = await _db.BudgetDrivers.AsNoTracking().FirstOrDefaultAsync(d => d.CompanyId == c.CompanyId && d.FiscalYearId == c.Year.Id && d.Code == code, ct).ConfigureAwait(false);
        if (driver is null || newValue is null || driver.Value == 0m)
        {
            return [];
        }

        decimal factor = newValue.Value / driver.Value;
        List<Budget> budgets = await BudgetsAsync(c.CompanyId, c.Year.Id, false, ct).ConfigureAwait(false);
        List<IReadOnlyDictionary<string, object?>> rows = [.. budgets.SelectMany(b => b.Lines.Where(l => l.DriverCode == code).GroupBy(l => l.AccountCode).Select(g => (Budget: b, Account: g.Key, Amount: g.Sum(l => l.Amount))))
            .OrderBy(x => x.Budget.Label, StringComparer.Ordinal).ThenBy(x => x.Account, StringComparer.Ordinal)
            .Select(x => Row(("Budget", $"{x.Budget.Label} v{x.Budget.VersionNumber}"), ("Account", x.Account), ("Department", x.Budget.Department ?? "All"), ("Current", Money(x.Amount)),
                ("Revised", Money(Math.Round(x.Amount * factor, 2))), ("Change", Math.Round(x.Amount * factor, 2) - x.Amount)))];
        return rows;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> BasisAsync(Context c, CancellationToken ct)
        => [.. (await BudgetsAsync(c.CompanyId, c.Year.Id, false, ct).ConfigureAwait(false)).OrderBy(b => b.Label, StringComparer.Ordinal).ThenBy(b => b.VersionNumber)
            .SelectMany(b => b.Lines.GroupBy(l => (l.AccountCode, l.Basis, l.DriverCode)).OrderBy(g => g.Key.AccountCode, StringComparer.Ordinal)
                .Select(g => Row(("Budget", $"{b.Label} v{b.VersionNumber}"), ("Department", b.Department ?? "All"), ("Account", g.Key.AccountCode), ("Basis", g.Key.Basis.ToString()), ("Driver", g.Key.DriverCode ?? "—"),
                    ("Amount", g.Sum(l => l.Amount)))))];

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> StaffAsync(Context c, string staffAccount, decimal vacancyPercent, CancellationToken ct)
    {
        List<Core.Payroll.EstablishmentPost> posts = await _db.EstablishmentPosts.AsNoTracking().Where(p => p.CompanyId == c.CompanyId).ToListAsync(ct).ConfigureAwait(false);
        List<Core.Payroll.Employee> staff = await _db.Employees.AsNoTracking().Where(e => e.CompanyId == c.CompanyId && e.EngagementDate <= c.AsAt && (e.TerminationDate == null || e.TerminationDate >= c.AsAt))
            .ToListAsync(ct).ConfigureAwait(false);
        List<Budget> budgets = await BudgetsAsync(c.CompanyId, c.Year.Id, true, ct).ConfigureAwait(false);
        return [.. posts.GroupBy(p => p.Department).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g =>
        {
            int approved = g.Sum(p => p.ApprovedPosts);
            decimal cost = g.Sum(p => p.ApprovedPosts * p.AnnualCostPerPost), expected = Math.Round(cost * (1m - (vacancyPercent / 100m)), 2);
            decimal budgeted = budgets.Where(b => b.Department == g.Key).SelectMany(b => b.Lines).Where(l => l.AccountCode == staffAccount).Sum(l => l.Amount);
            return Row(("Department", g.Key), ("Posts", approved), ("Filled", staff.Count(e => e.Department == g.Key)), ("Establishment", Money(cost)),
                ("Vacancy", vacancyPercent.ToString("0.#", CultureInfo.InvariantCulture) + "%"), ("Expected", Money(expected)), ("Budgeted", Money(budgeted)), ("Difference", budgeted - expected));
        })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> RevenueBuildAsync(Context c, CancellationToken ct)
    {
        Dictionary<string, string> names = await NamesAsync(c.CompanyId, ct).ConfigureAwait(false);
        List<string> income = await _db.Accounts.AsNoTracking().Where(a => a.CompanyId == c.CompanyId && a.Type == AccountType.Income).Select(a => a.AccountCode).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<string, BudgetDriver> drivers = await _db.BudgetDrivers.AsNoTracking().Where(d => d.CompanyId == c.CompanyId && d.FiscalYearId == c.Year.Id).ToDictionaryAsync(d => d.Code, ct).ConfigureAwait(false);
        return [.. (await BudgetsAsync(c.CompanyId, c.Year.Id, false, ct).ConfigureAwait(false)).OrderBy(b => b.Label, StringComparer.Ordinal).ThenBy(b => b.VersionNumber)
            .SelectMany(b => b.Lines.Where(l => income.Contains(l.AccountCode) && l.Basis == BudgetBasis.DriverBased && l.DriverCode != null).GroupBy(l => (l.AccountCode, l.DriverCode))
                .OrderBy(g => g.Key.AccountCode, StringComparer.Ordinal).Select(g =>
                {
                    decimal amount = g.Sum(l => l.Amount), volume = drivers.GetValueOrDefault(g.Key.DriverCode!)?.Value ?? 0m;
                    return Row(("Budget", $"{b.Label} v{b.VersionNumber}"), ("Account", g.Key.AccountCode), ("Name", names.GetValueOrDefault(g.Key.AccountCode, "?")), ("Driver", g.Key.DriverCode),
                        ("Volume", volume.ToString("0.####", CultureInfo.InvariantCulture)), ("Rate", volume == 0m ? "—" : Money(amount / volume)), ("Amount", amount));
                }))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> HoldersAsync(Context c, CancellationToken ct)
    {
        List<DepartmentUnit> holders = await _db.DepartmentUnits.AsNoTracking().Where(u => u.CompanyId == c.CompanyId && u.BudgetHolder != null).OrderBy(u => u.BudgetHolder).ThenBy(u => u.Department)
            .ToListAsync(ct).ConfigureAwait(false);
        var budgeted = await BudgetedAsync(c.CompanyId, c.Year.Id, int.MaxValue, ct).ConfigureAwait(false);
        var actuals = await ActualsAsync(c.CompanyId, c.Year.StartDate, c.AsAt, ct).ConfigureAwait(false);
        List<BudgetCommitment> commitments = await OpenCommitmentsAsync(c, ct).ConfigureAwait(false);
        return [.. holders.SelectMany(h => budgeted.Where(b => b.Key.Department == h.Department).OrderBy(b => b.Key.Account, StringComparer.Ordinal).Select(b =>
        {
            decimal actual = ActualFor(actuals, b.Key), committed = commitments.Where(x => x.AccountCode == b.Key.Account && x.Department == h.Department).Sum(x => x.Amount);
            return Row(("Holder", h.BudgetHolder), ("Department", h.Department), ("Account", b.Key.Account), ("Budget", Money(b.Value)), ("Actual", Money(actual)), ("Committed", Money(committed)),
                ("Used", b.Value == 0m ? "—" : ((actual + committed) * 100m / b.Value).ToString("0.0", CultureInfo.InvariantCulture) + "%"), ("Available", b.Value - actual - committed));
        }))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> CapitalPhasingAsync(Context c, CancellationToken ct)
    {
        List<Budget> capital = [.. (await BudgetsAsync(c.CompanyId, c.Year.Id, false, ct).ConfigureAwait(false)).Where(b => b.Type == BudgetType.Capital && b.Status != BudgetStatus.Superseded)];
        List<IReadOnlyDictionary<string, object?>> rows = [];
        foreach (Budget b in capital.OrderBy(b => b.Label, StringComparer.Ordinal))
        {
            foreach (IGrouping<string, BudgetLine> vote in b.Lines.GroupBy(l => l.AccountCode).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                decimal total = vote.Sum(l => l.Amount), running = 0m;
                foreach (IGrouping<int, BudgetLine> month in vote.GroupBy(l => l.Period).OrderBy(g => g.Key))
                {
                    decimal amount = month.Sum(l => l.Amount);
                    running += amount;
                    rows.Add(Row(("Budget", $"{b.Label} v{b.VersionNumber}"), ("Account", vote.Key), ("Period", month.Key), ("Month", c.Year.StartDate.AddMonths(month.Key - 1).ToString("yyyy-MM", CultureInfo.InvariantCulture)),
                        ("Cumulative", Money(running)), ("Share", total == 0m ? "—" : (running * 100m / total).ToString("0.0", CultureInfo.InvariantCulture) + "%"), ("Amount", amount)));
                }
            }
        }

        return rows;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ApprovalAuditAsync(Context c, CancellationToken ct)
    {
        Dictionary<string, string> labels = (await BudgetsAsync(c.CompanyId, c.Year.Id, false, ct).ConfigureAwait(false))
            .ToDictionary(b => b.Id.ToString(CultureInfo.InvariantCulture), b => $"{b.Label} v{b.VersionNumber}" + (b.Department is null ? string.Empty : $" ({b.Department})"));
        // The year's budgets bound the report; their changes are taken whenever they were made.
        List<Core.Audit.ChangeLogEntry> changes = [.. (await _db.ChangeLog.AsNoTracking().Where(l => l.CompanyId == c.CompanyId && l.EntityType == "Budget").OrderBy(l => l.Id)
                .ToListAsync(ct).ConfigureAwait(false)).Where(l => l.EntityId != null && labels.ContainsKey(l.EntityId) && (l.Action == Core.Audit.AuditAction.Insert || l.ChangedFields.Contains("Status")))];
        static string Status(string? json)
        {
            if (json is null)
            {
                return "—";
            }

            using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("Status", out System.Text.Json.JsonElement v) ? (v.ValueKind == System.Text.Json.JsonValueKind.Number ? ((BudgetStatus)v.GetInt32()).ToString() : v.ToString()) : "—";
        }

        Dictionary<string, DateTimeOffset> last = [];
        List<IReadOnlyDictionary<string, object?>> rows = [];
        foreach (Core.Audit.ChangeLogEntry change in changes)
        {
            string days = last.TryGetValue(change.EntityId!, out DateTimeOffset previous) ? ((int)(change.OccurredOnUtc - previous).TotalDays).ToString(CultureInfo.InvariantCulture) : "—";
            last[change.EntityId!] = change.OccurredOnUtc;
            rows.Add(Row(("When", change.OccurredOnUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)), ("Budget", labels[change.EntityId!]), ("Status", Status(change.NewValues)), ("User", change.UserName), ("Days", days)));
        }

        return rows;
    }

    // Rephasing: a vote whose annual total is the same in two consecutive versions but whose monthly amounts differ.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> RephasingAsync(Context c, CancellationToken ct)
    {
        List<Budget> budgets = await BudgetsAsync(c.CompanyId, c.Year.Id, false, ct).ConfigureAwait(false);
        List<IReadOnlyDictionary<string, object?>> rows = [];
        foreach (IGrouping<(string Label, string? Department), Budget> chain in budgets.GroupBy(b => (b.Label, b.Department)).OrderBy(g => g.Key.Label, StringComparer.Ordinal))
        {
            List<Budget> versions = [.. chain.OrderBy(b => b.VersionNumber)];
            for (int i = 1; i < versions.Count; i++)
            {
                Budget before = versions[i - 1], after = versions[i];
                foreach (string account in before.Lines.Select(l => l.AccountCode).Intersect(after.Lines.Select(l => l.AccountCode)).Order(StringComparer.Ordinal))
                {
                    Dictionary<int, decimal> was = before.Lines.Where(l => l.AccountCode == account).GroupBy(l => l.Period).ToDictionary(g => g.Key, g => g.Sum(l => l.Amount));
                    Dictionary<int, decimal> now = after.Lines.Where(l => l.AccountCode == account).GroupBy(l => l.Period).ToDictionary(g => g.Key, g => g.Sum(l => l.Amount));
                    if (was.Values.Sum() != now.Values.Sum())
                    {
                        continue;
                    }

                    List<(int Period, decimal Delta)> moves = [.. was.Keys.Union(now.Keys).Order().Select(p => (p, now.GetValueOrDefault(p) - was.GetValueOrDefault(p))).Where(x => x.Item2 != 0m)];
                    if (moves.Count > 0)
                    {
                        rows.Add(Row(("Budget", chain.Key.Label + (chain.Key.Department is null ? string.Empty : $" ({chain.Key.Department})")), ("Versions", $"v{before.VersionNumber} → v{after.VersionNumber}"), ("Account", account),
                            ("Annual", Money(now.Values.Sum())), ("Detail", string.Join(", ", moves.Select(m => $"P{m.Period} {(m.Delta > 0m ? "+" : string.Empty)}{m.Delta.ToString("0.00", CultureInfo.InvariantCulture)}"))),
                            ("Moved", moves.Where(m => m.Delta > 0m).Sum(m => m.Delta))));
                    }
                }
            }
        }

        return rows;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> CapitalUnspentAsync(Context c, CancellationToken ct)
    {
        Dictionary<string, string> names = await NamesAsync(c.CompanyId, ct).ConfigureAwait(false);
        List<Budget> capital = [.. (await BudgetsAsync(c.CompanyId, c.Year.Id, false, ct).ConfigureAwait(false)).Where(b => b.Type == BudgetType.Capital && b.Status is BudgetStatus.Approved or BudgetStatus.Active)];
        var actuals = await ActualsAsync(c.CompanyId, c.Year.StartDate, c.AsAt, ct).ConfigureAwait(false);
        return [.. capital.SelectMany(b => b.Lines).GroupBy(l => l.AccountCode).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g =>
        {
            decimal budget = g.Sum(l => l.Amount), spent = actuals.Where(a => a.Key.Account == g.Key).Sum(a => a.Value.Amount);
            return Row(("Account", g.Key), ("Name", names.GetValueOrDefault(g.Key, "?")), ("Budget", Money(budget)), ("Spent", Money(spent)),
                ("Used", budget == 0m ? "—" : (spent * 100m / budget).ToString("0.0", CultureInfo.InvariantCulture) + "%"), ("Unspent", Math.Max(0m, budget - spent)));
        })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> RollingAsync(Context c, int months, CancellationToken ct)
    {
        Dictionary<string, string> names = await NamesAsync(c.CompanyId, ct).ConfigureAwait(false);
        List<Budget> budgets = await BudgetsAsync(c.CompanyId, c.Year.Id, false, ct).ConfigureAwait(false);
        Budget? reforecast = budgets.Where(b => b.Type == BudgetType.Reforecast).OrderBy(b => b.VersionNumber).LastOrDefault();
        List<BudgetLine> inForce = [.. budgets.Where(b => b.Type is BudgetType.OriginalApproved or BudgetType.SupplementaryRevised && b.IsActive && InForce.Contains(b.Status)).SelectMany(b => b.Lines)];
        IEnumerable<string> accounts = inForce.Select(l => l.AccountCode).Concat(reforecast?.Lines.Select(l => l.AccountCode) ?? []).Distinct().Order(StringComparer.Ordinal);
        List<int> ahead = [.. Enumerable.Range(c.Period + 1, Math.Max(0, months)).Where(p => p <= 12)];
        return [.. accounts.SelectMany(a => ahead.Select(p =>
        {
            List<BudgetLine> fromForecast = [.. reforecast?.Lines.Where(l => l.AccountCode == a && l.Period == p) ?? []];
            bool forecast = fromForecast.Count > 0;
            decimal amount = forecast ? fromForecast.Sum(l => l.Amount) : inForce.Where(l => l.AccountCode == a && l.Period == p).Sum(l => l.Amount);
            return Row(("Account", a), ("Name", names.GetValueOrDefault(a, "?")), ("Month", c.Year.StartDate.AddMonths(p - 1).ToString("yyyy-MM", CultureInfo.InvariantCulture)), ("Source", forecast ? "Reforecast" : "Budget"),
                ("Amount", amount));
        }))];
    }

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
