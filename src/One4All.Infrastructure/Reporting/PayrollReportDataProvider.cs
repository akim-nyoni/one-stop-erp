using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Core.Budgeting;
using One4All.Core.Payroll;
using One4All.Core.Reporting;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Reporting;

/// <summary>Serves the PR-coded payroll reports (brief §14 O): every data command whose name starts with <c>Pyrl</c>.</summary>
public sealed class PayrollReportDataProvider : IReportDataProvider
{
    private static readonly string[] PayrollEntities = ["Employee", "EmployeePayElement", "PayElement", "EmployeeLoan", "EmployeeLeaveBalance", "Payslip"];

    private readonly One4AllDbContext _db;

    /// <summary>Creates the provider.</summary>
    /// <param name="db">The database context.</param>
    public PayrollReportDataProvider(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public bool CanHandle(string command) => command.StartsWith("Pyrl", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetRowsAsync(long companyId, string command, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct = default)
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        DateOnly asAt = DateParameter(parameters, "AsAtDate") ?? today;
        DateOnly toDate = DateParameter(parameters, "ToDate") ?? today;
        DateOnly fromDate = DateParameter(parameters, "FromDate") ?? new DateOnly(toDate.Year, 1, 1);
        return command.ToUpperInvariant() switch
        {
            "PYRLSUMMARY" => await SummaryAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "PYRLJOURNAL" => await JournalAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "PYRLVARIANCE" => await VarianceAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "PYRLTRACE" => await TraceAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "PYRLDENOMINATIONS" => await DenominationsAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "PYRLNETCONTROL" => await NetControlAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "PYRLEARNINGS" => await EarningsAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "PYRLTAX" => await TaxAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "PYRLLEAVE" => await LeaveAsync(companyId, ct).ConfigureAwait(false),
            "PYRLSALARYCHANGES" => await SalaryChangesAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "PYRLLABOURCOST" => await LabourCostAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "PYRLBUDGET" => await BudgetAsync(companyId, asAt, ct).ConfigureAwait(false),
            "PYRLREVENUESHARE" => await RevenueShareAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "PYRLGLRECON" => await GlReconAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "PYRLAUDIT" => await AuditAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "PYRLACCESSLOG" => await AccessLogAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "PYRLYTD" => await YtdAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            _ => [],
        };
    }

    private Task<List<Payslip>> PayslipsAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
        => _db.Payslips.AsNoTracking().Include(p => p.Lines).Where(p => p.CompanyId == companyId && p.PayDate >= fromDate && p.PayDate <= toDate).OrderBy(p => p.PayDate).ToListAsync(ct);

    private Task<Dictionary<long, Employee>> EmployeesAsync(long companyId, CancellationToken ct)
        => _db.Employees.AsNoTracking().IgnoreQueryFilters().Where(e => e.CompanyId == companyId).ToDictionaryAsync(e => e.Id, ct);

    private static string Name(Dictionary<long, Employee> employees, long id) => employees.TryGetValue(id, out Employee? e) ? $"{e.EmployeeNumber} {e.FullName}" : "?";

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> SummaryAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
        => [.. (await PayslipsAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false)).SelectMany(p => p.Lines.Select(l => (p.EmployeeId, Line: l)))
            .GroupBy(x => (x.Line.Type, x.Line.ElementCode)).OrderBy(g => g.Key.Type).ThenBy(g => g.Key.ElementCode, StringComparer.Ordinal)
            .Select(g => Row(("Type", g.Key.Type.ToString()), ("Element", g.Key.ElementCode), ("Description", g.First().Line.Description), ("Employees", g.Select(x => x.EmployeeId).Distinct().Count()),
                ("Amount", g.Sum(x => x.Line.Amount))))];

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> JournalAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        Dictionary<string, string> names = await AccountNamesAsync(companyId, ct).ConfigureAwait(false);
        List<JournalEntry> journals = await _db.Journals.AsNoTracking().Include(j => j.Lines)
            .Where(j => j.CompanyId == companyId && j.DocumentType == "PAYROLL" && j.Status != JournalStatus.Cancelled && j.PostingDate >= fromDate && j.PostingDate <= toDate)
            .OrderBy(j => j.PostingDate).ThenBy(j => j.JournalNumber).ToListAsync(ct).ConfigureAwait(false);
        return [.. journals.SelectMany(j => j.Lines.GroupBy(l => l.AccountCode).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => Row(("Journal", j.JournalNumber), ("Date", Date(j.PostingDate)),
            ("Account", g.Key), ("Name", names.GetValueOrDefault(g.Key, "?")), ("Debit", Money(g.Sum(l => l.BaseDebit))), ("Credit", Money(g.Sum(l => l.BaseCredit))), ("Net", g.Sum(l => l.BaseDebit - l.BaseCredit)))))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> VarianceAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        List<Payslip> slips = await PayslipsAsync(companyId, fromDate.AddMonths(-1), toDate, ct).ConfigureAwait(false);
        if (slips.Count == 0)
        {
            return [];
        }

        Dictionary<long, Employee> employees = await EmployeesAsync(companyId, ct).ConfigureAwait(false);
        DateOnly latest = slips.Max(p => p.PayDate);
        (int Year, int Month) current = (latest.Year, latest.Month), previous = (latest.AddMonths(-1).Year, latest.AddMonths(-1).Month);
        Dictionary<long, decimal> Net((int Year, int Month) month) => slips.Where(p => p.PayDate.Year == month.Year && p.PayDate.Month == month.Month).GroupBy(p => p.EmployeeId).ToDictionary(g => g.Key, g => g.Sum(p => p.NetPay));
        Dictionary<long, decimal> now = Net(current), before = Net(previous);
        return [.. now.Keys.Union(before.Keys).OrderBy(id => Name(employees, id), StringComparer.Ordinal).Select(id =>
        {
            bool isNew = !before.ContainsKey(id), gone = !now.ContainsKey(id);
            decimal a = before.GetValueOrDefault(id), b = now.GetValueOrDefault(id);
            string flag = isNew ? "Starter / new" : gone ? "Leaver / not paid" : a != 0m && Math.Abs((b - a) / a) > 0.10m ? "Changed > 10%" : string.Empty;
            return Row(("Employee", Name(employees, id)), ("Department", employees.GetValueOrDefault(id)?.Department ?? "—"), ("Previous", Money(a)), ("Current", Money(b)), ("Flag", flag), ("Change", b - a));
        })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> TraceAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        Dictionary<long, Employee> employees = await EmployeesAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<string, PayElement> elements = await _db.PayElements.AsNoTracking().IgnoreQueryFilters().Where(e => e.CompanyId == companyId).ToDictionaryAsync(e => e.Code, ct).ConfigureAwait(false);
        string Rule(PayElement? e) => e is null ? "—" : e.Basis switch
        {
            CalculationBasis.FixedAmount => Money(e.FixedAmount),
            CalculationBasis.Formula => e.Formula ?? "—",
            CalculationBasis.RatePerUnit => $"{e.Rate:0.####} per unit",
            _ => $"{e.Rate:0.####}%",
        };
        return [.. (await PayslipsAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false)).SelectMany(p => p.Lines.Select(l => Row(("Employee", Name(employees, p.EmployeeId)), ("Period", p.PeriodLabel),
            ("Element", l.ElementCode), ("Basis", elements.GetValueOrDefault(l.ElementCode)?.Basis.ToString() ?? "—"), ("Rule", Rule(elements.GetValueOrDefault(l.ElementCode))),
            ("Basic", Money(employees.GetValueOrDefault(p.EmployeeId)?.BasicSalary ?? 0m)), ("Amount", l.Amount))))];
    }

    // Greedy split into the base currency's denominations, largest first.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> DenominationsAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        CurrencyCode baseCurrency = await _db.Companies.AsNoTracking().Where(c => c.Id == companyId).Select(c => c.BaseCurrency).FirstAsync(ct).ConfigureAwait(false);
        List<decimal> faces = [.. (await _db.Currencies.AsNoTracking().Include(c => c.Denominations).Where(c => c.Code == baseCurrency).ToListAsync(ct).ConfigureAwait(false))
            .SelectMany(c => c.Denominations).Select(d => d.FaceValue).Where(f => f > 0m).Distinct().OrderByDescending(f => f)];
        Dictionary<long, Employee> employees = await EmployeesAsync(companyId, ct).ConfigureAwait(false);
        return [.. (await PayslipsAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false)).GroupBy(p => p.EmployeeId).OrderBy(g => Name(employees, g.Key), StringComparer.Ordinal).Select(g =>
        {
            decimal net = g.Sum(p => p.NetPay), left = net;
            List<string> parts = [];
            foreach (decimal face in faces)
            {
                int count = (int)Math.Floor(left / face);
                if (count > 0)
                {
                    parts.Add($"{count} × {face:0.##}");
                    left -= count * face;
                }
            }

            if (left > 0m)
            {
                parts.Add($"remainder {left:0.00}");
            }

            return Row(("Employee", Name(employees, g.Key)), ("Breakdown", faces.Count == 0 ? "no denominations set up" : string.Join(", ", parts)), ("Net", net));
        })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> NetControlAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        List<Payslip> slips = await PayslipsAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false);
        HashSet<string> elementAccounts = [.. await _db.PayElements.AsNoTracking().IgnoreQueryFilters().Where(e => e.CompanyId == companyId).Select(e => e.AccountCode).Distinct().ToListAsync(ct).ConfigureAwait(false)];
        List<JournalEntry> journals = await _db.Journals.AsNoTracking().Include(j => j.Lines)
            .Where(j => j.CompanyId == companyId && j.DocumentType == "PAYROLL" && j.Status == JournalStatus.Posted && j.PostingDate >= fromDate && j.PostingDate <= toDate).ToListAsync(ct).ConfigureAwait(false);
        return [.. slips.GroupBy(p => p.PayDate.ToString("yyyy-MM", CultureInfo.InvariantCulture)).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g =>
        {
            decimal net = g.Sum(p => p.NetPay);
            decimal journal = journals.Where(j => j.PostingDate.ToString("yyyy-MM", CultureInfo.InvariantCulture) == g.Key).SelectMany(j => j.Lines)
                .Where(l => !elementAccounts.Contains(l.AccountCode)).Sum(l => l.BaseCredit - l.BaseDebit);
            return Row(("Month", g.Key), ("Payslips", g.Count()), ("Gross", Money(g.Sum(p => p.GrossEarnings))), ("Deductions", Money(g.Sum(p => p.TotalDeductions))), ("Net", Money(net)),
                ("Journal", Money(journal)), ("Difference", net - journal));
        })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> EarningsAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        Dictionary<long, Employee> employees = await EmployeesAsync(companyId, ct).ConfigureAwait(false);
        return [.. (await PayslipsAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false)).SelectMany(p => p.Lines.Where(l => l.Type == PayElementType.Earning).Select(l => (p.EmployeeId, Line: l)))
            .GroupBy(x => (Department: employees.GetValueOrDefault(x.EmployeeId)?.Department ?? "(none)", x.Line.ElementCode))
            .OrderBy(g => g.Key.Department, StringComparer.Ordinal).ThenBy(g => g.Key.ElementCode, StringComparer.Ordinal)
            .Select(g => Row(("Department", g.Key.Department), ("Element", g.Key.ElementCode), ("Description", g.First().Line.Description), ("Employees", g.Select(x => x.EmployeeId).Distinct().Count()),
                ("Amount", g.Sum(x => x.Line.Amount))))];
    }

    // PAYE is any deduction whose code or description names tax.
    private static bool IsTax(PayslipLine line) => line.Type == PayElementType.Deduction
        && (line.ElementCode.Contains("PAYE", StringComparison.OrdinalIgnoreCase) || line.ElementCode.Contains("TAX", StringComparison.OrdinalIgnoreCase) || line.Description.Contains("tax", StringComparison.OrdinalIgnoreCase));

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> TaxAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        Dictionary<long, Employee> employees = await EmployeesAsync(companyId, ct).ConfigureAwait(false);
        return [.. (await PayslipsAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false)).GroupBy(p => p.EmployeeId).OrderBy(g => Name(employees, g.Key), StringComparer.Ordinal).Select(g =>
        {
            decimal taxable = g.SelectMany(p => p.Lines).Where(l => l.Type == PayElementType.Earning && l.Taxable).Sum(l => l.Amount);
            decimal tax = g.SelectMany(p => p.Lines).Where(IsTax).Sum(l => l.Amount);
            return Row(("Employee", Name(employees, g.Key)), ("Taxable", Money(taxable)), ("Rate", taxable == 0m ? "—" : (tax * 100m / taxable).ToString("0.0", CultureInfo.InvariantCulture) + "%"), ("Tax", tax));
        })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> LeaveAsync(long companyId, CancellationToken ct)
    {
        Dictionary<long, Employee> employees = await EmployeesAsync(companyId, ct).ConfigureAwait(false);
        return [.. (await _db.EmployeeLeaveBalances.AsNoTracking().Where(b => b.CompanyId == companyId).ToListAsync(ct).ConfigureAwait(false))
            .Where(b => employees.TryGetValue(b.EmployeeId, out Employee? e) && e.Status != EmployeeStatus.Terminated)
            .OrderBy(b => Name(employees, b.EmployeeId), StringComparer.Ordinal).ThenBy(b => b.LeaveType, StringComparer.Ordinal).Select(b =>
            {
                decimal daily = Math.Round(employees[b.EmployeeId].BasicSalary * 12m / 260m, 2, MidpointRounding.AwayFromZero);
                decimal balance = b.AccruedDays - b.TakenDays;
                return Row(("Employee", Name(employees, b.EmployeeId)), ("LeaveType", b.LeaveType), ("Accrued", Qty(b.AccruedDays)), ("Taken", Qty(b.TakenDays)), ("Balance", Qty(balance)),
                    ("DailyRate", Money(daily)), ("Liability", Math.Round(Math.Max(0m, balance) * daily, 2, MidpointRounding.AwayFromZero)));
            })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> SalaryChangesAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        Dictionary<long, Employee> employees = await EmployeesAsync(companyId, ct).ConfigureAwait(false);
        DateTimeOffset start = Start(fromDate), end = End(toDate);
        var changes = await _db.ChangeLog.AsNoTracking().Where(c => c.CompanyId == companyId && c.EntityType == "Employee" && c.Action == Core.Audit.AuditAction.Update
            && c.OccurredOnUtc >= start && c.OccurredOnUtc <= end).OrderBy(c => c.Id).ToListAsync(ct).ConfigureAwait(false);
        static string Value(string? json)
        {
            if (json is null)
            {
                return "—";
            }

            using JsonDocument document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("BasicSalary", out JsonElement salary) ? salary.ToString() : "—";
        }

        return [.. changes.Where(c => c.ChangedFields.Contains("BasicSalary")).Select(c => Row(("When", c.OccurredOnUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)),
            ("Employee", c.EntityId is string id && long.TryParse(id, out long value) ? Name(employees, value) : "?"), ("User", c.UserName), ("Old", Value(c.OldValues)), ("New", Value(c.NewValues))))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> LabourCostAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        Dictionary<long, Employee> employees = await EmployeesAsync(companyId, ct).ConfigureAwait(false);
        return [.. (await PayslipsAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false))
            .GroupBy(p => (Department: employees.GetValueOrDefault(p.EmployeeId)?.Department ?? "(none)", CostCentre: employees.GetValueOrDefault(p.EmployeeId)?.CostCentre ?? "(none)"))
            .OrderBy(g => g.Key.Department, StringComparer.Ordinal).ThenBy(g => g.Key.CostCentre, StringComparer.Ordinal).Select(g =>
            {
                decimal gross = g.Sum(p => p.GrossEarnings), employer = g.SelectMany(p => p.Lines).Where(l => l.Type == PayElementType.EmployerContribution).Sum(l => l.Amount);
                return Row(("Department", g.Key.Department), ("CostCentre", g.Key.CostCentre), ("Employees", g.Select(p => p.EmployeeId).Distinct().Count()), ("Gross", Money(gross)),
                    ("Employer", Money(employer)), ("Cost", gross + employer));
            })];
    }

    // Payroll expense accounts are the earning and employer-contribution element accounts.
    private async Task<HashSet<string>> CostAccountsAsync(long companyId, CancellationToken ct)
        => [.. await _db.PayElements.AsNoTracking().IgnoreQueryFilters().Where(e => e.CompanyId == companyId && (e.Type == PayElementType.Earning || e.Type == PayElementType.EmployerContribution))
            .Select(e => e.AccountCode).Distinct().ToListAsync(ct).ConfigureAwait(false)];

    private async Task<Dictionary<string, decimal>> LedgerAsync(long companyId, IReadOnlyCollection<string> accounts, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
        => await (from l in _db.Set<JournalLine>().AsNoTracking() join j in _db.Journals.AsNoTracking() on l.JournalEntryId equals j.Id
                  where j.CompanyId == companyId && (j.Status == JournalStatus.Posted || j.Status == JournalStatus.Reversed) && j.PostingDate >= fromDate && j.PostingDate <= toDate && accounts.Contains(l.AccountCode)
                  group l by l.AccountCode into g select new { g.Key, Net = g.Sum(l => l.BaseDebit - l.BaseCredit) }).ToDictionaryAsync(x => x.Key, x => x.Net, ct).ConfigureAwait(false);

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> BudgetAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        FiscalYear? year = await _db.FiscalYears.AsNoTracking().FirstOrDefaultAsync(y => y.CompanyId == companyId && y.StartDate <= asAt && y.EndDate >= asAt, ct).ConfigureAwait(false);
        if (year is null)
        {
            return [];
        }

        int period = ((asAt.Year - year.StartDate.Year) * 12) + asAt.Month - year.StartDate.Month + 1;
        HashSet<string> accounts = await CostAccountsAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<string, decimal> budget = (await _db.Budgets.AsNoTracking().Include(b => b.Lines)
                .Where(b => b.CompanyId == companyId && b.FiscalYearId == year.Id && b.IsActive && (b.Status == BudgetStatus.Approved || b.Status == BudgetStatus.Active)).ToListAsync(ct).ConfigureAwait(false))
            .SelectMany(b => b.Lines).Where(l => accounts.Contains(l.AccountCode) && l.Period <= period).GroupBy(l => l.AccountCode).ToDictionary(g => g.Key, g => g.Sum(l => l.Amount));
        Dictionary<string, decimal> actual = await LedgerAsync(companyId, accounts, year.StartDate, asAt, ct).ConfigureAwait(false);
        Dictionary<string, string> names = await AccountNamesAsync(companyId, ct).ConfigureAwait(false);
        return [.. accounts.Where(a => budget.ContainsKey(a) || actual.ContainsKey(a)).Order(StringComparer.Ordinal).Select(a => Row(("Account", a), ("Name", names.GetValueOrDefault(a, "?")),
            ("Budget", Money(budget.GetValueOrDefault(a))), ("Actual", Money(actual.GetValueOrDefault(a))), ("Variance", budget.GetValueOrDefault(a) - actual.GetValueOrDefault(a))))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> RevenueShareAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        List<Payslip> slips = await PayslipsAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false);
        var revenue = (await (from l in _db.Set<JournalLine>().AsNoTracking()
                              join j in _db.Journals.AsNoTracking() on l.JournalEntryId equals j.Id
                              join a in _db.Accounts.AsNoTracking() on new { j.CompanyId, l.AccountCode } equals new { a.CompanyId, a.AccountCode }
                              where j.CompanyId == companyId && (j.Status == JournalStatus.Posted || j.Status == JournalStatus.Reversed) && j.PostingDate >= fromDate && j.PostingDate <= toDate && a.Type == AccountType.Income
                              select new { j.PostingDate, Net = l.BaseCredit - l.BaseDebit }).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(x => x.PostingDate.ToString("yyyy-MM", CultureInfo.InvariantCulture)).ToDictionary(g => g.Key, g => g.Sum(x => x.Net));
        Dictionary<string, decimal> payroll = slips.GroupBy(p => p.PayDate.ToString("yyyy-MM", CultureInfo.InvariantCulture))
            .ToDictionary(g => g.Key, g => g.Sum(p => p.GrossEarnings) + g.SelectMany(p => p.Lines).Where(l => l.Type == PayElementType.EmployerContribution).Sum(l => l.Amount));
        return [.. revenue.Keys.Union(payroll.Keys).Order(StringComparer.Ordinal).Select(m => Row(("Month", m), ("Revenue", Money(revenue.GetValueOrDefault(m))),
            ("Share", revenue.GetValueOrDefault(m) == 0m ? "—" : (payroll.GetValueOrDefault(m) * 100m / revenue[m]).ToString("0.0", CultureInfo.InvariantCulture) + "%"), ("Payroll", payroll.GetValueOrDefault(m))))];
    }

    // Earnings and employer contributions debit their accounts; deductions and contributions' liabilities credit theirs.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GlReconAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        Dictionary<string, PayElement> elements = await _db.PayElements.AsNoTracking().IgnoreQueryFilters().Where(e => e.CompanyId == companyId).ToDictionaryAsync(e => e.Code, ct).ConfigureAwait(false);
        Dictionary<string, decimal> perSlips = (await PayslipsAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false)).SelectMany(p => p.Lines).Where(l => elements.ContainsKey(l.ElementCode))
            .GroupBy(l => elements[l.ElementCode].AccountCode).ToDictionary(g => g.Key, g => g.Sum(l => l.Type == PayElementType.Deduction ? -l.Amount : l.Amount));
        List<string> accounts = [.. perSlips.Keys];
        Dictionary<string, decimal> ledger = await (from l in _db.Set<JournalLine>().AsNoTracking() join j in _db.Journals.AsNoTracking() on l.JournalEntryId equals j.Id
                                                   where j.CompanyId == companyId && j.DocumentType == "PAYROLL" && j.Status == JournalStatus.Posted && j.PostingDate >= fromDate && j.PostingDate <= toDate && accounts.Contains(l.AccountCode)
                                                   group l by l.AccountCode into g select new { g.Key, Net = g.Sum(l => l.BaseDebit - l.BaseCredit) }).ToDictionaryAsync(x => x.Key, x => x.Net, ct).ConfigureAwait(false);
        Dictionary<string, string> names = await AccountNamesAsync(companyId, ct).ConfigureAwait(false);
        return [.. accounts.Order(StringComparer.Ordinal).Select(a => Row(("Account", a), ("Name", names.GetValueOrDefault(a, "?")), ("Payslips", Money(perSlips[a])), ("Ledger", Money(ledger.GetValueOrDefault(a))),
            ("Difference", perSlips[a] - ledger.GetValueOrDefault(a))))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> AuditAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        DateTimeOffset start = Start(fromDate), end = End(toDate);
        return [.. (await _db.ChangeLog.AsNoTracking().Where(c => (c.CompanyId == companyId || c.CompanyId == null) && PayrollEntities.Contains(c.EntityType) && c.OccurredOnUtc >= start && c.OccurredOnUtc <= end)
                .OrderBy(c => c.Id).Take(10000).ToListAsync(ct).ConfigureAwait(false))
            .Select(c => Row(("When", c.OccurredOnUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)), ("User", c.UserName), ("Entity", c.EntityType), ("Id", c.EntityId ?? "—"),
                ("Action", c.Action.ToString()), ("Changed", c.ChangedFields.Count == 0 ? "—" : string.Join(", ", c.ChangedFields))))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> AccessLogAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        DateTimeOffset start = Start(fromDate), end = End(toDate);
        Dictionary<long, string> users = await _db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Username, ct).ConfigureAwait(false);
        return [.. (await _db.Set<ReportRun>().AsNoTracking().Where(r => r.CompanyId == companyId && r.RunAt >= start && r.RunAt <= end
                && (r.ReportCode.StartsWith("PR") || r.ReportCode.StartsWith("PAY-") || r.ReportCode.StartsWith("HR-"))).OrderBy(r => r.RunAt).ToListAsync(ct).ConfigureAwait(false))
            .Select(r => Row(("When", r.RunAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)), ("Report", r.ReportCode), ("User", r.UserId is long u ? users.GetValueOrDefault(u, $"user {u}") : "system"),
                ("Purpose", r.Purpose.ToString()), ("Format", r.Format), ("Copy", r.CopyNumber?.ToString(CultureInfo.InvariantCulture) ?? "—")))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> YtdAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        Dictionary<long, Employee> employees = await EmployeesAsync(companyId, ct).ConfigureAwait(false);
        return [.. (await PayslipsAsync(companyId, new DateOnly(toDate.Year, 1, 1), toDate, ct).ConfigureAwait(false)).GroupBy(p => p.EmployeeId).OrderBy(g => Name(employees, g.Key), StringComparer.Ordinal)
            .Select(g => Row(("Employee", Name(employees, g.Key)), ("Department", employees.GetValueOrDefault(g.Key)?.Department ?? "—"), ("Gross", Money(g.Sum(p => p.GrossEarnings))),
                ("Taxable", Money(g.SelectMany(p => p.Lines).Where(l => l.Type == PayElementType.Earning && l.Taxable).Sum(l => l.Amount))), ("Deductions", Money(g.Sum(p => p.TotalDeductions))),
                ("Employer", Money(g.SelectMany(p => p.Lines).Where(l => l.Type == PayElementType.EmployerContribution).Sum(l => l.Amount))), ("Net", g.Sum(p => p.NetPay))))];
    }

    private Task<Dictionary<string, string>> AccountNamesAsync(long companyId, CancellationToken ct)
        => _db.Accounts.AsNoTracking().IgnoreQueryFilters().Where(a => a.CompanyId == companyId).ToDictionaryAsync(a => a.AccountCode, a => a.Name, ct);

    private static DateTimeOffset Start(DateOnly date) => new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    private static DateTimeOffset End(DateOnly date) => new(date.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

    private static DateOnly? DateParameter(IReadOnlyDictionary<string, object?> parameters, string name) => parameters.TryGetValue(name, out object? value) && value is DateOnly date ? date : null;

    private static string Qty(decimal quantity) => quantity.ToString("0.##", CultureInfo.InvariantCulture);

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
