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
    private readonly IPurchaseLedgerService _purchases;
    private readonly ReportExpressionEngine _expressions = new();

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="posting">The central posting engine.</param>
    /// <param name="sales">The sales ledger (rent deducted from pay is receipted to the tenancy's account).</param>
    /// <param name="purchases">The purchase ledger (third-party remittances are raised as supplier invoices).</param>
    public PayrollService(One4AllDbContext db, IPostingEngine posting, ISalesLedgerService sales, IPurchaseLedgerService purchases)
    {
        _sales = sales;
        _purchases = purchases;
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
        Dictionary<long, Dictionary<string, decimal>> arrears = await ArrearsAsync(companyId, [employee], ct).ConfigureAwait(false);
        Company company = await _db.Set<Company>().FirstAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        Result<Payslip> slip = BuildProtected(company, employee, elements, "PREVIEW", today, recoveries[employee.Id], arrears[employee.Id]).Slip;
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
        Dictionary<long, Dictionary<string, decimal>> arrears = await ArrearsAsync(companyId, [employee], ct).ConfigureAwait(false);
        Company payer = await _db.Set<Company>().FirstAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        Dictionary<long, Dictionary<string, PayrollVariable>> variables = await VariablesAsync(companyId, periodLabel, [employee], ct).ConfigureAwait(false);
        Result<Payslip> slip = BuildProtected(payer, employee, elements, periodLabel, payDate, recoveries[employee.Id], arrears[employee.Id], variables: variables[employee.Id]).Slip;
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
        => RunGroupAsync(companyId, null, periodLabel, payDate, netPayAccountCode, documentNumber, ct);

    /// <inheritdoc />
    public Task<Result<PayrollRunResult>> RunGroupAsync(long companyId, string? payGroup, string periodLabel, DateOnly payDate, string netPayAccountCode, string documentNumber, CancellationToken ct = default)
        => _db.AtomicAsync(() => RunCoreAsync(companyId, PayGroupKey(payGroup), periodLabel, payDate, netPayAccountCode, documentNumber, ct), ct);

    private static string PayGroupKey(string? payGroup) => (payGroup ?? string.Empty).Trim().ToUpperInvariant();

    // One unit of work: its postings and record changes commit together or not at all.
    private async Task<Result<PayrollRunResult>> RunCoreAsync(long companyId, string payGroup, string periodLabel, DateOnly payDate, string netPayAccountCode, string documentNumber, CancellationToken ct)
    {
        Result<PreparedRun> prepared = await PrepareRunAsync(companyId, payGroup, periodLabel, payDate, documentNumber, ct).ConfigureAwait(false);
        if (prepared.IsFailure)
        {
            return prepared.Error;
        }

        // Where payroll needs approval, runs go through calculate → approve → commit instead.
        if (prepared.Value.Company.PayrollApprovalRequired)
        {
            return Error.Rule("Payroll.ApprovalRequired", "This company's payroll must be calculated, approved by someone else, then committed.");
        }

        return await CommitPreparedAsync(prepared.Value, payGroup, payDate, netPayAccountCode, documentNumber, ct).ConfigureAwait(false);
    }

    /// <summary>A run calculated but not yet saved or posted.</summary>
    /// <param name="Company">The company.</param>
    /// <param name="Period">The period label.</param>
    /// <param name="Slips">The payslips.</param>
    /// <param name="Arrears">The arrears movements.</param>
    /// <param name="Elements">The pay elements.</param>
    private sealed record PreparedRun(Company Company, string Period, List<Payslip> Slips, List<DeductionArrear> Arrears, Dictionary<long, PayElement> Elements);

    // Calculates a pay group's regular run for the period without saving anything: the checks, then every payslip under net-pay protection.
    private async Task<Result<PreparedRun>> PrepareRunAsync(long companyId, string payGroup, string periodLabel, DateOnly payDate, string documentNumber, CancellationToken ct)
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
        if (await RegularRunIsLiveAsync(companyId, periodLabel, payGroup, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Payroll.PeriodAlreadyRun", payGroup.Length == 0 ? $"Payroll for period {periodLabel} has already been run." : $"The {payGroup} payroll for period {periodLabel} has already been run.");
        }

        Dictionary<long, PayElement> elements = await LoadElementsAsync(companyId, ct).ConfigureAwait(false);
        List<Employee> employees = [.. (await _db.Set<Employee>().Include(e => e.PayElements)
            .Where(e => e.CompanyId == companyId && e.Status == EmployeeStatus.Active)
            .OrderBy(e => e.EmployeeNumber)
            .ToListAsync(ct).ConfigureAwait(false)).Where(e => PayGroupKey(e.PayGroup) == payGroup)];

        if (employees.Count == 0)
        {
            return Error.Rule("Payroll.NoEmployees", "There are no active employees to pay.");
        }

        Dictionary<long, Dictionary<string, object?>> recoveries = await RecoveryVariablesAsync(companyId, employees, elements, payDate, ct).ConfigureAwait(false);
        Dictionary<long, Dictionary<string, decimal>> arrears = await ArrearsAsync(companyId, employees, ct).ConfigureAwait(false);
        Dictionary<long, Dictionary<string, PayrollVariable>> variables = await VariablesAsync(companyId, periodLabel, employees, ct).ConfigureAwait(false);
        HashSet<string> recoveryCodes = new(elements.Values.Where(e => e.Recovers != PayRecovery.None).Select(e => e.Code), StringComparer.OrdinalIgnoreCase);
        List<Payslip> slips = [];
        List<DeductionArrear> arrearEntries = [];
        foreach (Employee employee in employees)
        {
            (Result<Payslip> slip, IReadOnlyList<DeductionOutcome> outcomes, decimal floor) = BuildProtected(company, employee, elements, periodLabel, payDate, recoveries[employee.Id], arrears[employee.Id],
                documentNumber, variables[employee.Id]);
            if (slip.IsFailure)
            {
                return slip.Error;
            }

            slips.Add(slip.Value);

            // Recoveries (staff purchases, rent) carry their own balances, so only ordinary deductions build up arrears.
            arrearEntries.AddRange(ArrearEntries(companyId, employee, periodLabel, documentNumber, payDate, [.. outcomes.Where(o => !recoveryCodes.Contains(o.Code))], arrears[employee.Id], floor));
        }

        return new PreparedRun(company, periodLabel, slips, arrearEntries, elements);
    }

    // Saves and posts a prepared run, with its arrears movements.
    private async Task<Result<PayrollRunResult>> CommitPreparedAsync(PreparedRun prepared, string payGroup, DateOnly payDate, string netPayAccountCode, string documentNumber, CancellationToken ct)
    {
        Result<PayrollRunResult> run = await PostRunAsync(prepared.Company, prepared.Period, payDate, netPayAccountCode, documentNumber, prepared.Slips, prepared.Elements, PayRunKind.Regular, null, payGroup, ct)
            .ConfigureAwait(false);
        if (run.IsSuccess && prepared.Arrears.Count > 0)
        {
            _db.DeductionArrears.AddRange(prepared.Arrears);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        // Third parties are invoiced with the run when the company asks for it (a run with nothing to remit is fine).
        if (run.IsSuccess && prepared.Company.RaisePayrollRemittances)
        {
            Result<IReadOnlyList<RemittanceRaised>> remitted = await RaiseRemittancesCoreAsync(prepared.Company.Id, documentNumber, ct).ConfigureAwait(false);
            if (remitted.IsFailure && remitted.Error.Code != "Payroll.NothingToRemit")
            {
                return remitted.Error;
            }
        }

        return run;
    }

    // Whether the period has a regular run that has not been reversed — for the pay group, or any group when none is given. Runs from
    // before the run register are known by their payslips.
    private async Task<bool> RegularRunIsLiveAsync(long companyId, string periodLabel, string? payGroup, CancellationToken ct)
    {
        List<PayrollRunRecord> runs = await _db.Set<PayrollRunRecord>()
            .Where(r => r.CompanyId == companyId && r.PeriodLabel == periodLabel && r.Kind == PayRunKind.Regular && (payGroup == null || r.PayGroup == payGroup))
            .ToListAsync(ct).ConfigureAwait(false);
        return runs.Count > 0
            ? runs.Exists(r => !r.IsReversed)
            : await _db.Set<Payslip>().AnyAsync(p => p.CompanyId == companyId && p.PeriodLabel == periodLabel && p.RunKind == PayRunKind.Regular && (payGroup == null || p.PayGroup == payGroup), ct)
                .ConfigureAwait(false);
    }

    // Saves a run's payslips, posts its one balanced journal (Dr earnings and employer costs, Cr deductions, scheme liabilities and
    // net pay; a negative amount swaps its side) and records the run in the register.
    private async Task<Result<PayrollRunResult>> PostRunAsync(Company company, string periodLabel, DateOnly payDate, string netPayAccountCode, string documentNumber, IReadOnlyList<Payslip> slips,
        Dictionary<long, PayElement> elements, PayRunKind kind, string? reason, string payGroup, CancellationToken ct)
    {
        // Costs are analysed by the employee's department and cost centre (§4.15.9), matched to the GL dimensions by code or name.
        Dictionary<(string Account, string CostCentre, string Department), decimal> debitsByAccount = [];
        Dictionary<string, decimal> creditsByAccount = new(StringComparer.OrdinalIgnoreCase);
        List<long> employeeIds = [.. slips.Select(s => s.EmployeeId).Distinct()];
        Dictionary<long, (string? Department, string? CostCentre)> staff = await _db.Set<Employee>().IgnoreQueryFilters([Persistence.One4AllDbContext.SoftDeleteFilter]).Where(e => employeeIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => (e.Department, e.CostCentre), ct).ConfigureAwait(false);
        var departments = await _db.Departments.Where(d => d.CompanyId == company.Id).Select(d => new { d.Code, d.Name }).ToListAsync(ct).ConfigureAwait(false);
        var costCentres = await _db.CostCentres.Where(c => c.CompanyId == company.Id).Select(c => new { c.Code, c.Name }).ToListAsync(ct).ConfigureAwait(false);
        string Dimension(string? value, IEnumerable<(string Code, string Name)> master)
            => value is null ? string.Empty
                : master.FirstOrDefault(m => string.Equals(m.Code, value.Trim(), StringComparison.OrdinalIgnoreCase) || string.Equals(m.Name, value.Trim(), StringComparison.OrdinalIgnoreCase)).Code ?? string.Empty;
        decimal totalGross = 0m;
        decimal totalDeductions = 0m;
        decimal totalNet = 0m;

        foreach (Payslip payslip in slips)
        {
            _db.Set<Payslip>().Add(payslip);
            (string? department, string? costCentre) = staff.GetValueOrDefault(payslip.EmployeeId);
            string dept = Dimension(department, departments.Select(d => (d.Code, d.Name)));
            string cc = Dimension(costCentre, costCentres.Select(c => (c.Code, c.Name)));

            foreach (PayslipLine line in payslip.Lines)
            {
                if (line.Amount == 0m)
                {
                    continue;
                }

                PayElement element = elements.Values.First(e => e.Code == line.ElementCode);
                if (line.Type == PayElementType.Earning)
                {
                    debitsByAccount[(element.AccountCode, cc, dept)] = debitsByAccount.GetValueOrDefault((element.AccountCode, cc, dept)) + line.Amount;
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

                    debitsByAccount[(element.ContributionExpenseAccountCode, cc, dept)] = debitsByAccount.GetValueOrDefault((element.ContributionExpenseAccountCode, cc, dept)) + line.Amount;
                    Add(creditsByAccount, element.AccountCode, line.Amount);
                }
            }

            totalGross += payslip.GrossEarnings;
            totalDeductions += payslip.TotalDeductions;
            totalNet += payslip.NetPay;
        }

        Add(creditsByAccount, netPayAccountCode, totalNet);

        List<JournalLineRequest> lines = [.. debitsByAccount.Where(kv => kv.Value != 0m).Select(kv => new JournalLineRequest
            {
                AccountCode = kv.Key.Account, CostCentre = kv.Key.CostCentre, Department = kv.Key.Department, Side = kv.Value > 0m ? JournalSide.Debit : JournalSide.Credit, TransactionAmount = Math.Abs(kv.Value),
            }),
            .. creditsByAccount.Where(kv => kv.Value != 0m)
                .Select(kv => new JournalLineRequest { AccountCode = kv.Key, Side = kv.Value > 0m ? JournalSide.Credit : JournalSide.Debit, TransactionAmount = Math.Abs(kv.Value) })];

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

        _db.Set<PayrollRunRecord>().Add(new PayrollRunRecord(company.Id, periodLabel, payDate, slips.Count, totalGross, totalDeductions, totalNet, posted.Value.JournalNumber, kind, documentNumber, reason,
            payGroup));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new PayrollRunResult(slips.Count, totalGross, totalDeductions, totalNet, posted.Value.JournalNumber);
    }

    private async Task<Dictionary<long, PayElement>> LoadElementsAsync(long companyId, CancellationToken ct) =>
        await _db.Set<PayElement>().Where(e => e.CompanyId == companyId)
            .ToDictionaryAsync(e => e.Id, ct).ConfigureAwait(false);

    private Result<Payslip> BuildPayslip(long companyId, Employee employee, IReadOnlyDictionary<long, PayElement> elements, string periodLabel, DateOnly payDate,
        decimal? basicSalary = null, string documentNumber = "", IReadOnlyDictionary<string, object?>? recoveries = null, IReadOnlyDictionary<string, decimal>? arrears = null,
        IReadOnlyDictionary<string, decimal>? caps = null, IReadOnlyDictionary<string, PayrollVariable>? variables = null)
    {
        Payslip payslip = new(companyId, employee.Id, periodLabel, payDate, PayRunKind.Regular, documentNumber, PayGroupKey(employee.PayGroup));

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

        List<(PayElement Element, EmployeePayElement Assignment)> assigned = [.. employee.PayElements
            .Where(a => a.IsActive && elements.ContainsKey(a.PayElementId))
            .Select(a => (elements[a.PayElementId], a))];

        // The period's variables: units or an amount for an assigned element, or a once-off element the employee does not normally have.
        foreach (PayrollVariable variable in variables?.Values ?? [])
        {
            if (!assigned.Exists(x => string.Equals(x.Element.Code, variable.ElementCode, StringComparison.OrdinalIgnoreCase))
                && elements.Values.FirstOrDefault(e => string.Equals(e.Code, variable.ElementCode, StringComparison.OrdinalIgnoreCase)) is PayElement onceOff)
            {
                assigned.Add((onceOff, new EmployeePayElement(onceOff.Id, null, null, null)));
            }
        }

        IEnumerable<(PayElement Element, EmployeePayElement Assignment)> ordered = assigned.OrderBy(x => x.Element.Sequence);

        decimal gross = 0m;
        decimal taxable = 0m;
        decimal annualTaxable = 0m;
        decimal deductions = 0m;
        vars["ANNUALTAXABLE"] = 0m;
        Dictionary<string, decimal> taxableByCurrency = new(StringComparer.Ordinal);
        vars[TaxableByCurrencyKey] = taxableByCurrency;

        foreach ((PayElement element, EmployeePayElement assigned1) in ordered)
        {
            EmployeePayElement assignment = assigned1;
            PayrollVariable? variable = variables?.GetValueOrDefault(element.Code);
            if (variable?.Units is decimal units)
            {
                assignment = new EmployeePayElement(element.Id, assignment.OverrideAmount, assignment.OverrideRate, units);
            }

            Result<decimal> resolved = variable?.Amount is decimal fixedAmount ? fixedAmount : Resolve(element, assignment, vars);
            if (resolved.IsFailure)
            {
                return resolved.Error;
            }

            decimal amount = decimal.Round(resolved.Value, 2, MidpointRounding.ToEven);

            // Net-pay protection: arrears suspended earlier are attempted again; a cap holds a suspended deduction down.
            if (element.Type == PayElementType.Deduction && element.Recovers == PayRecovery.None)
            {
                amount += arrears?.GetValueOrDefault(element.Code) ?? 0m;
            }

            if (caps is not null && caps.TryGetValue(element.Code, out decimal cap))
            {
                amount = Math.Min(amount, cap);
            }

            vars[element.Code] = amount;

            switch (element.Type)
            {
                case PayElementType.Earning:
                    gross += amount;
                    if (element.Taxable)
                    {
                        taxable += amount;
                        annualTaxable += element.IsAnnualPayment ? amount : 0m;
                        string currency = element.PaidInCurrency?.Value ?? string.Empty;
                        taxableByCurrency[currency] = taxableByCurrency.GetValueOrDefault(currency) + amount;
                    }

                    break;
                case PayElementType.Deduction:
                    deductions += amount;
                    break;
            }

            vars["GROSS"] = gross;
            vars["TAXABLE"] = taxable;
            vars["ANNUALTAXABLE"] = annualTaxable;
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
            case CalculationBasis.TaxTable:
                return TaxFromTable(element, vars);
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

    // §4.15.3: the table in force on the pay date; cumulative elements tax the year to date on the bands times the period number less the
    // tax already deducted; otherwise the period's regular taxable pay on the bands, with annual payments annualised and apportioned.
    private static Result<decimal> TaxFromTable(PayElement element, IReadOnlyDictionary<string, object?> vars)
    {
        DateOnly payDate = vars.GetValueOrDefault(PayDateKey) is DateOnly d ? d : DateOnly.FromDateTime(DateTime.UtcNow);
        TaxTable? table = (vars.GetValueOrDefault(TaxTablesKey) as IEnumerable<TaxTable> ?? [])
            .Where(t => t.Currency == null && t.EffectiveFrom <= payDate && (element.TaxTableCode == null || string.Equals(t.Code, element.TaxTableCode, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(t => t.EffectiveFrom).LastOrDefault();
        if (table is null)
        {
            return Error.Validation("Payroll.NoTaxTable", $"No tax table{(element.TaxTableCode is null ? string.Empty : " " + element.TaxTableCode)} is in force on {payDate:yyyy-MM-dd} for {element.Code}.");
        }

        decimal Number(string name) => vars.GetValueOrDefault(name) is decimal value ? value : 0m;
        IEnumerable<PayeBand> Bands(decimal times) => table.Bands.Select(b => new PayeBand(b.From * times, b.To * times, b.RatePercent));
        decimal taxable = Number("TAXABLE");
        if (element.CumulativeTax)
        {
            decimal period = Math.Max(1m, Number(TaxPeriodVariable));
            return PayeTable.Tax(Bands(period), Number(YtdTaxableVariable) + taxable) - Number("YTD_" + element.Code);
        }

        decimal annual = Number("ANNUALTAXABLE");
        decimal regular = taxable - annual;
        decimal tax = PayeTable.Tax(Bands(1m), regular);
        tax = annual == 0m ? tax : tax + PayeTable.Tax(Bands(12m), (regular * 12m) + annual) - PayeTable.Tax(Bands(12m), regular * 12m);
        return element.SplitCurrencyTax ? SplitByCurrency(element, vars, tax, taxable, payDate) : tax;
    }

    // Split-currency tax: each foreign currency's taxable earnings with a table of its own are taxed on it at the pay date's rate; everything else
    // bears its share of the base table's tax on the whole remuneration.
    private static Result<decimal> SplitByCurrency(PayElement element, IReadOnlyDictionary<string, object?> vars, decimal baseTax, decimal taxable, DateOnly payDate)
    {
        if (taxable == 0m || vars.GetValueOrDefault(TaxableByCurrencyKey) is not IReadOnlyDictionary<string, decimal> portions || portions.Keys.All(k => k.Length == 0))
        {
            return baseTax;
        }

        IEnumerable<TaxTable> tables = vars.GetValueOrDefault(TaxTablesKey) as IEnumerable<TaxTable> ?? [];
        IReadOnlyDictionary<string, decimal> rates = vars.GetValueOrDefault(PayRatesKey) as IReadOnlyDictionary<string, decimal> ?? new Dictionary<string, decimal>();
        decimal tax = 0m;
        foreach ((string currency, decimal earnings) in portions)
        {
            TaxTable? own = currency.Length == 0 ? null : tables
                .Where(t => t.Currency?.Value == currency && t.EffectiveFrom <= payDate && (element.TaxTableCode == null || string.Equals(t.Code, element.TaxTableCode, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(t => t.EffectiveFrom).LastOrDefault();
            if (own is null)
            {
                tax += baseTax * earnings / taxable;
                continue;
            }

            if (!rates.TryGetValue(currency, out decimal rate) || rate <= 0m)
            {
                return Error.Validation("Payroll.NoRate", $"No {currency} rate on {payDate:yyyy-MM-dd} to tax the earnings paid in it.");
            }

            decimal inCurrency = earnings / rate;
            tax += PayeTable.Tax(own.Bands.Select(b => new PayeBand(b.From, b.To, b.RatePercent)), inCurrency) * rate;
        }

        return decimal.Round(tax, 2, MidpointRounding.ToEven);
    }

    private static void Add(Dictionary<string, decimal> map, string account, decimal amount) =>
        map[account] = map.GetValueOrDefault(account) + amount;
}
