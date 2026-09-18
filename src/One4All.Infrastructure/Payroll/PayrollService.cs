using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Payroll;
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
public sealed class PayrollService : IPayrollService
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _posting;
    private readonly ReportExpressionEngine _expressions = new();

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="posting">The central posting engine.</param>
    public PayrollService(One4AllDbContext db, IPostingEngine posting)
    {
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
        Result<Payslip> slip = BuildPayslip(companyId, employee, elements, "PREVIEW", DateOnly.FromDateTime(DateTime.UtcNow));
        if (slip.IsFailure)
        {
            return slip.Error;
        }

        IReadOnlyList<PayLine> lines = slip.Value.Lines.Select(l => new PayLine(l.ElementCode, l.Amount)).ToList();
        return new PayCalculation(employee.Id, slip.Value.GrossEarnings, slip.Value.TotalDeductions, slip.Value.NetPay, lines);
    }

    /// <inheritdoc />
    public async Task<Result<PayrollRunResult>> RunAsync(long companyId, string periodLabel, DateOnly payDate, string netPayAccountCode, string documentNumber, CancellationToken ct = default)
    {
        Company? company = await _db.Set<Company>().FirstOrDefaultAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        if (company is null)
        {
            return Error.NotFound("Payroll.CompanyNotFound", $"Company {companyId} not found.");
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

        // Aggregate GL effects across payslips so one balanced journal represents the run.
        Dictionary<string, decimal> debitsByAccount = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, decimal> creditsByAccount = new(StringComparer.OrdinalIgnoreCase);
        decimal totalGross = 0m;
        decimal totalDeductions = 0m;
        decimal totalNet = 0m;

        foreach (Employee employee in employees)
        {
            Result<Payslip> slip = BuildPayslip(companyId, employee, elements, periodLabel, payDate);
            if (slip.IsFailure)
            {
                return slip.Error;
            }

            Payslip payslip = slip.Value;
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
            }

            totalGross += payslip.GrossEarnings;
            totalDeductions += payslip.TotalDeductions;
            totalNet += payslip.NetPay;
        }

        Add(creditsByAccount, netPayAccountCode, totalNet);

        List<JournalLineRequest> lines = [];
        lines.AddRange(debitsByAccount.Select(kv => new JournalLineRequest { AccountCode = kv.Key, Side = JournalSide.Debit, TransactionAmount = kv.Value }));
        lines.AddRange(creditsByAccount.Select(kv => new JournalLineRequest { AccountCode = kv.Key, Side = JournalSide.Credit, TransactionAmount = kv.Value }));

        Result<PostingReceipt> posted = await _posting.PostAsync(new JournalRequest
        {
            CompanyId = companyId,
            SourceModule = "GL",
            PostingDate = payDate,
            TransactionCurrency = company.BaseCurrency,
            DocumentType = "PAYROLL",
            DocumentNumber = documentNumber,
            Narrative = $"Payroll {periodLabel}",
            Lines = lines,
        }, ct).ConfigureAwait(false);

        if (posted.IsFailure)
        {
            return posted.Error;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new PayrollRunResult(employees.Count, totalGross, totalDeductions, totalNet, posted.Value.JournalNumber);
    }

    private async Task<Dictionary<long, PayElement>> LoadElementsAsync(long companyId, CancellationToken ct) =>
        await _db.Set<PayElement>().Where(e => e.CompanyId == companyId)
            .ToDictionaryAsync(e => e.Id, ct).ConfigureAwait(false);

    private Result<Payslip> BuildPayslip(long companyId, Employee employee, IReadOnlyDictionary<long, PayElement> elements, string periodLabel, DateOnly payDate)
    {
        Payslip payslip = new(companyId, employee.Id, periodLabel, payDate);

        // Variables the formulas evaluate against; updated as each element resolves.
        Dictionary<string, object?> vars = new(StringComparer.Ordinal)
        {
            ["BASIC"] = employee.BasicSalary,
            ["GROSS"] = 0m,
            ["TAXABLE"] = 0m,
            ["DEDUCTIONS"] = 0m,
            ["NET"] = 0m,
        };

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
