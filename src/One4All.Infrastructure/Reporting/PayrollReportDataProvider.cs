using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Payroll;
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
            "PYRLALLOWANCES" => await AllowancesAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "PYRLCOSTTOCOMPANY" => await CostToCompanyAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
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
            "PYRLVALIDATION" => await ValidationAsync(companyId, asAt, ct).ConfigureAwait(false),
            "PYRLEFT" => await PaymentScheduleAsync(companyId, fromDate, toDate, PayMethod.BankTransfer, ct).ConfigureAwait(false),
            "PYRLMOBILE" => await PaymentScheduleAsync(companyId, fromDate, toDate, PayMethod.MobileMoney, ct).ConfigureAwait(false),
            "PYRLHELD" => await HeldAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "PYRLHEADCOUNT" => await HeadcountAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "PYRLSTARTERSLEAVERS" => await StartersLeaversAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "PYRLGRADES" => await GradesAsync(companyId, ct).ConfigureAwait(false),
            "PYRLBANKCHANGES" => await BankChangesAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "PYRLEXPIRIES" => await ExpiriesAsync(companyId, asAt, ct).ConfigureAwait(false),
            "PYRLGHOSTS" => await GhostsAsync(companyId, asAt, ct).ConfigureAwait(false),
            "PYRLRUNS" => await RunsAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "PYRLPARALLEL" => await ParallelAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "PYRLADJUSTMENTS" => await AdjustmentsAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "PYRLBACKPAY" => await BackpayAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "PYRLSTAFFPURCHASES" => await StaffPurchasesAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "PYRLHOUSINGRENT" => await HousingRentAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "PYRLCURRENCYSPLIT" => await CurrencySplitAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "PYRLARREARS" => await ArrearsAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "PYRLVARIABLES" => await VariablesAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "PYRLREMITTANCEINVOICES" => await RemittanceInvoicesAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "PYRLSCHEMES" => await SchemeScheduleAsync(companyId, fromDate, toDate, _ => true, Text(parameters, "SchemeCode"), ct).ConfigureAwait(false),
            "PYRLSCHEMESOCIAL" => await SchemeScheduleAsync(companyId, fromDate, toDate, k => k == SchemeKind.SocialSecurity, null, ct).ConfigureAwait(false),
            "PYRLSCHEMEPENSION" => await SchemeScheduleAsync(companyId, fromDate, toDate, k => k == SchemeKind.Pension, null, ct).ConfigureAwait(false),
            "PYRLSCHEMEMEDICAL" => await SchemeScheduleAsync(companyId, fromDate, toDate, k => k == SchemeKind.MedicalAid, null, ct).ConfigureAwait(false),
            "PYRLSCHEMELEVY" => await SchemeScheduleAsync(companyId, fromDate, toDate, k => k is SchemeKind.WorkersCompensation or SchemeKind.SkillsLevy, null, ct).ConfigureAwait(false),
            "PYRLREMITTANCES" => await RemittancesAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "PYRLPAYERETURN" => await PayeReturnAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "PYRLCERTIFICATES" => await CertificatesAsync(companyId, toDate, ct).ConfigureAwait(false),
            "PYRLGARNISHEES" => await GarnisheesAsync(companyId, asAt, ct).ConfigureAwait(false),
            "PYRLESTABLISHMENT" => await EstablishmentAsync(companyId, asAt, ct).ConfigureAwait(false),
            "PYRLDOCUMENTS" => await DocumentsAsync(companyId, asAt, Days(parameters), Text(parameters, "RequiredTypes"), ct).ConfigureAwait(false),
            "PYRLDISCIPLINARY" => await DisciplinaryAsync(companyId, asAt, ct).ConfigureAwait(false),
            "PYRLTRAINING" => await TrainingAsync(companyId, asAt, Days(parameters), ct).ConfigureAwait(false),
            "PYRLOVERTIME" => await OvertimeAsync(companyId, fromDate, toDate, Number(parameters, "HoursPerMonth", 173.33m), ct).ConfigureAwait(false),
            "PYRLLEAVETAKEN" => await LeaveTakenAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "PYRLABSENCE" => await AbsenceAsync(companyId, fromDate, toDate, Number(parameters, "FullPayDays", 90m), Number(parameters, "HalfPayDays", 90m), ct).ConfigureAwait(false),
            "PYRLENCASHMENT" => await EncashmentAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "PYRLATTENDANCEEXCEPTIONS" => await AttendanceExceptionsAsync(companyId, fromDate, toDate, Number(parameters, "MaxHours", 12m), ct).ConfigureAwait(false),
            "PYRLSPLITPAY" => await SplitPayAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "PYRLBENEFITS" => await BenefitsAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "PYRLFINALSETTLEMENT" => await FinalSettlementAsync(companyId, fromDate, toDate, new TerminalPolicy(Number(parameters, "NoticeMonths", 1m), 21.67m,
                Number(parameters, "GratuityWeeksPerYear", 2m), Number(parameters, "SeveranceWeeksPerYear", 4m), Number(parameters, "TerminalTaxRate", 0m)), ct).ConfigureAwait(false),
            "PYRLGRATUITY" => await GratuityAsync(companyId, asAt, Number(parameters, "WeeksPerYear", 2m), (int)Number(parameters, "RetirementAge", 65m), Number(parameters, "DiscountRate", 10m), ct)
                .ConfigureAwait(false),
            "PYRLLABOURMIX" => await LabourMixAsync(companyId, fromDate, toDate, Number(parameters, "HoursPerMonth", 173.33m), ct).ConfigureAwait(false),
            "PYRLREPRINTS" => await ReprintsAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "PYRLLOANS" => await LoansAsync(companyId, ct).ConfigureAwait(false),
            "PYRLJOBALLOCATION" => await JobAllocationAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "PYRLCLEARANCE" => await ClearanceAsync(companyId, asAt, ct).ConfigureAwait(false),
            "PYRLACCRUAL" => await AccrualAsync(companyId, asAt, ct).ConfigureAwait(false),
            "PYRLSOD" => await SodAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "PYRLTAXTABLE" => await TaxTableAsync(companyId, asAt, ct).ConfigureAwait(false),
            _ => [],
        };
    }

    private Task<List<Payslip>> PayslipsAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
        => _db.Payslips.AsNoTracking().Include(p => p.Lines).Where(p => p.CompanyId == companyId && p.PayDate >= fromDate && p.PayDate <= toDate).OrderBy(p => p.PayDate).ToListAsync(ct);

    private Task<Dictionary<long, Employee>> EmployeesAsync(long companyId, CancellationToken ct)
        => _db.Employees.AsNoTracking().IgnoreQueryFilters([Persistence.One4AllDbContext.SoftDeleteFilter]).Where(e => e.CompanyId == companyId).ToDictionaryAsync(e => e.Id, ct);

    private static string Name(Dictionary<long, Employee> employees, long id) => employees.TryGetValue(id, out Employee? e) ? $"{e.EmployeeNumber} {e.FullName}" : "?";

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> SummaryAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
        => [.. (await PayslipsAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false)).SelectMany(p => p.Lines.Select(l => (p.EmployeeId, p.PayGroup, Line: l)))
            .GroupBy(x => (x.PayGroup, x.Line.Type, x.Line.ElementCode)).OrderBy(g => g.Key.PayGroup, StringComparer.Ordinal).ThenBy(g => g.Key.Type).ThenBy(g => g.Key.ElementCode, StringComparer.Ordinal)
            .Select(g => Row(("PayGroup", g.Key.PayGroup.Length == 0 ? "Default" : g.Key.PayGroup), ("Type", g.Key.Type.ToString()), ("Element", g.Key.ElementCode), ("Description", g.First().Line.Description), ("Employees", g.Select(x => x.EmployeeId).Distinct().Count()),
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
        Dictionary<string, PayElement> elements = await _db.PayElements.AsNoTracking().IgnoreQueryFilters([Persistence.One4AllDbContext.SoftDeleteFilter]).Where(e => e.CompanyId == companyId).ToDictionaryAsync(e => e.Code, ct).ConfigureAwait(false);
        string Rule(PayElement? e) => e is null ? "—" : e.Basis switch
        {
            CalculationBasis.FixedAmount => Money(e.FixedAmount),
            CalculationBasis.Formula => e.Formula ?? "—",
            CalculationBasis.RatePerUnit => $"{e.Rate:0.####} per unit",
            _ => $"{e.Rate:0.####}%",
        };
        return [.. (await PayslipsAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false)).SelectMany(p => p.Lines.Select(l => Row(("Employee", Name(employees, p.EmployeeId)), ("Period", p.PeriodLabel),
            ("Element", l.ElementCode), ("Basis", elements.GetValueOrDefault(l.ElementCode)?.Basis.ToString() ?? l.Type.ToString()), ("Rule", l.Workings ?? Rule(elements.GetValueOrDefault(l.ElementCode))),
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
        HashSet<string> elementAccounts = [.. await _db.PayElements.AsNoTracking().IgnoreQueryFilters([Persistence.One4AllDbContext.SoftDeleteFilter]).Where(e => e.CompanyId == companyId).Select(e => e.AccountCode).Distinct().ToListAsync(ct).ConfigureAwait(false)];
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
    private static bool IsTax(PayslipLine line) => TaxLines.IsTax(line.Type, line.ElementCode, line.Description);

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
        => [.. await _db.PayElements.AsNoTracking().IgnoreQueryFilters([Persistence.One4AllDbContext.SoftDeleteFilter]).Where(e => e.CompanyId == companyId && (e.Type == PayElementType.Earning || e.Type == PayElementType.EmployerContribution))
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
        Dictionary<string, PayElement> elements = await _db.PayElements.AsNoTracking().IgnoreQueryFilters([Persistence.One4AllDbContext.SoftDeleteFilter]).Where(e => e.CompanyId == companyId).ToDictionaryAsync(e => e.Code, ct).ConfigureAwait(false);
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

    private async Task<List<EmployeeFacts>> FactsAsync(long companyId, CancellationToken ct)
    {
        Dictionary<long, decimal> taken = await _db.EmployeeLeaveBalances.AsNoTracking().Where(b => b.CompanyId == companyId).GroupBy(b => b.EmployeeId)
            .Select(g => new { g.Key, Days = g.Sum(b => b.TakenDays) }).ToDictionaryAsync(x => x.Key, x => x.Days, ct).ConfigureAwait(false);
        return [.. (await _db.Employees.AsNoTracking().Where(e => e.CompanyId == companyId && e.Status != EmployeeStatus.Terminated).OrderBy(e => e.EmployeeNumber).ToListAsync(ct).ConfigureAwait(false))
            .Select(e => new EmployeeFacts(e.Id, e.EmployeeNumber, e.FullName, e.NationalIdNumber, e.TaxNumber, e.DateOfBirth, e.BasicSalary, e.PaymentMethod, e.BankAccountNumber, e.MobileMoneyNumber,
                e.EngagementDate, taken.GetValueOrDefault(e.Id)))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ValidationAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        Dictionary<long, Employee> employees = await EmployeesAsync(companyId, ct).ConfigureAwait(false);
        return [.. (await FactsAsync(companyId, ct).ConfigureAwait(false)).Select(f => (Facts: f, Issues: EmployeeChecks.Issues(f, asAt))).Where(x => x.Issues.Count > 0)
            .Select(x => Row(("Employee", Name(employees, x.Facts.Id)), ("Department", employees[x.Facts.Id].Department ?? "—"), ("Method", x.Facts.PaymentMethod.ToString()), ("Issues", string.Join("; ", x.Issues))))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> PaymentScheduleAsync(long companyId, DateOnly fromDate, DateOnly toDate, PayMethod method, CancellationToken ct)
    {
        Dictionary<long, Employee> employees = await EmployeesAsync(companyId, ct).ConfigureAwait(false);
        return [.. (await PayslipsAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false))
            .Where(p => employees.TryGetValue(p.EmployeeId, out Employee? e) && e.PaymentMethod == method && !e.IsSalaryHeld && p.NetPay > 0m)
            .OrderBy(p => Name(employees, p.EmployeeId), StringComparer.Ordinal).ThenBy(p => p.PayDate)
            .Select(p =>
            {
                Employee e = employees[p.EmployeeId];
                return method == PayMethod.BankTransfer
                    ? Row(("Employee", Name(employees, p.EmployeeId)), ("Bank", e.BankName ?? "MISSING"), ("Branch", e.BankBranch ?? "—"), ("Account", e.BankAccountNumber ?? "MISSING"), ("Reference", p.PeriodLabel), ("Net", p.NetPay))
                    : Row(("Employee", Name(employees, p.EmployeeId)), ("Wallet", e.MobileMoneyNumber ?? "MISSING"), ("Reference", p.PeriodLabel), ("Net", p.NetPay));
            })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> HeldAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        Dictionary<long, Employee> employees = await EmployeesAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<long, decimal> net = (await PayslipsAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false)).GroupBy(p => p.EmployeeId).ToDictionary(g => g.Key, g => g.Sum(p => p.NetPay));
        return [.. employees.Values.Where(e => e.IsSalaryHeld).OrderBy(e => e.EmployeeNumber, StringComparer.Ordinal)
            .Select(e => Row(("Employee", Name(employees, e.Id)), ("Department", e.Department ?? "—"), ("Method", e.PaymentMethod.ToString()), ("Reason", e.HoldReason ?? "—"), ("Net", net.GetValueOrDefault(e.Id))))];
    }

    private static bool EmployedOn(Employee e, DateOnly date) => e.EngagementDate <= date && !(e.TerminationDate is DateOnly left && left < date);

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> HeadcountAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        List<Employee> employees = [.. (await EmployeesAsync(companyId, ct).ConfigureAwait(false)).Values];
        List<IReadOnlyDictionary<string, object?>> rows = [.. employees.GroupBy(e => e.Department ?? "(none)").OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => Row(("Department", g.Key),
            ("Opening", g.Count(e => EmployedOn(e, fromDate.AddDays(-1)))), ("Starters", g.Count(e => e.EngagementDate >= fromDate && e.EngagementDate <= toDate)),
            ("Leavers", g.Count(e => e.TerminationDate is DateOnly d && d >= fromDate && d <= toDate)), ("Closing", g.Count(e => EmployedOn(e, toDate)))))];
        rows.Add(Row(("Department", "Total"), ("Opening", employees.Count(e => EmployedOn(e, fromDate.AddDays(-1)))), ("Starters", employees.Count(e => e.EngagementDate >= fromDate && e.EngagementDate <= toDate)),
            ("Leavers", employees.Count(e => e.TerminationDate is DateOnly d && d >= fromDate && d <= toDate)), ("Closing", employees.Count(e => EmployedOn(e, toDate)))));
        return rows;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> StartersLeaversAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        Dictionary<long, Employee> employees = await EmployeesAsync(companyId, ct).ConfigureAwait(false);
        return [.. employees.Values.Where(e => e.EngagementDate >= fromDate && e.EngagementDate <= toDate).Select(e => (Kind: "Starter", Employee: e, Date: e.EngagementDate, Reason: string.Empty))
            .Concat(employees.Values.Where(e => e.TerminationDate is DateOnly d && d >= fromDate && d <= toDate).Select(e => (Kind: "Leaver", Employee: e, Date: e.TerminationDate!.Value, Reason: e.TerminationReason ?? "—")))
            .OrderBy(x => x.Date).ThenBy(x => x.Kind, StringComparer.Ordinal)
            .Select(x => Row(("Kind", x.Kind), ("Employee", Name(employees, x.Employee.Id)), ("Department", x.Employee.Department ?? "—"), ("Date", Date(x.Date)), ("Reason", x.Reason), ("Basic", x.Employee.BasicSalary)))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GradesAsync(long companyId, CancellationToken ct)
        => [.. (await EmployeesAsync(companyId, ct).ConfigureAwait(false)).Values.Where(e => e.Status != EmployeeStatus.Terminated).GroupBy(e => e.Grade ?? "(none)").OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => Row(("Grade", g.Key), ("Headcount", g.Count()), ("Female", g.Count(e => e.Gender == Gender.Female)), ("Male", g.Count(e => e.Gender == Gender.Male)),
                ("Unspecified", g.Count(e => e.Gender == Gender.Unspecified)), ("Range", $"{Money(g.Min(e => e.BasicSalary))} – {Money(g.Max(e => e.BasicSalary))}"),
                ("Average", Math.Round(g.Average(e => e.BasicSalary), 2, MidpointRounding.AwayFromZero))))];

    private static readonly string[] PaymentFields = ["PaymentMethod", "BankName", "BankBranch", "BankAccountNumber", "MobileMoneyNumber"];

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> BankChangesAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        Dictionary<long, Employee> employees = await EmployeesAsync(companyId, ct).ConfigureAwait(false);
        DateTimeOffset start = Start(fromDate), end = End(toDate);
        var changes = await _db.ChangeLog.AsNoTracking().Where(c => c.CompanyId == companyId && c.EntityType == "Employee" && c.Action == Core.Audit.AuditAction.Update
            && c.OccurredOnUtc >= start && c.OccurredOnUtc <= end).OrderBy(c => c.Id).ToListAsync(ct).ConfigureAwait(false);
        static string Mask(string value) => value.Length <= 4 ? new string('*', value.Length) : new string('*', value.Length - 4) + value[^4..];
        static string Details(string? json)
        {
            if (json is null)
            {
                return "—";
            }

            using JsonDocument document = JsonDocument.Parse(json);
            return string.Join(", ", PaymentFields.Where(f => document.RootElement.TryGetProperty(f, out _)).Select(f =>
            {
                string value = document.RootElement.GetProperty(f).ToString();
                return $"{f}: {(f is "BankAccountNumber" or "MobileMoneyNumber" ? Mask(value) : value)}";
            }));
        }

        return [.. changes.Where(c => c.ChangedFields.Any(PaymentFields.Contains)).Select(c => Row(("When", c.OccurredOnUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)),
            ("Employee", c.EntityId is string id && long.TryParse(id, out long value) ? Name(employees, value) : "?"), ("User", c.UserName),
            ("Changed", string.Join(", ", c.ChangedFields.Where(PaymentFields.Contains))), ("New", Details(c.NewValues))))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ExpiriesAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        Dictionary<long, Employee> employees = await EmployeesAsync(companyId, ct).ConfigureAwait(false);
        DateOnly horizon = asAt.AddDays(90);
        return [.. employees.Values.Where(e => e.Status != EmployeeStatus.Terminated)
            .SelectMany(e => new[] { ("Contract end", e, e.ContractEndDate), ("Probation end", e, e.ProbationEndDate), ("Work permit", e, e.WorkPermitExpiry) })
            .Where(x => x.Item3 is DateOnly d && d <= horizon).OrderBy(x => x.Item3)
            .Select(x => Row(("Kind", x.Item1), ("Employee", Name(employees, x.Item2.Id)), ("Department", x.Item2.Department ?? "—"), ("Date", Date(x.Item3!.Value)),
                ("Due", x.Item3.Value < asAt ? $"{asAt.DayNumber - x.Item3.Value.DayNumber} days overdue" : $"in {x.Item3.Value.DayNumber - asAt.DayNumber} days")))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GhostsAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        Dictionary<long, Employee> employees = await EmployeesAsync(companyId, ct).ConfigureAwait(false);
        return [.. EmployeeChecks.GhostSignals(await FactsAsync(companyId, ct).ConfigureAwait(false), asAt).OrderBy(s => Name(employees, s.Key), StringComparer.Ordinal)
            .Select(s => Row(("Employee", Name(employees, s.Key)), ("Department", employees[s.Key].Department ?? "—"), ("Engaged", Date(employees[s.Key].EngagementDate)), ("Signals", string.Join("; ", s.Value))))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> RunsAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        Dictionary<long, string> users = await _db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Username, ct).ConfigureAwait(false);
        string User(long? id) => id is not long u ? "-" : u == 0 ? "system" : users.GetValueOrDefault(u, $"user {u}");
        Dictionary<string, PayrollDraft> drafts = (await _db.PayrollDrafts.AsNoTracking().Where(d => d.CompanyId == companyId && d.Status == PayrollDraftStatus.Committed).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(d => d.DocumentNumber).ToDictionary(g => g.Key, g => g.Last());
        return [.. (await _db.Set<PayrollRunRecord>().AsNoTracking().Where(r => r.CompanyId == companyId && r.PayDate >= fromDate && r.PayDate <= toDate).OrderBy(r => r.PayDate).ToListAsync(ct).ConfigureAwait(false))
            .Select(r =>
            {
                PayrollDraft? draft = r.Kind == PayRunKind.Regular && r.DocumentNumber.Length > 0 ? drafts.GetValueOrDefault(r.DocumentNumber) : null;
                string approved = draft is null ? "-" : draft.SecondApproverUserId is long second ? $"{User(draft.FirstApproverUserId)} + {User(second)}" : User(draft.FirstApproverUserId);
                return Row(("Period", r.PeriodLabel), ("PayDate", Date(r.PayDate)), ("Employees", r.Employees), ("Gross", Money(r.Gross)), ("Deductions", Money(r.Deductions)), ("Journal", r.JournalNumber),
                    ("RunBy", User(r.CreatedByUserId)), ("CalculatedBy", draft is null ? "-" : User(draft.CalculatedByUserId)), ("ApprovedBy", approved), ("Net", r.Net));
            })];
    }

    // PR8: per period paid in the range, One4All's net amount per employee and element (all runs) against the legacy import.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ParallelAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        List<Payslip> slips = await PayslipsAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false);
        List<string> periods = [.. slips.Select(p => p.PeriodLabel).Distinct()];
        List<LegacyPayslipLine> legacy = await _db.LegacyPayslipLines.AsNoTracking().Where(l => l.CompanyId == companyId && periods.Contains(l.PeriodLabel)).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, Employee> employees = await EmployeesAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<string, Employee> byNumber = employees.Values.GroupBy(e => e.EmployeeNumber).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        Dictionary<(string Period, string Employee, string Element), decimal> ours = [];
        static void Add(Dictionary<(string Period, string Employee, string Element), decimal> map, (string, string, string) key, decimal amount) => map[key] = map.GetValueOrDefault(key) + amount;
        foreach (Payslip slip in slips)
        {
            string number = employees.TryGetValue(slip.EmployeeId, out Employee? e) ? e.EmployeeNumber : $"#{slip.EmployeeId}";
            foreach (PayslipLine line in slip.Lines)
            {
                Add(ours, (slip.PeriodLabel, number, line.ElementCode.ToUpperInvariant()), line.Amount);
            }

            Add(ours, (slip.PeriodLabel, number, "NET"), slip.NetPay);
        }

        Dictionary<(string Period, string Employee, string Element), decimal> theirs = [];
        foreach (LegacyPayslipLine line in legacy)
        {
            Add(theirs, (line.PeriodLabel, line.EmployeeNumber, line.ElementCode), line.Amount);
        }

        HashSet<string> imported = [.. legacy.Select(l => l.PeriodLabel)];
        string Who(string number) => byNumber.TryGetValue(number, out Employee? e) ? $"{e.EmployeeNumber} {e.FullName}" : number;
        return [.. ours.Keys.Union(theirs.Keys).Where(k => imported.Contains(k.Period)).OrderBy(k => k.Period, StringComparer.Ordinal).ThenBy(k => k.Employee, StringComparer.Ordinal)
            .ThenBy(k => k.Element == "NET" ? 1 : 0).ThenBy(k => k.Element, StringComparer.Ordinal)
            .Select(k =>
            {
                bool inOurs = ours.TryGetValue(k, out decimal a);
                bool inTheirs = theirs.TryGetValue(k, out decimal b);
                string status = !inTheirs ? "Not in legacy" : !inOurs ? "Not in One4All" : a == b ? "Match" : "Differs";
                return Row(("Period", k.Period), ("Employee", Who(k.Employee)), ("Element", k.Element), ("One4All", inOurs ? Money(a) : "-"), ("Legacy", inTheirs ? Money(b) : "-"), ("Status", status),
                    ("Difference", a - b));
            })];
    }

    // PR9: correction runs (supplementary, backpay, reversal) and runs that were reversed.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> AdjustmentsAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        Dictionary<long, string> users = await _db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Username, ct).ConfigureAwait(false);
        return [.. (await _db.Set<PayrollRunRecord>().AsNoTracking().Where(r => r.CompanyId == companyId && r.PayDate >= fromDate && r.PayDate <= toDate && (r.Kind != PayRunKind.Regular || r.ReversedOn != null))
                .OrderBy(r => r.PayDate).ThenBy(r => r.Id).ToListAsync(ct).ConfigureAwait(false))
            .Select(r => Row(("Run", r.DocumentNumber.Length > 0 ? r.DocumentNumber : r.JournalNumber), ("Period", r.PeriodLabel), ("Kind", r.Kind.ToString()), ("PayDate", Date(r.PayDate)), ("Reason", r.Reason ?? "-"),
                ("Employees", r.Employees), ("Journal", r.JournalNumber),
                ("Reversal", r.ReversedOn is DateOnly on ? $"{r.ReversedBy} on {Date(on)}, approved by {users.GetValueOrDefault(r.ReversalApprovedByUserId ?? 0, "?")}" : "-"), ("Net", r.Net)))];
    }

    // PR10: the backpay calculations settled in the period.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> BackpayAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        Dictionary<long, Employee> employees = await EmployeesAsync(companyId, ct).ConfigureAwait(false);
        return [.. (await _db.BackpayLines.AsNoTracking().Where(b => b.CompanyId == companyId && b.PaidOn >= fromDate && b.PaidOn <= toDate).ToListAsync(ct).ConfigureAwait(false))
            .OrderBy(b => Name(employees, b.EmployeeId), StringComparer.Ordinal).ThenBy(b => b.ForPeriod, StringComparer.Ordinal).ThenBy(b => b.ElementCode, StringComparer.Ordinal)
            .Select(b => Row(("Employee", Name(employees, b.EmployeeId)), ("ForPeriod", b.ForPeriod), ("Element", b.ElementCode), ("Paid", Money(b.Paid)), ("Due", Money(b.Due)), ("Run", b.RunReference),
                ("PaidOn", Date(b.PaidOn)), ("Reason", b.Reason), ("Difference", b.Difference)))];
    }

    // Every recovery deduction line (element code, employee, pay date, amount) for the kind.
    private async Task<List<(long EmployeeId, DateOnly PayDate, decimal Amount)>> RecoveryLinesAsync(long companyId, PayRecovery kind, CancellationToken ct)
    {
        List<string> codes = await _db.PayElements.AsNoTracking().Where(e => e.CompanyId == companyId && e.Recovers == kind).Select(e => e.Code).ToListAsync(ct).ConfigureAwait(false);
        var lines = await _db.Payslips.AsNoTracking().Where(p => p.CompanyId == companyId)
            .SelectMany(p => p.Lines.Where(l => codes.Contains(l.ElementCode) && l.Amount != 0m).Select(l => new { p.EmployeeId, p.PayDate, l.Amount })).ToListAsync(ct).ConfigureAwait(false);
        return [.. lines.Select(l => (l.EmployeeId, l.PayDate, l.Amount))];
    }

    // PR23: staff-account tenders against staff-purchase deductions, per employee.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> StaffPurchasesAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        var tenders = await _db.Sales.AsNoTracking().Where(s => s.CompanyId == companyId && s.Status == Core.Pos.SaleStatus.Completed && s.SaleDate <= toDate)
            .SelectMany(s => s.Tenders.Where(t => t.StaffEmployeeId != null).Select(t => new { EmployeeId = t.StaffEmployeeId!.Value, s.SaleDate, t.BaseAmount })).ToListAsync(ct).ConfigureAwait(false);
        List<(long EmployeeId, DateOnly PayDate, decimal Amount)> deducted = [.. (await RecoveryLinesAsync(companyId, PayRecovery.StaffPurchases, ct).ConfigureAwait(false)).Where(l => l.PayDate <= toDate)];
        Dictionary<long, Employee> employees = await EmployeesAsync(companyId, ct).ConfigureAwait(false);
        return [.. tenders.Select(t => t.EmployeeId).Concat(deducted.Select(d => d.EmployeeId)).Distinct().OrderBy(id => Name(employees, id), StringComparer.Ordinal).Select(id =>
        {
            decimal opening = tenders.Where(t => t.EmployeeId == id && t.SaleDate < fromDate).Sum(t => t.BaseAmount) - deducted.Where(d => d.EmployeeId == id && d.PayDate < fromDate).Sum(d => d.Amount);
            decimal purchases = tenders.Where(t => t.EmployeeId == id && t.SaleDate >= fromDate).Sum(t => t.BaseAmount);
            decimal recovered = deducted.Where(d => d.EmployeeId == id && d.PayDate >= fromDate).Sum(d => d.Amount);
            return Row(("Employee", Name(employees, id)), ("Opening", Money(opening)), ("Purchases", Money(purchases)), ("Deducted", Money(recovered)), ("Closing", decimal.Round(opening + purchases - recovered, 2, MidpointRounding.ToEven)));
        })];
    }

    // PR24: per employee tenant, the tenancy account's rent billed and payroll receipts against the rent deducted from pay.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> HousingRentAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        Dictionary<long, Employee> employees = await EmployeesAsync(companyId, ct).ConfigureAwait(false);
        List<(long EmployeeId, DateOnly PayDate, decimal Amount)> deducted = [.. (await RecoveryLinesAsync(companyId, PayRecovery.HousingRent, ct).ConfigureAwait(false))
            .Where(l => l.PayDate >= fromDate && l.PayDate <= toDate)];
        List<long> tenancyIds = [.. employees.Values.Where(e => e.HousingTenancyId != null).Select(e => e.HousingTenancyId!.Value)];
        Dictionary<long, Core.Housing.Tenancy> tenancies = await _db.Set<Core.Housing.Tenancy>().AsNoTracking().Where(t => tenancyIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct).ConfigureAwait(false);
        List<long> unitIds = [.. tenancies.Values.Select(t => t.UnitId)];
        Dictionary<long, string> units = await _db.Set<Core.Housing.RentalUnit>().AsNoTracking().Where(u => unitIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Code, ct).ConfigureAwait(false);
        List<long> customers = [.. tenancies.Values.Select(t => t.TenantCustomerId)];
        List<Core.Trading.OpenItem> items = await _db.OpenItems.AsNoTracking().Where(i => i.CompanyId == companyId && i.Ledger == Core.Trading.TradingLedger.AccountsReceivable && customers.Contains(i.TraderId)
            && i.DocumentDate <= toDate).ToListAsync(ct).ConfigureAwait(false);

        // Payroll receipts are numbered "<run document>-<employee number>".
        List<string> runs = await _db.Set<PayrollRunRecord>().AsNoTracking().Where(r => r.CompanyId == companyId && r.DocumentNumber != string.Empty).Select(r => r.DocumentNumber).ToListAsync(ct).ConfigureAwait(false);
        HashSet<string> ReceiptNumbers(Employee e) => [.. runs.Select(r => $"{r}-{e.EmployeeNumber}").Select(d => d.Length > 40 ? d[..40] : d)];

        return [.. employees.Values.Where(e => (e.HousingTenancyId is long t && tenancies.ContainsKey(t)) || deducted.Any(d => d.EmployeeId == e.Id)).OrderBy(e => e.EmployeeNumber, StringComparer.Ordinal).Select(e =>
        {
            Core.Housing.Tenancy? tenancy = e.HousingTenancyId is long id ? tenancies.GetValueOrDefault(id) : null;
            List<Core.Trading.OpenItem> account = tenancy is null ? [] : [.. items.Where(i => i.TraderId == tenancy.TenantCustomerId)];
            HashSet<string> receipts = ReceiptNumbers(e);
            decimal Signed(Core.Trading.OpenItem i) => i.Sign == Core.Trading.LedgerSign.Debit ? i.OriginalBase : -i.OriginalBase;
            decimal billed = account.Where(i => i.DocumentDate >= fromDate && i.ItemType == Core.Trading.OpenItemType.Invoice).Sum(i => i.OriginalBase);
            decimal receipted = -account.Where(i => i.DocumentDate >= fromDate && receipts.Contains(i.DocumentNumber)).Sum(Signed);
            decimal recovered = deducted.Where(d => d.EmployeeId == e.Id).Sum(d => d.Amount);
            return Row(("Employee", $"{e.EmployeeNumber} {e.FullName}"), ("Tenancy", tenancy?.TenancyNumber ?? "-"), ("Unit", tenancy is null ? "-" : units.GetValueOrDefault(tenancy.UnitId, "?")),
                ("Rent", Money(tenancy?.MonthlyRent ?? 0m)), ("Billed", Money(billed)), ("Deducted", Money(recovered)), ("Receipted", Money(receipted)), ("Balance", Money(account.Sum(Signed))),
                ("Difference", recovered - receipted));
        })];
    }

    // PR35: per employee and period with earnings paid in more than the base currency, each currency's taxable earnings, its share,
    // and the tax attributable to it — on the currency's own table when one is in force, otherwise the base table's tax on the
    // whole remuneration apportioned by share — with the tax deducted apportioned the same way.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> CurrencySplitAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        CurrencyCode baseCurrency = await _db.Companies.AsNoTracking().Where(c => c.Id == companyId).Select(c => c.BaseCurrency).FirstAsync(ct).ConfigureAwait(false);
        Dictionary<string, CurrencyCode> paidIn = (await _db.PayElements.AsNoTracking().Where(e => e.CompanyId == companyId && e.PaidInCurrency != null).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(e => e.Code, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().PaidInCurrency!.Value, StringComparer.OrdinalIgnoreCase);
        List<TaxTable> tables = await TaxTablesAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<long, Employee> employees = await EmployeesAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<(CurrencyCode, DateOnly), decimal?> rates = [];
        async Task<decimal?> RateAsync(CurrencyCode currency, DateOnly date)
        {
            if (currency == baseCurrency)
            {
                return 1m;
            }

            if (!rates.TryGetValue((currency, date), out decimal? rate))
            {
                rate = await Accounting.ExchangeRateResolver.FindAsync(_db, currency, baseCurrency, ExchangeRateType.Daily, date, ct).ConfigureAwait(false);
                rates[(currency, date)] = rate;
            }

            return rate;
        }

        static string Marginal(TaxTable table, decimal amount)
        {
            TaxBand? band = table.Bands.LastOrDefault(b => amount > b.From) ?? table.Bands.FirstOrDefault();
            return band is null ? "-" : $"{table.Code} {band.RatePercent.ToString("0.##", CultureInfo.InvariantCulture)}% from {Money(band.From)}";
        }

        List<IReadOnlyDictionary<string, object?>> rows = [];
        foreach (IGrouping<(long EmployeeId, string Period), Payslip> group in (await PayslipsAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false))
            .GroupBy(p => (p.EmployeeId, p.PeriodLabel)).OrderBy(g => Name(employees, g.Key.EmployeeId), StringComparer.Ordinal).ThenBy(g => g.Key.PeriodLabel, StringComparer.Ordinal))
        {
            List<PayslipLine> taxable = [.. group.SelectMany(p => p.Lines).Where(l => l.Type == PayElementType.Earning && l.Taxable)];
            if (!taxable.Any(l => paidIn.ContainsKey(l.ElementCode)))
            {
                continue;
            }

            DateOnly payDate = group.Min(p => p.PayDate);
            decimal totalBase = taxable.Sum(l => l.Amount);
            decimal deducted = group.SelectMany(p => p.Lines).Where(IsTax).Sum(l => l.Amount);
            TaxTable? baseTable = InForce(tables, payDate);
            decimal baseTax = baseTable is null ? 0m : PayeTable.Tax(baseTable.Bands.Select(b => new PayeBand(b.From, b.To, b.RatePercent)), totalBase);
            foreach (IGrouping<CurrencyCode, PayslipLine> currency in taxable.GroupBy(l => paidIn.TryGetValue(l.ElementCode, out CurrencyCode c) ? c : baseCurrency).OrderBy(g => g.Key == baseCurrency ? 0 : 1)
                .ThenBy(g => g.Key.ToString(), StringComparer.Ordinal))
            {
                decimal earningsBase = currency.Sum(l => l.Amount);
                decimal share = totalBase == 0m ? 0m : earningsBase / totalBase;
                decimal? rate = await RateAsync(currency.Key, payDate).ConfigureAwait(false);
                decimal? earnings = rate is decimal r && r != 0m ? decimal.Round(earningsBase / r, 2, MidpointRounding.ToEven) : null;
                TaxTable? own = tables.LastOrDefault(t => t.Currency == currency.Key && t.EffectiveFrom <= payDate);
                string method;
                decimal taxBase;
                decimal? taxCurrency;
                if (own is not null && earnings is decimal amount)
                {
                    taxCurrency = PayeTable.Tax(own.Bands.Select(b => new PayeBand(b.From, b.To, b.RatePercent)), amount);
                    taxBase = decimal.Round(taxCurrency.Value * rate!.Value, 2, MidpointRounding.ToEven);
                    method = $"Own table, {Marginal(own, amount)}";
                }
                else
                {
                    taxBase = decimal.Round(baseTax * share, 2, MidpointRounding.ToEven);
                    taxCurrency = rate is decimal r2 && r2 != 0m ? decimal.Round(taxBase / r2, 2, MidpointRounding.ToEven) : null;
                    method = baseTable is null ? "No tax table" : $"Apportioned, {Marginal(baseTable, totalBase)}";
                }

                rows.Add(Row(("Employee", Name(employees, group.Key.EmployeeId)), ("Period", group.Key.Period), ("Currency", currency.Key.ToString()),
                    ("Earnings", earnings is decimal e ? Money(e) : "no rate"), ("Rate", rate is decimal x ? x.ToString("0.####", CultureInfo.InvariantCulture) : "no rate"), ("EarningsBase", Money(earningsBase)),
                    ("Share", (share * 100m).ToString("0.0", CultureInfo.InvariantCulture) + "%"), ("Method", method), ("TaxCurrency", taxCurrency is decimal t ? Money(t) : "no rate"),
                    ("Deducted", Money(decimal.Round(deducted * share, 2, MidpointRounding.ToEven))), ("TaxBase", taxBase)));
            }
        }

        return rows;
    }

    // PR71: every deduction suspended by net-pay protection or recovered later in the period, with the arrears still carried forward.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ArrearsAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        Dictionary<long, Employee> employees = await EmployeesAsync(companyId, ct).ConfigureAwait(false);
        List<DeductionArrear> entries = await _db.DeductionArrears.AsNoTracking().Where(a => a.CompanyId == companyId && a.PayDate <= toDate).OrderBy(a => a.PayDate).ThenBy(a => a.Id).ToListAsync(ct).ConfigureAwait(false);
        List<IReadOnlyDictionary<string, object?>> rows = [];
        foreach (IGrouping<(long EmployeeId, string ElementCode), DeductionArrear> group in entries.GroupBy(a => (a.EmployeeId, a.ElementCode))
                     .OrderBy(g => Name(employees, g.Key.EmployeeId), StringComparer.Ordinal).ThenBy(g => g.Key.ElementCode, StringComparer.Ordinal))
        {
            decimal outstanding = 0m;
            foreach (DeductionArrear entry in group)
            {
                outstanding += entry.Movement;
                if (entry.PayDate >= fromDate)
                {
                    rows.Add(Row(("Employee", Name(employees, entry.EmployeeId)), ("Period", entry.PeriodLabel), ("Element", entry.ElementCode), ("Attempted", Money(entry.Attempted)),
                        ("Deducted", Money(entry.Deducted)), ("Reason", entry.Reason), ("Outstanding", outstanding)));
                }
            }
        }

        return rows;
    }

    // PR72: the variable input of every period paid in the range and of every period not yet paid, with what each variable was paid at.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> VariablesAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        Dictionary<long, Employee> employees = await EmployeesAsync(companyId, ct).ConfigureAwait(false);
        List<Payslip> slips = await _db.Payslips.AsNoTracking().Include(p => p.Lines).Where(p => p.CompanyId == companyId && p.RunKind == PayRunKind.Regular).ToListAsync(ct).ConfigureAwait(false);
        HashSet<string> paidInRange = [.. slips.Where(p => p.PayDate >= fromDate && p.PayDate <= toDate).Select(p => p.PeriodLabel)];
        HashSet<string> paidEver = [.. slips.Select(p => p.PeriodLabel)];
        return [.. (await _db.PayrollVariables.AsNoTracking().Where(v => v.CompanyId == companyId).ToListAsync(ct).ConfigureAwait(false))
            .Where(v => paidInRange.Contains(v.PeriodLabel) || !paidEver.Contains(v.PeriodLabel))
            .OrderBy(v => v.PeriodLabel, StringComparer.Ordinal).ThenBy(v => Name(employees, v.EmployeeId), StringComparer.Ordinal).ThenBy(v => v.ElementCode, StringComparer.Ordinal)
            .Select(v =>
            {
                PayslipLine? paid = slips.Where(p => p.EmployeeId == v.EmployeeId && p.PeriodLabel == v.PeriodLabel).SelectMany(p => p.Lines).FirstOrDefault(l => l.ElementCode == v.ElementCode);
                return Row(("Period", v.PeriodLabel), ("Employee", Name(employees, v.EmployeeId)), ("Element", v.ElementCode), ("Units", v.Units is decimal u ? u.ToString("0.##", CultureInfo.InvariantCulture) : "-"),
                    ("Amount", v.Amount is decimal a ? Money(a) : "-"), ("Source", v.Source), ("Status", paid is null ? "Not yet paid" : "Paid"), ("Paid", paid?.Amount ?? 0m));
            })];
    }

    // PR73: the AP invoices raised for payroll third parties in the period, with whether each has been paid.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> RemittanceInvoicesAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        List<RemittanceInvoice> invoices = await _db.RemittanceInvoices.AsNoTracking().Where(r => r.CompanyId == companyId && r.RaisedOn >= fromDate && r.RaisedOn <= toDate)
            .OrderBy(r => r.RaisedOn).ThenBy(r => r.DocumentNumber).ToListAsync(ct).ConfigureAwait(false);
        List<long> supplierIds = [.. invoices.Select(r => r.SupplierId).Distinct()];
        Dictionary<long, string> suppliers = await _db.Suppliers.AsNoTracking().Where(s => supplierIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Code, ct).ConfigureAwait(false);
        List<string> documents = [.. invoices.Select(r => r.DocumentNumber)];
        Dictionary<(long, string), decimal> outstanding = (await _db.OpenItems.AsNoTracking()
                .Where(i => i.CompanyId == companyId && i.Ledger == Core.Trading.TradingLedger.AccountsPayable && supplierIds.Contains(i.TraderId) && documents.Contains(i.DocumentNumber)).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(i => (i.TraderId, i.DocumentNumber)).ToDictionary(g => g.Key, g => g.Sum(i => i.OutstandingBase));
        return [.. invoices.Select(r => Row(("Run", r.RunReference), ("Period", r.PeriodLabel), ("Payee", r.Payee), ("Supplier", suppliers.GetValueOrDefault(r.SupplierId, "?")), ("Account", r.AccountCode),
            ("Invoice", r.DocumentNumber), ("Outstanding", Money(outstanding.GetValueOrDefault((r.SupplierId, r.DocumentNumber)))), ("Amount", r.Amount)))];
    }

    // Each member's lines on the scheme's employee and employer elements in the period.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> SchemeScheduleAsync(long companyId, DateOnly fromDate, DateOnly toDate, Func<SchemeKind, bool> kind, string? schemeCode, CancellationToken ct)
    {
        List<StatutoryScheme> schemes = [.. (await _db.Set<StatutoryScheme>().AsNoTracking().Where(s => s.CompanyId == companyId).OrderBy(s => s.Code).ToListAsync(ct).ConfigureAwait(false))
            .Where(s => kind(s.Kind) && (schemeCode == null || s.Code == schemeCode.ToUpperInvariant()))];
        List<long> ids = [.. schemes.Select(s => s.Id)];
        List<SchemeMembership> members = await _db.Set<SchemeMembership>().AsNoTracking().Where(m => ids.Contains(m.SchemeId) && m.JoinedOn <= toDate && (m.LeftOn == null || m.LeftOn >= fromDate))
            .ToListAsync(ct).ConfigureAwait(false);
        List<Payslip> slips = await PayslipsAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false);
        Dictionary<long, Employee> employees = await EmployeesAsync(companyId, ct).ConfigureAwait(false);
        decimal Sum(long employeeId, string? element) => element is null ? 0m : slips.Where(p => p.EmployeeId == employeeId).SelectMany(p => p.Lines).Where(l => l.ElementCode == element).Sum(l => l.Amount);
        return [.. schemes.SelectMany(s => members.Where(m => m.SchemeId == s.Id).OrderBy(m => Name(employees, m.EmployeeId), StringComparer.Ordinal).Select(m =>
        {
            decimal own = Sum(m.EmployeeId, s.EmployeeElementCode), employer = Sum(m.EmployeeId, s.EmployerElementCode);
            return Row(("Scheme", $"{s.Code} {s.Name}"), ("Employee", Name(employees, m.EmployeeId)), ("Member", m.MemberNumber), ("EmployeePart", Money(own)), ("EmployerPart", Money(employer)), ("Total", own + employer));
        }))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> RemittancesAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        List<StatutoryScheme> schemes = await _db.Set<StatutoryScheme>().AsNoTracking().Where(s => s.CompanyId == companyId).OrderBy(s => s.Code).ToListAsync(ct).ConfigureAwait(false);
        List<Payslip> slips = await PayslipsAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false);
        List<string> periods = [.. slips.Select(p => p.PeriodLabel).Distinct().Order(StringComparer.Ordinal)];
        List<SchemeRemittance> remittances = await _db.Set<SchemeRemittance>().AsNoTracking().Where(r => r.CompanyId == companyId && periods.Contains(r.PeriodLabel)).ToListAsync(ct).ConfigureAwait(false);
        return [.. schemes.SelectMany(s => periods.Select(period =>
        {
            decimal due = slips.Where(p => p.PeriodLabel == period).SelectMany(p => p.Lines).Where(l => l.ElementCode == s.EmployeeElementCode || l.ElementCode == s.EmployerElementCode).Sum(l => l.Amount);
            decimal paid = remittances.Where(r => r.SchemeId == s.Id && r.PeriodLabel == period).Sum(r => r.Amount);
            return (Scheme: s, Period: period, Due: due, Paid: paid);
        })).Where(x => x.Due != 0m || x.Paid != 0m)
            .Select(x => Row(("Scheme", $"{x.Scheme.Code} {x.Scheme.Name}"), ("Period", x.Period), ("Deducted", Money(x.Due)), ("Remitted", Money(x.Paid)),
                ("Status", x.Paid == x.Due ? "Remitted" : x.Paid == 0m ? "Not remitted" : x.Paid < x.Due ? "Part remitted" : "Over remitted"), ("Outstanding", x.Due - x.Paid)))];
    }

    private async Task<List<TaxTable>> TaxTablesAsync(long companyId, CancellationToken ct)
        => await _db.Set<TaxTable>().AsNoTracking().Include(t => t.Bands).Where(t => t.CompanyId == companyId).OrderBy(t => t.EffectiveFrom).ToListAsync(ct).ConfigureAwait(false);

    private static decimal Number(IReadOnlyDictionary<string, object?> parameters, string name, decimal fallback)
        => parameters.TryGetValue(name, out object? value) && value is not null and not "" ? Convert.ToDecimal(value, CultureInfo.InvariantCulture) : fallback;

    private Task<List<AttendanceRecord>> AttendanceAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
        => _db.AttendanceRecords.AsNoTracking().Where(a => a.CompanyId == companyId && a.Date >= fromDate && a.Date <= toDate).OrderBy(a => a.Date).ToListAsync(ct);

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> OvertimeAsync(long companyId, DateOnly fromDate, DateOnly toDate, decimal hoursPerMonth, CancellationToken ct)
    {
        Dictionary<long, Employee> employees = await EmployeesAsync(companyId, ct).ConfigureAwait(false);
        List<AttendanceRecord> days = [.. (await AttendanceAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false)).Where(a => a.OvertimeHours > 0m)];
        return [.. days.GroupBy(a => (a.EmployeeId, a.OvertimeMultiplier)).Select(g => (g, e: employees.GetValueOrDefault(g.Key.EmployeeId)))
            .OrderBy(x => x.e?.Department ?? "—", StringComparer.Ordinal).ThenBy(x => Name(employees, x.g.Key.EmployeeId), StringComparer.Ordinal).ThenBy(x => x.g.Key.OvertimeMultiplier)
            .Select(x =>
            {
                decimal hours = x.g.Sum(a => a.OvertimeHours), approved = x.g.Where(a => a.OvertimeApprovedByUserId != null).Sum(a => a.OvertimeHours);
                return Row(("Department", x.e?.Department ?? "—"), ("Employee", Name(employees, x.g.Key.EmployeeId)), ("Multiplier", Qty(x.g.Key.OvertimeMultiplier)), ("Hours", Qty(hours)),
                    ("Approved", Qty(approved)), ("Unapproved", Qty(hours - approved)), ("Cost", AttendanceRules.OvertimeCost(hours, x.e?.BasicSalary ?? 0m, hoursPerMonth, x.g.Key.OvertimeMultiplier)));
            })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> LeaveTakenAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        Dictionary<long, Employee> employees = await EmployeesAsync(companyId, ct).ConfigureAwait(false);
        List<LeaveRecord> taken = await _db.LeaveRecords.AsNoTracking().Where(l => l.CompanyId == companyId && l.Kind == LeaveRecordKind.Taken && l.FromDate >= fromDate && l.FromDate <= toDate)
            .ToListAsync(ct).ConfigureAwait(false);
        return [.. taken.GroupBy(l => (l.LeaveType, Department: employees.GetValueOrDefault(l.EmployeeId)?.Department ?? "—", Month: l.FromDate.ToString("yyyy-MM", CultureInfo.InvariantCulture)))
            .OrderBy(g => g.Key.LeaveType, StringComparer.Ordinal).ThenBy(g => g.Key.Department, StringComparer.Ordinal).ThenBy(g => g.Key.Month, StringComparer.Ordinal)
            .Select(g => Row(("Type", g.Key.LeaveType), ("Department", g.Key.Department), ("Month", g.Key.Month), ("Employees", g.Select(l => l.EmployeeId).Distinct().Count()), ("Records", g.Count()),
                ("Days", g.Sum(l => l.Days))))];
    }

    // Sick days come from attendance marked sick and sick leave taken; the pattern counts those falling on a Monday or Friday.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> AbsenceAsync(long companyId, DateOnly fromDate, DateOnly toDate, decimal fullPayDays, decimal halfPayDays, CancellationToken ct)
    {
        Dictionary<long, Employee> employees = await EmployeesAsync(companyId, ct).ConfigureAwait(false);
        DateOnly yearBack = toDate.AddMonths(-12).AddDays(1);
        List<AttendanceRecord> days = await AttendanceAsync(companyId, yearBack < fromDate ? yearBack : fromDate, toDate, ct).ConfigureAwait(false);
        List<LeaveRecord> sickLeave = await _db.LeaveRecords.AsNoTracking().Where(l => l.CompanyId == companyId && l.Kind == LeaveRecordKind.Taken && l.LeaveType.ToUpper() == "SICK" && l.ToDate >= yearBack && l.FromDate <= toDate)
            .ToListAsync(ct).ConfigureAwait(false);
        static bool MonFri(DateOnly d) => d.DayOfWeek is DayOfWeek.Monday or DayOfWeek.Friday;
        return [.. days.Where(a => a.Date >= fromDate).Select(a => a.EmployeeId).Concat(sickLeave.Where(l => l.ToDate >= fromDate).Select(l => l.EmployeeId)).Distinct()
            .OrderBy(id => Name(employees, id), StringComparer.Ordinal).Select(id =>
            {
                List<AttendanceRecord> own = [.. days.Where(a => a.EmployeeId == id)], inPeriod = [.. own.Where(a => a.Date >= fromDate)];
                List<LeaveRecord> leave = [.. sickLeave.Where(l => l.EmployeeId == id)];
                decimal sickPeriod = inPeriod.Count(a => a.Status == AttendanceStatus.Sick) + leave.Where(l => l.FromDate >= fromDate).Sum(l => l.Days);
                decimal rolling = own.Count(a => a.Status == AttendanceStatus.Sick) + leave.Sum(l => l.Days);
                int monFri = inPeriod.Count(a => a.Status == AttendanceStatus.Sick && MonFri(a.Date)) + leave.Count(l => l.FromDate >= fromDate && MonFri(l.FromDate));
                return Row(("Employee", Name(employees, id)), ("Absent", inPeriod.Count(a => a.Status == AttendanceStatus.Absent)), ("Late", inPeriod.Count(a => a.LateMinutes > 0)),
                    ("LateMinutes", inPeriod.Sum(a => a.LateMinutes)), ("Sick", Qty(sickPeriod)), ("MonFri", monFri), ("Rolling", Qty(rolling)), ("Tier", AttendanceRules.SickTier(rolling, fullPayDays, halfPayDays)));
            })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> EncashmentAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        Dictionary<long, Employee> employees = await EmployeesAsync(companyId, ct).ConfigureAwait(false);
        List<LeaveRecord> paid = await _db.LeaveRecords.AsNoTracking().Where(l => l.CompanyId == companyId && l.Kind == LeaveRecordKind.Encashed && l.FromDate >= fromDate && l.FromDate <= toDate)
            .OrderBy(l => l.FromDate).ToListAsync(ct).ConfigureAwait(false);
        return [.. paid.Select(l => Row(("Date", Date(l.FromDate)), ("Employee", Name(employees, l.EmployeeId)), ("Type", l.LeaveType), ("Days", Qty(l.Days)), ("Rate", Money(l.Days == 0m ? 0m : l.Amount / l.Days)),
            ("Amount", l.Amount)))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> AttendanceExceptionsAsync(long companyId, DateOnly fromDate, DateOnly toDate, decimal maxHours, CancellationToken ct)
    {
        Dictionary<long, Employee> employees = await EmployeesAsync(companyId, ct).ConfigureAwait(false);
        List<IReadOnlyDictionary<string, object?>> rows = [];
        foreach (AttendanceRecord a in (await AttendanceAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false)).Where(a => a.Status == AttendanceStatus.Present))
        {
            decimal? hours = AttendanceRules.HoursWorked(a.ClockIn, a.ClockOut);
            List<string> issues = [];
            if (a.ClockIn is null)
            {
                issues.Add("No clock-in");
            }

            if (a.ClockOut is null)
            {
                issues.Add("No clock-out");
            }

            if (a.OvertimeHours > 0m && a.OvertimeApprovedByUserId is null)
            {
                issues.Add($"Unapproved overtime {Qty(a.OvertimeHours)} h");
            }

            if (hours > maxHours)
            {
                issues.Add($"Over {Qty(maxHours)} hours");
            }

            if (issues.Count > 0)
            {
                rows.Add(Row(("Date", Date(a.Date)), ("Employee", Name(employees, a.EmployeeId)), ("In", a.ClockIn?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "—"),
                    ("Out", a.ClockOut?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "—"), ("Hours", hours is decimal h ? Qty(h) : "—"), ("Exception", string.Join("; ", issues))));
            }
        }

        return rows;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> SplitPayAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        Dictionary<long, Employee> employees = await EmployeesAsync(companyId, ct).ConfigureAwait(false);
        List<EmployeePaymentSplit> splits = await _db.PaymentSplits.AsNoTracking().Where(s => s.CompanyId == companyId).ToListAsync(ct).ConfigureAwait(false);
        List<Payslip> slips = await PayslipsAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false);
        return [.. splits.GroupBy(s => s.EmployeeId).OrderBy(g => Name(employees, g.Key), StringComparer.Ordinal).Select(g =>
        {
            Employee? e = employees.GetValueOrDefault(g.Key);
            decimal net = slips.Where(p => p.EmployeeId == g.Key).OrderBy(p => p.PayDate).Select(p => p.NetPay).LastOrDefault();
            string primary = e is null ? "?" : e.PaymentMethod == PayMethod.MobileMoney ? $"MobileMoney {e.MobileMoneyNumber ?? "—"}" : $"{e.PaymentMethod} {e.BankAccountNumber ?? "—"}";
            return Row(("Employee", Name(employees, g.Key)), ("Primary", primary),
                ("Splits", string.Join("; ", g.Select(s => $"{s.Method} {s.Destination} {(s.Percent is decimal p ? $"{p:0.##}%" : Money(s.Amount))}"))), ("Net", Money(net)),
                ("Remainder", Math.Max(0m, net - g.Sum(s => s.AmountFrom(net)))));
        })];
    }

    // The tax effect of a benefit is the PAYE on the payslip's taxable pay less the PAYE without the benefit, at the table in force.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> BenefitsAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        Dictionary<long, Employee> employees = await EmployeesAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<string, PayElement> benefits = await _db.PayElements.AsNoTracking().Where(p => p.CompanyId == companyId && p.BenefitCategory != null).ToDictionaryAsync(p => p.Code, ct).ConfigureAwait(false);
        List<TaxTable> tables = await _db.Set<TaxTable>().AsNoTracking().Include(t => t.Bands).Where(t => t.CompanyId == companyId).OrderBy(t => t.EffectiveFrom).ToListAsync(ct).ConfigureAwait(false);
        List<IReadOnlyDictionary<string, object?>> rows = [];
        foreach (Payslip slip in (await PayslipsAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false)).OrderBy(p => Name(employees, p.EmployeeId), StringComparer.Ordinal).ThenBy(p => p.PayDate))
        {
            decimal taxable = slip.Lines.Where(l => l.Type == PayElementType.Earning && l.Taxable).Sum(l => l.Amount);
            List<PayeBand> bands = [.. (InForce(tables, slip.PayDate)?.Bands ?? []).Select(b => new PayeBand(b.From, b.To, b.RatePercent))];
            foreach (PayslipLine line in slip.Lines.Where(l => benefits.ContainsKey(l.ElementCode)))
            {
                decimal effect = bands.Count == 0 || !line.Taxable ? 0m : PayeTable.Tax(bands, taxable) - PayeTable.Tax(bands, taxable - line.Amount);
                rows.Add(Row(("Employee", Name(employees, slip.EmployeeId)), ("Category", benefits[line.ElementCode].BenefitCategory), ("Element", benefits[line.ElementCode].Name), ("Value", Money(line.Amount)),
                    ("TaxEffect", effect)));
            }
        }

        return rows;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> FinalSettlementAsync(long companyId, DateOnly fromDate, DateOnly toDate, TerminalPolicy policy, CancellationToken ct)
    {
        List<Employee> leavers = [.. (await EmployeesAsync(companyId, ct).ConfigureAwait(false)).Values.Where(e => e.TerminationDate is DateOnly left && left >= fromDate && left <= toDate)
            .OrderBy(e => e.TerminationDate).ThenBy(e => e.EmployeeNumber, StringComparer.Ordinal)];
        List<long> ids = [.. leavers.Select(e => e.Id)];
        List<EmployeeLoan> loans = await _db.Set<EmployeeLoan>().AsNoTracking().Where(l => ids.Contains(l.EmployeeId) && l.Outstanding > 0m).ToListAsync(ct).ConfigureAwait(false);
        List<EmployeeLeaveBalance> leave = await _db.Set<EmployeeLeaveBalance>().AsNoTracking().Where(l => ids.Contains(l.EmployeeId) && l.LeaveType.ToUpper() == "ANNUAL").ToListAsync(ct).ConfigureAwait(false);
        return [.. leavers.Select(e =>
        {
            bool retrenched = (e.TerminationReason ?? string.Empty).Contains("retrench", StringComparison.OrdinalIgnoreCase);
            FinalSettlement s = TerminalBenefits.Settle(e.BasicSalary, e.EngagementDate, e.TerminationDate!.Value, leave.Where(l => l.EmployeeId == e.Id).Sum(l => l.AccruedDays - l.TakenDays), retrenched,
                loans.Where(l => l.EmployeeId == e.Id).Sum(l => l.Outstanding), policy);
            return Row(("Employee", $"{e.EmployeeNumber} {e.FullName}"), ("Left", Date(e.TerminationDate!.Value)), ("Reason", e.TerminationReason ?? "—"), ("Years", s.Years), ("Notice", Money(s.Notice)),
                ("Leave", Money(s.Leave)), ("Gratuity", Money(s.Gratuity)), ("Severance", Money(s.Severance)), ("Recoveries", Money(s.Recoveries)), ("Tax", Money(s.Tax)), ("Net", s.Net));
        })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GratuityAsync(long companyId, DateOnly asAt, decimal weeksPerYear, int retirementAge, decimal discountRate, CancellationToken ct)
        => [.. (await EmployeesAsync(companyId, ct).ConfigureAwait(false)).Values.Where(e => EmployedOn(e, asAt)).OrderBy(e => e.EmployeeNumber, StringComparer.Ordinal).Select(e =>
        {
            var p = TerminalBenefits.Provision(e.BasicSalary, e.EngagementDate, asAt, e.DateOfBirth, weeksPerYear, retirementAge, discountRate);
            return Row(("Employee", $"{e.EmployeeNumber} {e.FullName}"), ("Years", TerminalBenefits.CompletedYears(e.EngagementDate, asAt)), ("Accrued", Money(p.Accrued)),
                ("ToRetirement", e.DateOfBirth is null ? "—" : p.YearsToRetirement.ToString(CultureInfo.InvariantCulture)), ("Provision", p.Provision));
        })];

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> LabourMixAsync(long companyId, DateOnly fromDate, DateOnly toDate, decimal hoursPerMonth, CancellationToken ct)
    {
        Dictionary<long, Employee> employees = await EmployeesAsync(companyId, ct).ConfigureAwait(false);
        List<Payslip> slips = await PayslipsAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false);
        List<AttendanceRecord> overtime = [.. (await AttendanceAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false)).Where(a => a.OvertimeHours > 0m)];
        string Department(long id) => employees.GetValueOrDefault(id)?.Department ?? "—";
        bool Casual(long id) => employees.GetValueOrDefault(id)?.EmploymentType == EmploymentType.Casual;
        return [.. slips.Select(p => Department(p.EmployeeId)).Concat(overtime.Select(a => Department(a.EmployeeId))).Distinct().Order(StringComparer.Ordinal).Select(d =>
        {
            decimal permanent = slips.Where(p => Department(p.EmployeeId) == d && !Casual(p.EmployeeId)).Sum(p => p.GrossEarnings);
            decimal casual = slips.Where(p => Department(p.EmployeeId) == d && Casual(p.EmployeeId)).Sum(p => p.GrossEarnings);
            decimal ot = overtime.Where(a => Department(a.EmployeeId) == d)
                .Sum(a => AttendanceRules.OvertimeCost(a.OvertimeHours, employees.GetValueOrDefault(a.EmployeeId)?.BasicSalary ?? 0m, hoursPerMonth, a.OvertimeMultiplier));
            return Row(("Department", d), ("Permanent", Money(permanent)), ("Casual", Money(casual)), ("Overtime", Money(ot)),
                ("Share", permanent == 0m ? "—" : ((casual + ot) * 100m / permanent).ToString("0.0", CultureInfo.InvariantCulture) + "%"), ("Total", permanent + casual + ot));
        })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ReprintsAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        Dictionary<long, Employee> employees = await EmployeesAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<long, string> users = await _db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Username, ct).ConfigureAwait(false);
        List<PayslipPrint> prints = await _db.PayslipPrints.AsNoTracking().Where(p => p.CompanyId == companyId).OrderBy(p => p.Id).ToListAsync(ct).ConfigureAwait(false);
        DateTimeOffset start = Start(fromDate), end = End(toDate);
        return [.. prints.GroupBy(p => (p.EmployeeId, p.PeriodLabel, p.PayDate)).SelectMany(g => g.Select((p, i) => (p, Number: i + 1)))
            .Where(x => x.Number > 1 && x.p.CreatedOnUtc >= start && x.p.CreatedOnUtc <= end).OrderBy(x => x.p.CreatedOnUtc)
            .Select(x => Row(("Employee", Name(employees, x.p.EmployeeId)), ("Period", x.p.PeriodLabel), ("Print", x.Number), ("When", x.p.CreatedOnUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)),
                ("User", users.GetValueOrDefault(x.p.CreatedByUserId, x.p.CreatedByUserId == 0 ? "system" : $"user {x.p.CreatedByUserId}"))))];
    }

    private static int Days(IReadOnlyDictionary<string, object?> parameters)
        => parameters.TryGetValue("Days", out object? days) && days is not null and not "" ? Convert.ToInt32(days, CultureInfo.InvariantCulture) : 30;

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GarnisheesAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        Dictionary<long, Employee> employees = await EmployeesAsync(companyId, ct).ConfigureAwait(false);
        List<GarnisheeOrder> orders = await _db.GarnisheeOrders.AsNoTracking().Where(g => g.CompanyId == companyId && g.IssuedOn <= asAt).ToListAsync(ct).ConfigureAwait(false);
        List<string> codes = [.. orders.Select(o => o.ElementCode).Distinct()];
        List<long> ids = [.. orders.Select(o => o.EmployeeId).Distinct()];
        var deducted = await (from p in _db.Set<Payslip>().AsNoTracking()
                              from l in p.Lines
                              where p.CompanyId == companyId && ids.Contains(p.EmployeeId) && codes.Contains(l.ElementCode) && p.PayDate <= asAt
                              select new { p.EmployeeId, l.ElementCode, p.PayDate, l.Amount }).ToListAsync(ct).ConfigureAwait(false);
        return [.. orders.OrderBy(o => Name(employees, o.EmployeeId), StringComparer.Ordinal).ThenBy(o => o.Priority).Select(o =>
        {
            decimal taken = Math.Min(o.TotalAmount, deducted.Where(d => d.EmployeeId == o.EmployeeId && d.ElementCode == o.ElementCode && d.PayDate >= o.IssuedOn).Sum(d => d.Amount));
            return Row(("Employee", Name(employees, o.EmployeeId)), ("Reference", o.Reference), ("Creditor", o.Creditor), ("Priority", o.Priority), ("Issued", Date(o.IssuedOn)), ("Instalment", Money(o.Instalment)),
                ("Total", Money(o.TotalAmount)), ("Deducted", Money(taken)), ("Balance", o.TotalAmount - taken));
        })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> EstablishmentAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        List<Employee> inService = [.. (await EmployeesAsync(companyId, ct).ConfigureAwait(false)).Values.Where(e => EmployedOn(e, asAt))];
        List<EstablishmentPost> posts = await _db.EstablishmentPosts.AsNoTracking().Where(p => p.CompanyId == companyId).ToListAsync(ct).ConfigureAwait(false);
        var keys = posts.Select(p => (p.Department, p.Grade)).Concat(inService.Select(e => (Department: e.Department ?? "—", Grade: e.Grade ?? "—"))).Distinct()
            .OrderBy(k => k.Department, StringComparer.Ordinal).ThenBy(k => k.Grade, StringComparer.Ordinal);
        return [.. keys.Select(k =>
        {
            EstablishmentPost? post = posts.FirstOrDefault(p => p.Department == k.Department && p.Grade == k.Grade);
            int approved = post?.ApprovedPosts ?? 0, filled = inService.Count(e => (e.Department ?? "—") == k.Department && (e.Grade ?? "—") == k.Grade);
            int vacant = Math.Max(0, approved - filled), over = Math.Max(0, filled - approved);
            return Row(("Department", k.Department), ("Grade", k.Grade), ("Approved", approved), ("Filled", filled), ("Vacant", vacant), ("Over", over), ("Saving", vacant * (post?.AnnualCostPerPost ?? 0m)));
        })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> DocumentsAsync(long companyId, DateOnly asAt, int windowDays, string? requiredTypes, CancellationToken ct)
    {
        List<Employee> inService = [.. (await EmployeesAsync(companyId, ct).ConfigureAwait(false)).Values.Where(e => EmployedOn(e, asAt)).OrderBy(e => e.EmployeeNumber, StringComparer.Ordinal)];
        List<EmployeeDocument> documents = await _db.EmployeeDocuments.AsNoTracking().Where(d => d.CompanyId == companyId).ToListAsync(ct).ConfigureAwait(false);
        IReadOnlyList<string> required = HrRules.RequiredTypes(requiredTypes);
        List<IReadOnlyDictionary<string, object?>> rows = [];
        foreach (Employee e in inService)
        {
            List<EmployeeDocument> own = [.. documents.Where(d => d.EmployeeId == e.Id)];
            foreach (string missing in required.Where(t => own.All(d => d.DocumentType != t)))
            {
                rows.Add(Row(("Employee", $"{e.EmployeeNumber} {e.FullName}"), ("Type", missing), ("Reference", "—"), ("Expires", "—"), ("Status", "Missing")));
            }

            foreach (EmployeeDocument d in own.Where(d => HrRules.Status(d.ExpiresOn, asAt, windowDays) != "Valid").OrderBy(d => d.ExpiresOn))
            {
                rows.Add(Row(("Employee", $"{e.EmployeeNumber} {e.FullName}"), ("Type", d.DocumentType), ("Reference", d.Reference ?? "—"), ("Expires", Date(d.ExpiresOn!.Value)),
                    ("Status", HrRules.Status(d.ExpiresOn, asAt, windowDays))));
            }
        }

        return rows;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> DisciplinaryAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        Dictionary<long, Employee> employees = await EmployeesAsync(companyId, ct).ConfigureAwait(false);
        List<DisciplinaryRecord> records = await _db.DisciplinaryRecords.AsNoTracking().Where(d => d.CompanyId == companyId && d.Date <= asAt).OrderBy(d => d.Date).ToListAsync(ct).ConfigureAwait(false);
        return [.. records.Select(d => Row(("Employee", Name(employees, d.EmployeeId)), ("Date", Date(d.Date)), ("Offence", d.Offence), ("Sanction", d.Sanction.ToString()),
            ("Expires", d.ExpiresOn is DateOnly e ? Date(e) : "—"), ("Status", d.ExpiresOn is DateOnly lapse && lapse < asAt ? "Lapsed" : "Live")))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> TrainingAsync(long companyId, DateOnly asAt, int windowDays, CancellationToken ct)
    {
        Dictionary<long, Employee> employees = await EmployeesAsync(companyId, ct).ConfigureAwait(false);
        List<TrainingRecord> training = await _db.TrainingRecords.AsNoTracking().Where(t => t.CompanyId == companyId && t.CompletedOn <= asAt).ToListAsync(ct).ConfigureAwait(false);
        List<EmployeeAppraisal> appraisals = await _db.EmployeeAppraisals.AsNoTracking().Where(a => a.CompanyId == companyId && a.Date <= asAt).ToListAsync(ct).ConfigureAwait(false);
        return [.. training.OrderBy(t => Name(employees, t.EmployeeId), StringComparer.Ordinal).ThenBy(t => t.CompletedOn).Select(t => Row(("Section", "Training"), ("Employee", Name(employees, t.EmployeeId)),
                ("Item", t.Provider is null ? t.Course : $"{t.Course} ({t.Provider})"), ("Date", Date(t.CompletedOn)), ("Due", t.ExpiresOn is DateOnly e ? Date(e) : "—"),
                ("Status", HrRules.Status(t.ExpiresOn, asAt, windowDays)), ("Cost", t.Cost))),
            .. appraisals.GroupBy(a => a.EmployeeId).Select(g => g.OrderBy(a => a.Date).Last()).Where(a => employees.TryGetValue(a.EmployeeId, out Employee? e) && EmployedOn(e, asAt))
                .OrderBy(a => a.NextDue).Select(a => Row(("Section", "Appraisal"), ("Employee", Name(employees, a.EmployeeId)), ("Item", $"Rated {a.Rating}"), ("Date", Date(a.Date)), ("Due", Date(a.NextDue)),
                    ("Status", a.NextDue < asAt ? "Overdue" : a.NextDue.DayNumber - asAt.DayNumber <= windowDays ? "Due soon" : "Planned"), ("Cost", 0m)))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> LoansAsync(long companyId, CancellationToken ct)
    {
        Dictionary<long, Employee> employees = await EmployeesAsync(companyId, ct).ConfigureAwait(false);
        List<EmployeeLoan> loans = await _db.Set<EmployeeLoan>().AsNoTracking().Where(l => l.CompanyId == companyId).ToListAsync(ct).ConfigureAwait(false);
        return [.. loans.OrderBy(l => Name(employees, l.EmployeeId), StringComparer.Ordinal).ThenBy(l => l.GrantedOn).Select(l => Row(("Employee", Name(employees, l.EmployeeId)), ("Reference", l.Reference),
            ("Granted", Date(l.GrantedOn)), ("Principal", Money(l.Principal)), ("Instalment", Money(l.Installment)), ("Repaid", Money(l.Principal - l.Outstanding)),
            ("Left", l.Outstanding <= 0m || l.Installment <= 0m ? "0" : Math.Ceiling(l.Outstanding / l.Installment).ToString(CultureInfo.InvariantCulture)), ("Status", l.Status.ToString()), ("Outstanding", Math.Max(0m, l.Outstanding))))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> JobAllocationAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        Dictionary<long, Employee> employees = await EmployeesAsync(companyId, ct).ConfigureAwait(false);
        List<Core.JobCosting.JobTimesheet> sheets = await _db.JobTimesheets.AsNoTracking().Where(t => t.CompanyId == companyId && t.Date >= fromDate && t.Date <= toDate).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, string> contracts = await _db.JobContracts.AsNoTracking().IgnoreQueryFilters([Persistence.One4AllDbContext.SoftDeleteFilter]).Where(c => c.CompanyId == companyId).ToDictionaryAsync(c => c.Id, c => c.ContractNumber, ct).ConfigureAwait(false);
        Dictionary<long, decimal> gross = (await PayslipsAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false)).GroupBy(p => p.EmployeeId).ToDictionary(g => g.Key, g => g.Sum(p => p.GrossEarnings));
        return [.. sheets.Select(t => t.EmployeeId).Concat(gross.Keys).Distinct().OrderBy(id => Name(employees, id), StringComparer.Ordinal).Select(id =>
        {
            List<Core.JobCosting.JobTimesheet> own = [.. sheets.Where(t => t.EmployeeId == id)];
            decimal allocated = own.Sum(t => t.Cost), paid = gross.GetValueOrDefault(id);
            return Row(("Employee", Name(employees, id)), ("Jobs", own.Count == 0 ? "—" : string.Join(", ", own.Select(t => contracts.GetValueOrDefault(t.JobContractId, "?")).Distinct().Order(StringComparer.Ordinal))),
                ("Hours", Qty(own.Sum(t => t.Hours))), ("Allocated", Money(allocated)), ("Gross", Money(paid)), ("Share", paid == 0m ? "—" : (allocated * 100m / paid).ToString("0.0", CultureInfo.InvariantCulture) + "%"),
                ("Unallocated", paid - allocated));
        })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ClearanceAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        List<Employee> leavers = [.. (await EmployeesAsync(companyId, ct).ConfigureAwait(false)).Values.Where(e => e.TerminationDate is DateOnly left && left <= asAt)];
        List<long> ids = [.. leavers.Select(e => e.Id)];
        List<EmployeeLoan> loans = await _db.Set<EmployeeLoan>().AsNoTracking().Where(l => ids.Contains(l.EmployeeId) && l.Outstanding > 0m).ToListAsync(ct).ConfigureAwait(false);
        List<EmployeeLeaveBalance> leave = await _db.Set<EmployeeLeaveBalance>().AsNoTracking().Where(l => ids.Contains(l.EmployeeId)).ToListAsync(ct).ConfigureAwait(false);
        List<Core.FixedAssets.FixedAsset> assets = await _db.FixedAssets.AsNoTracking()
            .Where(a => a.CompanyId == companyId && a.Status != Core.FixedAssets.FixedAssetStatus.Disposed && a.Custodian != null).ToListAsync(ct).ConfigureAwait(false);
        List<IReadOnlyDictionary<string, object?>> rows = [];
        foreach (Employee e in leavers.OrderBy(e => e.TerminationDate).ThenBy(e => e.EmployeeNumber, StringComparer.Ordinal))
        {
            List<string> held = [.. assets.Where(a => string.Equals(a.Custodian, e.FullName, StringComparison.OrdinalIgnoreCase) || string.Equals(a.Custodian, e.EmployeeNumber, StringComparison.OrdinalIgnoreCase))
                .Select(a => a.AssetNumber).Order(StringComparer.Ordinal)];
            decimal owed = loans.Where(l => l.EmployeeId == e.Id).Sum(l => l.Outstanding), days = leave.Where(l => l.EmployeeId == e.Id).Sum(l => l.AccruedDays - l.TakenDays);
            int items = held.Count + (owed > 0m ? 1 : 0) + (days > 0m ? 1 : 0);
            if (items > 0)
            {
                rows.Add(Row(("Employee", $"{e.EmployeeNumber} {e.FullName}"), ("Left", Date(e.TerminationDate!.Value)), ("Reason", e.TerminationReason ?? "—"), ("Assets", held.Count == 0 ? "—" : string.Join(", ", held)),
                    ("Leave", Qty(Math.Max(0m, days))), ("Items", items), ("Loans", owed)));
            }
        }

        return rows;
    }

    // Unpaid for the month of the date: in service at the date and no payslip dated in that month.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> AccrualAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        DateOnly monthStart = new(asAt.Year, asAt.Month, 1);
        Dictionary<long, Employee> employees = await EmployeesAsync(companyId, ct).ConfigureAwait(false);
        List<Payslip> slips = await _db.Set<Payslip>().AsNoTracking().Where(p => p.CompanyId == companyId && p.PayDate <= asAt).ToListAsync(ct).ConfigureAwait(false);
        return [.. employees.Values.Where(e => EmployedOn(e, asAt) && !slips.Any(p => p.EmployeeId == e.Id && p.PayDate >= monthStart)).OrderBy(e => e.EmployeeNumber, StringComparer.Ordinal).Select(e =>
        {
            DateOnly? last = slips.Where(p => p.EmployeeId == e.Id).Select(p => (DateOnly?)p.PayDate).Max();
            return Row(("Employee", $"{e.EmployeeNumber} {e.FullName}"), ("Department", e.Department ?? "—"), ("LastPaid", last is DateOnly d ? Date(d) : "never"),
                ("Status", e.HoldReason is string reason ? $"Salary held: {reason}" : "Not yet paid"), ("Accrual", e.BasicSalary));
        })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> SodAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        Dictionary<long, Employee> employees = await EmployeesAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<long, string> users = await _db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Username, ct).ConfigureAwait(false);
        List<PayrollRunRecord> runs = await _db.Set<PayrollRunRecord>().AsNoTracking().Where(r => r.CompanyId == companyId && r.PayDate >= fromDate && r.PayDate <= toDate).OrderBy(r => r.PayDate).ToListAsync(ct).ConfigureAwait(false);
        // Changes are judged by when they were made: those since the previous run was processed, up to this run's processing.
        List<DateTimeOffset> processed = await _db.Set<PayrollRunRecord>().AsNoTracking().Where(r => r.CompanyId == companyId).Select(r => r.CreatedOnUtc).ToListAsync(ct).ConfigureAwait(false);
        DateTimeOffset latest = runs.Count == 0 ? DateTimeOffset.MinValue : runs.Max(r => r.CreatedOnUtc);
        List<Core.Audit.ChangeLogEntry> changes = await _db.ChangeLog.AsNoTracking().Where(c => c.CompanyId == companyId && c.EntityType == "Employee" && c.Action == Core.Audit.AuditAction.Update
            && c.UserId != null && c.OccurredOnUtc <= latest).ToListAsync(ct).ConfigureAwait(false);
        List<Payslip> slips = await PayslipsAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false);
        List<IReadOnlyDictionary<string, object?>> rows = [];
        foreach (PayrollRunRecord run in runs)
        {
            string user = users.GetValueOrDefault(run.CreatedByUserId, $"user {run.CreatedByUserId}");
            DateTimeOffset since = processed.Where(p => p < run.CreatedOnUtc).DefaultIfEmpty(DateTimeOffset.MinValue).Max();
            foreach (Core.Audit.ChangeLogEntry change in changes.Where(c => c.UserId == run.CreatedByUserId && c.OccurredOnUtc > since && c.OccurredOnUtc <= run.CreatedOnUtc
                         && c.ChangedFields.Any(f => f == "BasicSalary" || PaymentFields.Contains(f))))
            {
                string who = long.TryParse(change.EntityId, out long id) ? Name(employees, id) : change.EntityId ?? "?";
                rows.Add(Row(("User", user), ("Violation", "Changed pay or payment details then processed the run"),
                    ("Detail", $"{who}: {string.Join(", ", change.ChangedFields.Where(f => f == "BasicSalary" || PaymentFields.Contains(f)))} on {change.OccurredOnUtc:yyyy-MM-dd HH:mm}"),
                    ("Run", run.PeriodLabel), ("When", run.CreatedOnUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))));
            }

            foreach (Payslip own in slips.Where(p => p.PeriodLabel == run.PeriodLabel && p.PayDate == run.PayDate && employees.TryGetValue(p.EmployeeId, out Employee? e) && e.UserId == run.CreatedByUserId))
            {
                rows.Add(Row(("User", user), ("Violation", "Processed a run containing their own pay"), ("Detail", $"{Name(employees, own.EmployeeId)} net {Money(own.NetPay)}"),
                    ("Run", run.PeriodLabel), ("When", run.CreatedOnUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))));
            }
        }

        return rows;
    }

    private static TaxTable? InForce(List<TaxTable> tables, DateOnly date) => tables.LastOrDefault(t => t.Currency == null && t.EffectiveFrom <= date);

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> PayeReturnAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        List<TaxTable> tables = await TaxTablesAsync(companyId, ct).ConfigureAwait(false);
        return [.. (await PayslipsAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false)).GroupBy(p => p.PayDate.ToString("yyyy-MM", CultureInfo.InvariantCulture)).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                decimal taxable = g.SelectMany(p => p.Lines).Where(l => l.Type == PayElementType.Earning && l.Taxable).Sum(l => l.Amount);
                decimal deducted = g.SelectMany(p => p.Lines).Where(IsTax).Sum(l => l.Amount);
                decimal? byTable = g.All(p => InForce(tables, p.PayDate) is not null)
                    ? g.Sum(p => PayeTable.Tax(InForce(tables, p.PayDate)!.Bands.Select(b => new PayeBand(b.From, b.To, b.RatePercent)), p.Lines.Where(l => l.Type == PayElementType.Earning && l.Taxable).Sum(l => l.Amount)))
                    : null;
                return Row(("Month", g.Key), ("Employees", g.Select(p => p.EmployeeId).Distinct().Count()), ("Taxable", Money(taxable)), ("ByTable", byTable is decimal t ? Money(t) : "no table"),
                    ("Difference", byTable is decimal d ? Money(deducted - d) : "—"), ("Deducted", deducted));
            })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> CertificatesAsync(long companyId, DateOnly toDate, CancellationToken ct)
    {
        Dictionary<long, Employee> employees = await EmployeesAsync(companyId, ct).ConfigureAwait(false);
        HashSet<string> pension = [.. await _db.Set<StatutoryScheme>().AsNoTracking().Where(s => s.CompanyId == companyId && s.Kind == SchemeKind.Pension && s.EmployeeElementCode != null)
            .Select(s => s.EmployeeElementCode!).ToListAsync(ct).ConfigureAwait(false)];
        return [.. (await PayslipsAsync(companyId, new DateOnly(toDate.Year, 1, 1), toDate, ct).ConfigureAwait(false)).GroupBy(p => p.EmployeeId).OrderBy(g => Name(employees, g.Key), StringComparer.Ordinal)
            .Select(g => Row(("Employee", Name(employees, g.Key)), ("NationalId", employees.GetValueOrDefault(g.Key)?.NationalIdNumber ?? "MISSING"), ("TaxNumber", employees.GetValueOrDefault(g.Key)?.TaxNumber ?? "MISSING"),
                ("Gross", Money(g.Sum(p => p.GrossEarnings))), ("Taxable", Money(g.SelectMany(p => p.Lines).Where(l => l.Type == PayElementType.Earning && l.Taxable).Sum(l => l.Amount))),
                ("Pension", Money(g.SelectMany(p => p.Lines).Where(l => pension.Contains(l.ElementCode)).Sum(l => l.Amount))), ("Paye", g.SelectMany(p => p.Lines).Where(IsTax).Sum(l => l.Amount))))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> TaxTableAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        TaxTable? table = InForce(await TaxTablesAsync(companyId, ct).ConfigureAwait(false), asAt);
        if (table is null)
        {
            return [];
        }

        List<PayeBand> bands = [.. table.Bands.Select(b => new PayeBand(b.From, b.To, b.RatePercent))];
        return [.. bands.Select(b => Row(("Table", table.Code), ("Effective", Date(table.EffectiveFrom)), ("From", Money(b.From)), ("To", b.To is decimal to ? Money(to) : "and above"),
            ("Rate", b.RatePercent.ToString("0.##", CultureInfo.InvariantCulture)), ("Cumulative", b.To is decimal top ? Money(PayeTable.Tax(bands, top)) : "—")))];
    }

    private static string? Text(IReadOnlyDictionary<string, object?> parameters, string name)
        => parameters.TryGetValue(name, out object? value) && value is string text && text.Trim().Length > 0 ? text.Trim() : null;

    // Allowances are the earning lines other than the BASIC element.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> AllowancesAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        Dictionary<long, Employee> employees = await EmployeesAsync(companyId, ct).ConfigureAwait(false);
        return [.. (await PayslipsAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false))
            .SelectMany(p => p.Lines.Where(l => l.Type == PayElementType.Earning && !l.ElementCode.Equals("BASIC", StringComparison.OrdinalIgnoreCase) && l.Amount != 0m).Select(l => (p.EmployeeId, Line: l)))
            .GroupBy(x => (x.EmployeeId, x.Line.ElementCode)).OrderBy(g => Name(employees, g.Key.EmployeeId), StringComparer.Ordinal).ThenBy(g => g.Key.ElementCode, StringComparer.Ordinal)
            .Select(g => Row(("Employee", Name(employees, g.Key.EmployeeId)), ("Department", employees.GetValueOrDefault(g.Key.EmployeeId)?.Department ?? "—"), ("Element", g.Key.ElementCode),
                ("Description", g.First().Line.Description), ("Taxable", g.First().Line.Taxable ? "Yes" : "No"), ("Amount", g.Sum(x => x.Line.Amount))))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> CostToCompanyAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        Dictionary<long, Employee> employees = await EmployeesAsync(companyId, ct).ConfigureAwait(false);
        return [.. (await PayslipsAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false)).GroupBy(p => p.EmployeeId).OrderBy(g => Name(employees, g.Key), StringComparer.Ordinal).Select(g =>
        {
            int months = g.Select(p => p.PeriodLabel).Distinct().Count();
            decimal gross = g.Sum(p => p.GrossEarnings), employer = g.SelectMany(p => p.Lines).Where(l => l.Type == PayElementType.EmployerContribution).Sum(l => l.Amount);
            return Row(("Employee", Name(employees, g.Key)), ("Department", employees.GetValueOrDefault(g.Key)?.Department ?? "—"), ("Months", months), ("Gross", Money(gross)), ("Employer", Money(employer)),
                ("Annualised", Money(Math.Round((gross + employer) / months * 12m, 2, MidpointRounding.AwayFromZero))), ("Cost", gross + employer));
        })];
    }

    private Task<Dictionary<string, string>> AccountNamesAsync(long companyId, CancellationToken ct)
        => _db.Accounts.AsNoTracking().IgnoreQueryFilters([Persistence.One4AllDbContext.SoftDeleteFilter]).Where(a => a.CompanyId == companyId).ToDictionaryAsync(a => a.AccountCode, a => a.Name, ct);

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
