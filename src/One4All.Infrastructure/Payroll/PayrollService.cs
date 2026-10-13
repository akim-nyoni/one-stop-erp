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
        Dictionary<(string Account, string CostCentre, string Department, string? Project, string? Fund, string? Branch), decimal> debitsByAccount = [];
        Dictionary<string, decimal> creditsByAccount = new(StringComparer.OrdinalIgnoreCase);
        List<long> employeeIds = [.. slips.Select(s => s.EmployeeId).Distinct()];
        Dictionary<long, (string? Department, string? CostCentre, string? Project, string? Fund, long? BranchId)> staff = await _db.Set<Employee>()
            .IgnoreQueryFilters([Persistence.One4AllDbContext.SoftDeleteFilter]).Where(e => employeeIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => (e.Department, e.CostCentre, e.Project, e.Fund, e.BranchId), ct).ConfigureAwait(false);
        Dictionary<long, string> branchCodes = await _db.Branches.Where(b => b.CompanyId == company.Id).ToDictionaryAsync(b => b.Id, b => b.Code, ct).ConfigureAwait(false);
        var departments = await _db.Departments.Where(d => d.CompanyId == company.Id).Select(d => new { d.Code, d.Name }).ToListAsync(ct).ConfigureAwait(false);
        var costCentres = await _db.CostCentres.Where(c => c.CompanyId == company.Id).Select(c => new { c.Code, c.Name }).ToListAsync(ct).ConfigureAwait(false);
        string Dimension(string? value, IEnumerable<(string Code, string Name)> master)
            => value is null ? string.Empty
                : master.FirstOrDefault(m => string.Equals(m.Code, value.Trim(), StringComparison.OrdinalIgnoreCase) || string.Equals(m.Name, value.Trim(), StringComparison.OrdinalIgnoreCase)).Code ?? string.Empty;
        decimal totalGross = 0m;
        decimal totalDeductions = 0m;
        decimal totalNet = 0m;

        List<long> loanIds = [.. slips.SelectMany(s => s.Lines).Select(l => LoanIdOf(l.ElementCode)).OfType<long>().Distinct()];
        Dictionary<long, EmployeeLoan> loansById = loanIds.Count == 0 ? [] : await _db.EmployeeLoans.Where(l => loanIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct).ConfigureAwait(false);
        foreach (Payslip payslip in slips)
        {
            _db.Set<Payslip>().Add(payslip);
            (string? department, string? costCentre, string? project, string? fund, long? branchId) = staff.GetValueOrDefault(payslip.EmployeeId);
            string dept = Dimension(department, departments.Select(d => (d.Code, d.Name)));
            string cc = Dimension(costCentre, costCentres.Select(c => (c.Code, c.Name)));
            string? branch = branchId is long b ? branchCodes.GetValueOrDefault(b) : null;

            foreach (PayslipLine line in payslip.Lines)
            {
                if (line.Amount != 0m && LoanIdOf(line.ElementCode) is long loanId && loansById.TryGetValue(loanId, out EmployeeLoan? loan))
                {
                    if (line.ElementCode.StartsWith(LoanInterestLinePrefix, StringComparison.Ordinal))
                    {
                        // Interest charged: Dr the loan, Cr interest income.
                        if (string.IsNullOrWhiteSpace(company.StaffLoanInterestAccountCode))
                        {
                            return Error.Validation("Payroll.LoanInterestAccount", "Set the staff loan interest income account before charging interest on staff loans.");
                        }

                        debitsByAccount[(loan.LoanReceivableAccountCode, string.Empty, string.Empty, null, null, null)] =
                            debitsByAccount.GetValueOrDefault((loan.LoanReceivableAccountCode, string.Empty, string.Empty, null, null, null)) + line.Amount;
                        Add(creditsByAccount, company.StaffLoanInterestAccountCode, line.Amount);
                        loan.ChargeInterest(line.Amount);
                    }
                    else
                    {
                        // The repayment out of pay: Cr the loan.
                        Add(creditsByAccount, loan.LoanReceivableAccountCode, line.Amount);
                        loan.Repay(line.Amount);
                    }

                    continue;
                }

                // Memo lines (benefits in kind, notes) are neither paid nor posted.
                if (line.Amount == 0m || line.Type == PayElementType.Memo)
                {
                    continue;
                }

                PayElement element = elements.Values.First(e => e.Code == line.ElementCode);
                if (line.Type == PayElementType.Earning)
                {
                    string expense = element.AccountFor(dept, element.AccountCode);
                    debitsByAccount[(expense, cc, dept, project, fund, branch)] = debitsByAccount.GetValueOrDefault((expense, cc, dept, project, fund, branch)) + line.Amount;
                }
                else if (line.Type == PayElementType.Deduction)
                {
                    Add(creditsByAccount, element.AccountCode, line.Amount);
                }
                else if (line.Type is PayElementType.EmployerContribution or PayElementType.Provision)
                {
                    // The employer's cost: Dr its expense account, Cr the liability to the scheme.
                    if (string.IsNullOrWhiteSpace(element.ContributionExpenseAccountCode))
                    {
                        return Error.Validation("Payroll.ContributionAccount", $"Employer contribution {element.Code} needs an expense account before it can be posted.");
                    }

                    string cost = element.AccountFor(dept, element.ContributionExpenseAccountCode);
                    debitsByAccount[(cost, cc, dept, project, fund, branch)] = debitsByAccount.GetValueOrDefault((cost, cc, dept, project, fund, branch)) + line.Amount;
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
                AccountCode = kv.Key.Account, CostCentre = kv.Key.CostCentre, Department = kv.Key.Department, Project = kv.Key.Project, Fund = kv.Key.Fund, Branch = kv.Key.Branch,
                Side = kv.Value > 0m ? JournalSide.Debit : JournalSide.Credit, TransactionAmount = Math.Abs(kv.Value),
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
            payGroup) { NetPayAccountCode = netPayAccountCode.Trim().ToUpperInvariant() });
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

        // Someone who joins or leaves during the period is paid basic for the days they were employed (brief §4.15.4); a given basic (back pay,
        // a final settlement) is used as it is.
        (int worked, int inPeriod) = PayPeriods.Days(employee.PayFrequency, payDate, employee.EngagementDate, employee.TerminationDate);
        decimal share = basicSalary is null && worked < inPeriod ? (decimal)worked / inPeriod : 1m;

        // Variables the formulas evaluate against; updated as each element resolves.
        Dictionary<string, object?> vars = new(StringComparer.Ordinal)
        {
            ["BASIC"] = basicSalary ?? decimal.Round(employee.BasicSalary * share, 2, MidpointRounding.AwayFromZero),
            [BasicNoteKey] = share < 1m ? $"basic {Money(employee.BasicSalary)} × {worked}/{inPeriod} days" : null,
            ["DAYSWORKED"] = (decimal)worked,
            ["DAYSINPERIOD"] = (decimal)inPeriod,
            ["PRORATA"] = share,
            [PeriodsPerYearVariable] = (decimal)PayPeriods.PerYear(employee.PayFrequency),
            [ActingAllowanceVariable] = 0m,
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

        // The bases flagged earnings build up (pensionable pay, the social security base…) and each element group's total.
        foreach (string basis in new[] { "PENSIONABLE", "SOCIALSECURITYBASE", "OVERTIMEBASE", "LEAVEBASE" })
        {
            vars[basis] = 0m;
        }

        foreach (string group in elements.Values.Select(e => e.Group).Concat(elements.Values.Select(e => e.BasisGroup)).OfType<string>().Where(g => g.Trim().Length > 0))
        {
            vars[GroupVariable(group)] = 0m;
        }

        // Benefits in kind are taxed but not paid: memo lines, valued monthly and scaled to the employee's pay period (brief §4.15.4).
        if (vars.GetValueOrDefault(BenefitsKey) is List<(string Code, string Name, decimal Monthly)> benefitsInKind)
        {
            decimal perPeriod = 12m / PayPeriods.PerYear(employee.PayFrequency);
            foreach ((string code, string name, decimal monthly) in benefitsInKind)
            {
                decimal value = decimal.Round(monthly * perPeriod, 2, MidpointRounding.AwayFromZero);
                taxable += value;
                taxableByCurrency[string.Empty] = taxableByCurrency.GetValueOrDefault(string.Empty) + value;
                payslip.AddLine(code, name, PayElementType.Memo, value, true,
                    perPeriod == 1m ? $"valued {Money(monthly)} a month" : $"valued {Money(monthly)} a month × 12/{PayPeriods.PerYear(employee.PayFrequency)}");
            }

            vars["BENEFITS"] = taxable;
            vars["TAXABLE"] = taxable;
        }

        foreach ((PayElement element, EmployeePayElement assigned1) in ordered)
        {
            void Accumulate(bool applies, string name)
            {
                if (applies)
                {
                    vars[name] = (vars.GetValueOrDefault(name) is decimal so_far ? so_far : 0m) + (decimal)vars[element.Code]!;
                }
            }

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

            decimal amount = element.ApplyLimits(resolved.Value);
            List<string> workings = [variable?.Amount is decimal keyed ? $"keyed for the period: {Money(keyed)}" : Workings(element, assignment, vars)];
            if (decimal.Round(resolved.Value, 2, MidpointRounding.ToEven) != amount)
            {
                workings.Add($"{Money(resolved.Value)} → {Money(amount)} by the element's floor / ceiling / rounding");
            }

            // Net-pay protection: arrears suspended earlier are attempted again; a cap holds a suspended deduction down.
            if (element.Type == PayElementType.Deduction && element.Recovers == PayRecovery.None && arrears?.GetValueOrDefault(element.Code) is decimal arrear and not 0m)
            {
                amount += arrear;
                workings.Add($"plus {Money(arrear)} arrears");
            }

            if (caps is not null && caps.TryGetValue(element.Code, out decimal cap) && cap < amount)
            {
                amount = cap;
                workings.Add($"held to {Money(cap)} to protect net pay");
            }

            vars[element.Code] = amount;

            switch (element.Type)
            {
                case PayElementType.Earning:
                    gross += amount;
                    Accumulate(element.Pensionable, "PENSIONABLE");
                    Accumulate(element.SubjectToSocialSecurity, "SOCIALSECURITYBASE");
                    Accumulate(element.OvertimeBase, "OVERTIMEBASE");
                    Accumulate(element.LeavePayBase, "LEAVEBASE");
                    Accumulate(!string.IsNullOrWhiteSpace(element.Group), GroupVariable(element.Group));
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
            payslip.AddLine(element.Code, element.Name, element.Type, amount, element.Taxable, string.Join("; ", workings));
        }

        // Staff loans (brief §4.15.5): the period's interest is charged (a memo line) and the instalment deducted, never taking net pay below zero.
        if (vars.GetValueOrDefault(LoansKey) is List<(long Id, string Reference, decimal Outstanding, decimal Installment, decimal Rate)> loans)
        {
            decimal available = Math.Max(0m, gross - deductions);
            foreach ((long id, string reference, decimal outstanding, decimal installment, decimal rate) in loans)
            {
                (decimal interest, decimal due) = EmployeeLoan.Instalment(outstanding, installment, rate, PayPeriods.PerYear(employee.PayFrequency));
                if (interest > 0m)
                {
                    payslip.AddLine(LoanInterestLinePrefix + id.ToString(System.Globalization.CultureInfo.InvariantCulture), $"Interest on loan {reference}", PayElementType.Memo, interest, false,
                        $"{rate:0.####}% a year on {Money(outstanding)} for one of {PayPeriods.PerYear(employee.PayFrequency)} periods");
                }

                decimal take = Math.Min(due, available);
                if (take > 0m)
                {
                    payslip.AddLine(LoanLinePrefix + id.ToString(System.Globalization.CultureInfo.InvariantCulture), $"Loan {reference} repayment", PayElementType.Deduction, take, false,
                        take < due ? $"instalment {Money(installment)} (due {Money(due)}) held to {Money(take)} of net pay" : $"instalment {Money(installment)} on a balance of {Money(outstanding + interest)}");
                    available -= take;
                }
            }
        }

        return payslip;
    }

    private const string BasicNoteKey = "__BASICNOTE";

    private static string Money(decimal value) => value.ToString("#,0.00", System.Globalization.CultureInfo.InvariantCulture);

    // How an element's amount follows from its rule and the values it used — kept on the payslip line as the run's own trace.
    private static string Workings(PayElement element, EmployeePayElement assignment, IReadOnlyDictionary<string, object?> vars)
    {
        decimal Number(string name) => vars.GetValueOrDefault(name) is decimal value ? value : 0m;
        decimal rate = assignment.OverrideRate ?? element.Rate;
        string basicNote = vars.GetValueOrDefault(BasicNoteKey) is string note ? $" ({note})" : string.Empty;
        string text = element.Basis switch
        {
            CalculationBasis.FixedAmount => assignment.OverrideAmount is decimal own ? $"the employee's amount {Money(own)}" : $"fixed {Money(element.FixedAmount)}",
            CalculationBasis.PercentOfBasic => $"{rate:0.####}% of basic {Money(Number("BASIC"))}{basicNote}",
            CalculationBasis.PercentOfGross => $"{rate:0.####}% of gross {Money(Number("GROSS"))}",
            CalculationBasis.RatePerUnit => $"{rate:0.####} × {assignment.Units ?? 0m:0.####} units",
            CalculationBasis.PercentOfGroup => $"{rate:0.####}% of group {element.BasisGroup} {Money(Number(GroupVariable(element.BasisGroup)))}",
            CalculationBasis.TaxTable => $"tax table {element.TaxTableCode ?? "in force"} on taxable {Money(Number("TAXABLE"))}"
                + (Number(PeriodsPerYearVariable) is var perYear && perYear is > 0m and not 12m ? $" (bands × 12/{perYear:0})" : string.Empty)
                + (element.CumulativeTax ? $", cumulative to period {Number(TaxPeriodVariable):0}" : string.Empty),
            _ => FormulaWorkings(element.Formula ?? string.Empty, vars) + (FormulaUses(element.Formula, "BASIC") ? basicNote : string.Empty),
        };
        return text;
    }

    // A formula with the value of every variable it names.
    private static string FormulaWorkings(string formula, IReadOnlyDictionary<string, object?> vars)
    {
        List<string> used = [.. System.Text.RegularExpressions.Regex.Matches(formula, "[A-Za-z_][A-Za-z0-9_]*").Select(m => m.Value).Distinct()
            .Where(name => vars.GetValueOrDefault(name) is decimal)
            .Select(name => $"{name} = {Money((decimal)vars[name]!)}")];
        return used.Count == 0 ? formula : $"{formula} with {string.Join(", ", used)}";
    }

    private static bool FormulaUses(string? formula, string name)
        => formula is not null && System.Text.RegularExpressions.Regex.IsMatch(formula, $"\\b{name}\\b");

    // The loan a payslip line repays (or was charged interest on), or null.
    private static long? LoanIdOf(string code)
    {
        string? digits = code.StartsWith(LoanInterestLinePrefix, StringComparison.Ordinal) ? code[LoanInterestLinePrefix.Length..]
            : code.StartsWith(LoanLinePrefix, StringComparison.Ordinal) ? code[LoanLinePrefix.Length..] : null;
        return long.TryParse(digits, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long id) ? id : null;
    }

    private static string GroupVariable(string? group) => "GROUP_" + new string([.. (group ?? string.Empty).Trim().ToUpperInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_')]);

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
            case CalculationBasis.PercentOfGroup:
                return (vars.GetValueOrDefault(GroupVariable(element.BasisGroup)) is decimal groupTotal ? groupTotal : 0m) * rate / 100m;
            case CalculationBasis.TableLookup:
                Result<IReadOnlyList<Application.Payroll.PayLookupBand>> table = Application.Payroll.PayLookupTable.Parse(element.LookupBands);
                return table.IsFailure ? table.Error
                    : Application.Payroll.PayLookupTable.Amount(table.Value, vars.GetValueOrDefault(element.LookupVariable ?? "BASIC") is decimal looked ? looked : 0m);
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

        // The tables' bands are monthly: a weekly or fortnightly employee's period takes them scaled to the period (12/52, 12/26).
        decimal periodsPerYear = Number(PeriodsPerYearVariable) is > 0m and var perYear ? perYear : 12m;
        decimal scale = 12m / periodsPerYear;

        // Tax credits come off the table's tax, never below nil.
        decimal Credited(decimal amount) => TaxCredits.Apply(amount, TaxCredits.Total(vars.GetValueOrDefault(TaxCreditsKey) as IEnumerable<TaxCreditRule> ?? [], Number, scale));
        IEnumerable<PayeBand> Bands(decimal times) => table.Bands.Select(b => new PayeBand(b.From * times, b.To * times, b.RatePercent));
        decimal taxable = Number("TAXABLE");
        if (element.CumulativeTax)
        {
            decimal period = Math.Max(1m, Number(TaxPeriodVariable));
            return Credited(PayeTable.Tax(Bands(period * scale), Number(YtdTaxableVariable) + taxable) - Number("YTD_" + element.Code));
        }

        decimal annual = Number("ANNUALTAXABLE");
        decimal regular = taxable - annual;
        decimal tax = PayeTable.Tax(Bands(scale), regular);
        tax = annual == 0m ? tax : tax + PayeTable.Tax(Bands(12m), (regular * periodsPerYear) + annual) - PayeTable.Tax(Bands(12m), regular * periodsPerYear);
        tax = Credited(tax);
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
