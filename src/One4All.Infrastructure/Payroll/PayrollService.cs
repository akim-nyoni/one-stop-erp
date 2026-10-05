using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Payroll;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Payroll;
using One4All.Core.Common;
using One4All.Core.Payroll;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using One4All.Reporting.Expressions;

namespace One4All.Infrastructure.Payroll;

/// <summary>
/// Payroll (brief §4.15.6). Resolves each configured pay element in sequence — fixed, percentage,
/// rate-per-unit or a formula evaluated by the shared report expression engine (brief §13.1) — to build a
/// payslip, and commits a run as one balanced GL journal (Dr earning expenses, Cr deduction/statutory
/// liabilities, Cr net pay) through the central engine.
/// </summary>
public sealed partial class PayrollService : IPayrollService, IPayrollAdjustmentService
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _posting;
    private readonly ISalesLedgerService _sales;
    private readonly ReportExpressionEngine _expressions = new();

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="posting">The central posting engine.</param>
    /// <param name="sales">The sales ledger (rent deducted from pay is receipted to the tenancy's account).</param>
    public PayrollService(One4AllDbContext db, IPostingEngine posting, ISalesLedgerService sales)
    {
        _sales = sales;
        _db = db;
        _posting = posting;
    }

    /// <inheritdoc />
    public async Task<Result<PayCalculation>> CalculateAsync(long companyId, long employeeId, CancellationToken ct = default)
    {
        Employee? employee = await _db.Set<Employee>().Include(e => e.PayElements)
            .FirstOrDefaultAsync(e => e.Id == employeeId && e.CompanyId == companyId, ct).ConfigureAwait(false);
        if (employee is null)
        {
            return Error.NotFound("Payroll.EmployeeNotFound", $"Employee {employeeId} not found.");
        }

        Dictionary<long, PayElement> elements = await LoadElementsAsync(companyId, ct).ConfigureAwait(false);
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        Dictionary<long, Dictionary<string, object?>> recoveries = await RecoveryVariablesAsync(companyId, [employee], elements, today, ct).ConfigureAwait(false);
        Result<Payslip> slip = BuildPayslip(companyId, employee, elements, "PREVIEW", today, recoveries: recoveries[employee.Id]);
        if (slip.IsFailure)
        {
            return slip.Error;
        }

        IReadOnlyList<PayLine> lines = slip.Value.Lines.Select(l => new PayLine(l.ElementCode, l.Amount)).ToList();
        return new PayCalculation(employee.Id, slip.Value.GrossEarnings, slip.Value.TotalDeductions, slip.Value.NetPay, lines);
    }

    /// <inheritdoc />
    public async Task<Result<string>> RenderPayslipTextAsync(long companyId, long employeeId, string periodLabel, DateOnly payDate, int width = PayslipFormatter.DefaultWidth, CancellationToken ct = default)
    {
        Employee? employee = await _db.Set<Employee>().Include(e => e.PayElements)
            .FirstOrDefaultAsync(e => e.Id == employeeId && e.CompanyId == companyId, ct).ConfigureAwait(false);
        if (employee is null)
        {
            return Error.NotFound("Payroll.EmployeeNotFound", $"Employee {employeeId} not found.");
        }

        Dictionary<long, PayElement> elements = await LoadElementsAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<long, Dictionary<string, object?>> recoveries = await RecoveryVariablesAsync(companyId, [employee], elements, payDate, ct).ConfigureAwait(false);
        Result<Payslip> slip = BuildPayslip(companyId, employee, elements, periodLabel, payDate, recoveries: recoveries[employee.Id]);
        if (slip.IsFailure)
        {
            return slip.Error;
        }

        string companyName = await _db.Set<Company>().Where(c => c.Id == companyId).Select(c => c.Name).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? string.Empty;
        Dictionary<string, string> names = elements.Values.GroupBy(e => e.Code).ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);
        string Label(string code) => names.TryGetValue(code, out string? name) ? name : code;

        PayslipDocument document = new()
        {
            CompanyName = companyName,
            EmployeeName = employee.FullName,
            EmployeeNumber = employee.EmployeeNumber,
            PeriodLabel = periodLabel,
            PayDate = payDate,
            Earnings = slip.Value.Lines.Where(l => l.Type == PayElementType.Earning).Select(l => new PayslipItem(Label(l.ElementCode), l.Amount)).ToList(),
            Deductions = slip.Value.Lines.Where(l => l.Type == PayElementType.Deduction).Select(l => new PayslipItem(Label(l.ElementCode), l.Amount)).ToList(),
            GrossEarnings = slip.Value.GrossEarnings,
            TotalDeductions = slip.Value.TotalDeductions,
            NetPay = slip.Value.NetPay,
        };

        // Every render is logged; the second and later of the same payslip are reprints (PR69).
        _db.PayslipPrints.Add(new PayslipPrint(companyId, employeeId, periodLabel, payDate));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return PayslipFormatter.ToPlainText(document, width);
    }

    /// <inheritdoc />
    public Task<Result<PayrollRunResult>> RunAsync(long companyId, string periodLabel, DateOnly payDate, string netPayAccountCode, string documentNumber, CancellationToken ct = default)
        => _db.AtomicAsync(() => RunCoreAsync(companyId, periodLabel, payDate, netPayAccountCode, documentNumber, ct), ct);

    // One unit of work: its postings and record changes commit together or not at all.
    private async Task<Result<PayrollRunResult>> RunCoreAsync(long companyId, string periodLabel, DateOnly payDate, string netPayAccountCode, string documentNumber, CancellationToken ct)
    {
        Company? company = await _db.Set<Company>().FirstOrDefaultAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        if (company is null)
        {
            return Error.NotFound("Payroll.CompanyNotFound", $"Company {companyId} not found.");
        }

        periodLabel = (periodLabel ?? string.Empty).Trim();
        if (periodLabel.Length == 0)
        {
            return Error.Validation("Payroll.PeriodRequired", "A payroll run needs a period label (e.g. 2026-01).");
        }

        // A period is paid once: a second run would pay every employee again (unless the first was formally reversed).
        if (await RegularRunIsLiveAsync(companyId, periodLabel, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Payroll.PeriodAlreadyRun", $"Payroll for period {periodLabel} has already been run.");
        }

        Dictionary<long, PayElement> elements = await LoadElementsAsync(companyId, ct).ConfigureAwait(false);
        List<Employee> employees = await _db.Set<Employee>().Include(e => e.PayElements)
            .Where(e => e.CompanyId == companyId && e.Status == EmployeeStatus.Active)
            .OrderBy(e => e.EmployeeNumber)
            .ToListAsync(ct).ConfigureAwait(false);

        if (employees.Count == 0)
        {
            return Error.Rule("Payroll.NoEmployees", "There are no active employees to pay.");
        }

        Dictionary<long, Dictionary<string, object?>> recoveries = await RecoveryVariablesAsync(companyId, employees, elements, payDate, ct).ConfigureAwait(false);
        List<Payslip> slips = [];
        foreach (Employee employee in employees)
        {
            Result<Payslip> slip = BuildPayslip(companyId, employee, elements, periodLabel, payDate, documentNumber: documentNumber, recoveries: recoveries[employee.Id]);
            if (slip.IsFailure)
            {
                return slip.Error;
            }

            slips.Add(slip.Value);
        }

        return await PostRunAsync(company, periodLabel, payDate, netPayAccountCode, documentNumber, slips, elements, PayRunKind.Regular, null, ct).ConfigureAwait(false);
    }

    // Whether the period has a regular run that has not been reversed. Runs from before the run register are known by their payslips.
    private async Task<bool> RegularRunIsLiveAsync(long companyId, string periodLabel, CancellationToken ct)
    {
        List<PayrollRunRecord> runs = await _db.Set<PayrollRunRecord>().Where(r => r.CompanyId == companyId && r.PeriodLabel == periodLabel && r.Kind == PayRunKind.Regular)
            .ToListAsync(ct).ConfigureAwait(false);
        return runs.Count > 0
            ? runs.Exists(r => !r.IsReversed)
            : await _db.Set<Payslip>().AnyAsync(p => p.CompanyId == companyId && p.PeriodLabel == periodLabel && p.RunKind == PayRunKind.Regular, ct).ConfigureAwait(false);
    }

    // Saves a run's payslips, posts its one balanced journal (Dr earnings and employer costs, Cr deductions, scheme liabilities and
    // net pay; a negative amount swaps its side) and records the run in the register.
    private async Task<Result<PayrollRunResult>> PostRunAsync(Company company, string periodLabel, DateOnly payDate, string netPayAccountCode, string documentNumber, IReadOnlyList<Payslip> slips,
        Dictionary<long, PayElement> elements, PayRunKind kind, string? reason, CancellationToken ct)
    {
        Dictionary<string, decimal> debitsByAccount = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, decimal> creditsByAccount = new(StringComparer.OrdinalIgnoreCase);
        decimal totalGross = 0m;
        decimal totalDeductions = 0m;
        decimal totalNet = 0m;

        foreach (Payslip payslip in slips)
        {
            _db.Set<Payslip>().Add(payslip);

            foreach (PayslipLine line in payslip.Lines)
            {
                if (line.Amount == 0m)
                {
                    continue;
                }

                PayElement element = elements.Values.First(e => e.Code == line.ElementCode);
                if (line.Type == PayElementType.Earning)
                {
                    Add(debitsByAccount, element.AccountCode, line.Amount);
                }
                else if (line.Type == PayElementType.Deduction)
                {
                    Add(creditsByAccount, element.AccountCode, line.Amount);
                }
                else if (line.Type == PayElementType.EmployerContribution)
                {
                    // The employer's cost: Dr its expense account, Cr the liability to the scheme.
                    if (string.IsNullOrWhiteSpace(element.ContributionExpenseAccountCode))
                    {
                        return Error.Validation("Payroll.ContributionAccount", $"Employer contribution {element.Code} needs an expense account before it can be posted.");
                    }

                    Add(debitsByAccount, element.ContributionExpenseAccountCode, line.Amount);
                    Add(creditsByAccount, element.AccountCode, line.Amount);
                }
            }

            totalGross += payslip.GrossEarnings;
            totalDeductions += payslip.TotalDeductions;
            totalNet += payslip.NetPay;
        }

        Add(creditsByAccount, netPayAccountCode, totalNet);

        static JournalSide Flip(JournalSide side) => side == JournalSide.Debit ? JournalSide.Credit : JournalSide.Debit;
        List<JournalLineRequest> lines = [.. debitsByAccount.Select(kv => (kv, JournalSide.Debit)).Concat(creditsByAccount.Select(kv => (kv, JournalSide.Credit)))
            .Where(x => x.kv.Value != 0m)
            .Select(x => new JournalLineRequest { AccountCode = x.kv.Key, Side = x.kv.Value > 0m ? x.Item2 : Flip(x.Item2), TransactionAmount = Math.Abs(x.kv.Value) })];

        Result<PostingReceipt> posted = await _posting.PostAsync(new JournalRequest
        {
            CompanyId = company.Id,
            SourceModule = "GL",
            PostingDate = payDate,
            TransactionCurrency = company.BaseCurrency,
            DocumentType = "PAYROLL",
            DocumentNumber = documentNumber,
            Narrative = kind == PayRunKind.Regular ? $"Payroll {periodLabel}" : $"Payroll {periodLabel} {kind.ToString().ToLowerInvariant()}: {reason}",
            Lines = lines,
        }, ct).ConfigureAwait(false);

        if (posted.IsFailure)
        {
            return posted.Error;
        }

        Result rent = await SettleHousingRentAsync(company, slips, elements, documentNumber, payDate, ct).ConfigureAwait(false);
        if (rent.IsFailure)
        {
            return rent.Error;
        }

        _db.Set<PayrollRunRecord>().Add(new PayrollRunRecord(company.Id, periodLabel, payDate, slips.Count, totalGross, totalDeductions, totalNet, posted.Value.JournalNumber, kind, documentNumber, reason));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new PayrollRunResult(slips.Count, totalGross, totalDeductions, totalNet, posted.Value.JournalNumber);
    }

    private async Task<Dictionary<long, PayElement>> LoadElementsAsync(long companyId, CancellationToken ct) =>
        await _db.Set<PayElement>().Where(e => e.CompanyId == companyId)
            .ToDictionaryAsync(e => e.Id, ct).ConfigureAwait(false);

    private Result<Payslip> BuildPayslip(long companyId, Employee employee, IReadOnlyDictionary<long, PayElement> elements, string periodLabel, DateOnly payDate,
        decimal? basicSalary = null, string documentNumber = "", IReadOnlyDictionary<string, object?>? recoveries = null)
    {
        Payslip payslip = new(companyId, employee.Id, periodLabel, payDate, PayRunKind.Regular, documentNumber);

        // Variables the formulas evaluate against; updated as each element resolves.
        Dictionary<string, object?> vars = new(StringComparer.Ordinal)
        {
            ["BASIC"] = basicSalary ?? employee.BasicSalary,
            ["GROSS"] = 0m,
            ["TAXABLE"] = 0m,
            ["DEDUCTIONS"] = 0m,
            ["NET"] = 0m,
            [StaffPurchasesVariable] = 0m,
            [HousingRentVariable] = 0m,
        };
        foreach ((string name, object? value) in recoveries ?? new Dictionary<string, object?>())
        {
            vars[name] = value;
        }

        IEnumerable<(PayElement Element, EmployeePayElement Assignment)> ordered = employee.PayElements
            .Where(a => a.IsActive && elements.ContainsKey(a.PayElementId))
            .Select(a => (elements[a.PayElementId], a))
            .OrderBy(x => x.Item1.Sequence);

        decimal gross = 0m;
        decimal taxable = 0m;
        decimal deductions = 0m;

        foreach ((PayElement element, EmployeePayElement assignment) in ordered)
        {
            Result<decimal> resolved = Resolve(element, assignment, vars);
            if (resolved.IsFailure)
            {
                return resolved.Error;
            }

            decimal amount = decimal.Round(resolved.Value, 2, MidpointRounding.ToEven);
            vars[element.Code] = amount;

            switch (element.Type)
            {
                case PayElementType.Earning:
                    gross += amount;
                    if (element.Taxable)
                    {
                        taxable += amount;
                    }

                    break;
                case PayElementType.Deduction:
                    deductions += amount;
                    break;
            }

            vars["GROSS"] = gross;
            vars["TAXABLE"] = taxable;
            vars["DEDUCTIONS"] = deductions;
            payslip.AddLine(element.Code, element.Name, element.Type, amount, element.Taxable);
        }

        return payslip;
    }

    private Result<decimal> Resolve(PayElement element, EmployeePayElement assignment, IReadOnlyDictionary<string, object?> vars)
    {
        decimal basic = (decimal)(vars["BASIC"] ?? 0m);
        decimal gross = (decimal)(vars["GROSS"] ?? 0m);
        decimal rate = assignment.OverrideRate ?? element.Rate;

        switch (element.Basis)
        {
            case CalculationBasis.FixedAmount:
                return assignment.OverrideAmount ?? element.FixedAmount;
            case CalculationBasis.PercentOfBasic:
                return basic * rate / 100m;
            case CalculationBasis.PercentOfGross:
                return gross * rate / 100m;
            case CalculationBasis.RatePerUnit:
                return rate * (assignment.Units ?? 0m);
            case CalculationBasis.Formula:
                if (string.IsNullOrWhiteSpace(element.Formula))
                {
                    return Error.Validation("Payroll.FormulaMissing", $"Element {element.Code} has no formula.");
                }

                try
                {
                    object? value = _expressions.Evaluate(element.Formula, new ReportExpressionContext { Fields = vars });
                    return value is null ? 0m : Convert.ToDecimal(value);
                }
                catch (Exception ex)
                {
                    return Error.Validation("Payroll.FormulaError", $"Element {element.Code} formula failed: {ex.Message}");
                }

            default:
                return 0m;
        }
    }

    private static void Add(Dictionary<string, decimal> map, string account, decimal amount) =>
        map[account] = map.GetValueOrDefault(account) + amount;
}
