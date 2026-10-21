using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Payroll;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Payroll;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Payroll;

/// <summary>Payroll master data: employees, pay elements, tax tables, the standard element set and approved bank-detail changes.</summary>
public sealed class PayrollSetupService : IPayrollSetupService
{
    /// <summary>The bank and mobile-money details that change only through an approved request.</summary>
    public static readonly IReadOnlySet<string> BankFields = new HashSet<string>(StringComparer.Ordinal) { "BankName", "BankBranch", "BankAccountNumber", "MobileMoneyNumber" };

    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public PayrollSetupService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result<long>> CreateEmployeeAsync(long companyId, NewEmployee employee, CancellationToken ct = default)
    {
        string number = (employee.EmployeeNumber ?? string.Empty).Trim().ToUpperInvariant();
        if (number.Length == 0 || string.IsNullOrWhiteSpace(employee.FullName))
        {
            return Error.Validation("Payroll.EmployeeRequired", "An employee needs a number and a name.");
        }

        if (employee.BasicSalary < 0m)
        {
            return Error.Validation("Payroll.SalaryNegative", "The basic salary cannot be negative.");
        }

        if (Employee.HasEndDate(employee.EmploymentType) && employee.ContractEndDate is null)
        {
            return Error.Validation("Payroll.ContractEnd", $"A {employee.EmploymentType} employee needs the contract's end date.");
        }

        if (DimensionProblem(employee.Department, employee.CostCentre) is Error dimension)
        {
            return dimension;
        }

        List<string> numbers = await _db.Employees.Where(e => e.CompanyId == companyId).Select(e => e.EmployeeNumber).ToListAsync(ct).ConfigureAwait(false);
        if (numbers.Any(n => string.Equals(n, number, StringComparison.OrdinalIgnoreCase)))
        {
            return Error.Conflict("Payroll.EmployeeExists", $"Employee {number} already exists.");
        }

        Result<Placement> placement = await PlacementAsync(companyId, employee.Branch, employee.Project, employee.Fund, employee.ReportsTo, null, ct).ConfigureAwait(false);
        if (placement.IsFailure)
        {
            return placement.Error;
        }

        if (await EstablishmentService.PostProblemAsync(_db, companyId, employee.Department, employee.Grade, null, ct).ConfigureAwait(false) is Error post)
        {
            return post;
        }

        Employee created = new(companyId, number, employee.FullName.Trim(), employee.BasicSalary, employee.EngagementDate)
        {
            EmploymentType = employee.EmploymentType, Department = Clean(employee.Department), CostCentre = Clean(employee.CostCentre), PayGroup = Clean(employee.PayGroup),
            Grade = Clean(employee.Grade), Notch = employee.Notch, NationalIdNumber = Clean(employee.NationalIdNumber), TaxNumber = Clean(employee.TaxNumber), Gender = employee.Gender,
            DateOfBirth = employee.DateOfBirth, ContractEndDate = employee.ContractEndDate, ProbationEndDate = employee.ProbationEndDate, PaymentMethod = employee.PaymentMethod,
            BankName = Clean(employee.BankName), BankBranch = Clean(employee.BankBranch), BankAccountNumber = Clean(employee.BankAccountNumber),
            MobileMoneyNumber = Clean(employee.MobileMoneyNumber), Email = Clean(employee.Email), PayFrequency = employee.PayFrequency ?? PayFrequency.Monthly,
            BranchId = placement.Value.BranchId, Project = placement.Value.Project, Fund = placement.Value.Fund, ReportsToEmployeeId = placement.Value.ReportsToId,
            NextOfKinName = Clean(employee.NextOfKinName), NextOfKinPhone = Clean(employee.NextOfKinPhone), NextOfKinRelationship = Clean(employee.NextOfKinRelationship),
            MaritalStatus = employee.MaritalStatus ?? MaritalStatus.NotRecorded, Nationality = Clean(employee.Nationality), SocialSecurityNumber = Clean(employee.SocialSecurityNumber),
            UnionMembership = Clean(employee.UnionMembership),
        };
        _db.Employees.Add(created);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return created.Id;
    }

    /// <inheritdoc />
    public async Task<Result> UpdateEmployeeAsync(long companyId, string employeeNumber, EmployeeUpdate update, CancellationToken ct = default)
    {
        if (await EmployeeAsync(companyId, employeeNumber, ct).ConfigureAwait(false) is not Employee employee)
        {
            return Result.Failure(EmployeeNotFound(employeeNumber));
        }

        if (update.BasicSalary is < 0m)
        {
            return Result.Failure(Error.Validation("Payroll.SalaryNegative", "The basic salary cannot be negative."));
        }

        if (DimensionProblem(update.Department, update.CostCentre) is Error dimension)
        {
            return Result.Failure(dimension);
        }

        if (update.FullName is string name && name.Trim().Length > 0)
        {
            employee.FullName = name.Trim();
        }

        employee.BasicSalary = update.BasicSalary ?? employee.BasicSalary;
        employee.EmploymentType = update.EmploymentType ?? employee.EmploymentType;
        employee.Department = update.Department is null ? employee.Department : Clean(update.Department);
        employee.CostCentre = update.CostCentre is null ? employee.CostCentre : Clean(update.CostCentre);
        employee.PayGroup = update.PayGroup is null ? employee.PayGroup : Clean(update.PayGroup);
        employee.Grade = update.Grade is null ? employee.Grade : Clean(update.Grade);
        employee.Notch = update.Notch ?? employee.Notch;
        employee.NationalIdNumber = update.NationalIdNumber is null ? employee.NationalIdNumber : Clean(update.NationalIdNumber);
        employee.TaxNumber = update.TaxNumber is null ? employee.TaxNumber : Clean(update.TaxNumber);
        if (update.HousingTenancyNumber is string tenancyNumber)
        {
            if (Clean(tenancyNumber) is not string wanted)
            {
                employee.HousingTenancyId = null;
            }
            else
            {
                string number = wanted.ToUpperInvariant();
                long? tenancy = await _db.Set<Core.Housing.Tenancy>().Where(t => t.CompanyId == companyId && t.TenancyNumber == number).Select(t => (long?)t.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
                if (tenancy is null)
                {
                    return Result.Failure(Error.NotFound("Payroll.TenancyNotFound", $"No housing tenancy {number}."));
                }

                employee.HousingTenancyId = tenancy;
            }
        }

        employee.ContractEndDate = update.ContractEndDate ?? employee.ContractEndDate;
        employee.ProbationEndDate = update.ProbationEndDate ?? employee.ProbationEndDate;
        employee.WorkPermitExpiry = update.WorkPermitExpiry ?? employee.WorkPermitExpiry;
        employee.PaymentMethod = update.PaymentMethod ?? employee.PaymentMethod;
        employee.Email = update.Email is null ? employee.Email : Clean(update.Email);
        Result<Placement> placement = await PlacementAsync(companyId, update.Branch, update.Project, update.Fund, update.ReportsTo, employee.Id, ct).ConfigureAwait(false);
        if (placement.IsFailure)
        {
            return Result.Failure(placement.Error);
        }

        employee.PayFrequency = update.PayFrequency ?? employee.PayFrequency;
        employee.BranchId = update.Branch is null ? employee.BranchId : placement.Value.BranchId;
        employee.Project = update.Project is null ? employee.Project : placement.Value.Project;
        employee.Fund = update.Fund is null ? employee.Fund : placement.Value.Fund;
        employee.ReportsToEmployeeId = update.ReportsTo is null ? employee.ReportsToEmployeeId : placement.Value.ReportsToId;
        employee.NextOfKinName = update.NextOfKinName is null ? employee.NextOfKinName : Clean(update.NextOfKinName);
        employee.NextOfKinPhone = update.NextOfKinPhone is null ? employee.NextOfKinPhone : Clean(update.NextOfKinPhone);
        employee.NextOfKinRelationship = update.NextOfKinRelationship is null ? employee.NextOfKinRelationship : Clean(update.NextOfKinRelationship);
        employee.MaritalStatus = update.MaritalStatus ?? employee.MaritalStatus;
        employee.Nationality = update.Nationality is null ? employee.Nationality : Clean(update.Nationality);
        employee.SocialSecurityNumber = update.SocialSecurityNumber is null ? employee.SocialSecurityNumber : Clean(update.SocialSecurityNumber);
        employee.UnionMembership = update.UnionMembership is null ? employee.UnionMembership : Clean(update.UnionMembership);
        if (Employee.HasEndDate(employee.EmploymentType) && employee.ContractEndDate is null)
        {
            return Result.Failure(Error.Validation("Payroll.ContractEnd", $"A {employee.EmploymentType} employee needs the contract's end date."));
        }

        // A move to another department or grade needs a vacant approved post there.
        Microsoft.EntityFrameworkCore.ChangeTracking.PropertyValues original = _db.Entry(employee).OriginalValues;
        if ((!string.Equals(original.GetValue<string?>(nameof(Employee.Department)), employee.Department, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(original.GetValue<string?>(nameof(Employee.Grade)), employee.Grade, StringComparison.OrdinalIgnoreCase))
            && employee.Status == EmployeeStatus.Active
            && await EstablishmentService.PostProblemAsync(_db, companyId, employee.Department, employee.Grade, employee.Id, ct).ConfigureAwait(false) is Error post)
        {
            return Result.Failure(post);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    // Where the employee sits: their branch, project and fund (each the company's, and open), and whom they report to (another of the company's
    // staff, not themselves). A blank text clears the field.
    private sealed record Placement(long? BranchId, string? Project, string? Fund, long? ReportsToId);

    private async Task<Result<Placement>> PlacementAsync(long companyId, string? branch, string? project, string? fund, string? reportsTo, long? self, CancellationToken ct)
    {
        long? branchId = null;
        if (Clean(branch) is string branchCode)
        {
            string key = branchCode.ToUpperInvariant();
            branchId = await _db.Branches.Where(b => b.CompanyId == companyId && b.Code.ToUpper() == key).Select(b => (long?)b.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (branchId is null)
            {
                return Error.Validation("Payroll.BranchUnknown", $"Branch {branchCode} does not exist.");
            }
        }

        string? projectCode = Clean(project)?.ToUpperInvariant();
        if (projectCode is not null && !await _db.Projects.AnyAsync(p => p.CompanyId == companyId && p.Code == projectCode && p.IsActive, ct).ConfigureAwait(false))
        {
            return Error.Validation("Payroll.ProjectUnknown", $"Project {projectCode} does not exist or is closed.");
        }

        string? fundCode = Clean(fund)?.ToUpperInvariant();
        if (fundCode is not null && !await _db.Funds.AnyAsync(f => f.CompanyId == companyId && f.Code == fundCode && f.IsActive, ct).ConfigureAwait(false))
        {
            return Error.Validation("Payroll.FundUnknown", $"Fund {fundCode} does not exist or is inactive.");
        }

        long? managerId = null;
        if (Clean(reportsTo) is string managerNumber)
        {
            managerId = (await EmployeeAsync(companyId, managerNumber, ct).ConfigureAwait(false))?.Id;
            if (managerId is null)
            {
                return EmployeeNotFound(managerNumber);
            }

            if (managerId == self)
            {
                return Error.Validation("Payroll.ReportsToSelf", "An employee cannot report to themselves.");
            }
        }

        return new Placement(branchId, projectCode, fundCode, managerId);
    }

    /// <inheritdoc />
    public async Task<Result<long>> CreatePayElementAsync(long companyId, NewPayElement element, CancellationToken ct = default)
    {
        string code = (element.Code ?? string.Empty).Trim().ToUpperInvariant();
        if (await ElementProblemAsync(companyId, element, ct).ConfigureAwait(false) is Error problem)
        {
            return problem;
        }

        string account = element.AccountCode.Trim().ToUpperInvariant();
        if (await _db.PayElements.AnyAsync(p => p.CompanyId == companyId && p.Code == code, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Payroll.ElementExists", $"Pay element {code} already exists.");
        }

        PayElement created = new(companyId, code, element.Name.Trim(), element.Type, element.Basis, element.Sequence, account);
        Result applied = Configure(created, element);
        if (applied.IsFailure)
        {
            return applied.Error;
        }

        _db.PayElements.Add(created);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return created.Id;
    }

    /// <inheritdoc />
    public async Task<Result> EditPayElementAsync(long companyId, NewPayElement element, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(element);
        string code = (element.Code ?? string.Empty).Trim().ToUpperInvariant();
        PayElement? existing = await _db.PayElements.FirstOrDefaultAsync(p => p.CompanyId == companyId && p.Code == code, ct).ConfigureAwait(false);
        if (existing is null)
        {
            return Result.Failure(Error.NotFound("Payroll.ElementNotFound", $"Pay element {code} not found."));
        }

        if (existing.Type != element.Type)
        {
            return Result.Failure(Error.Rule("Payroll.ElementTypeFixed", $"{code} is {existing.Type}; an element's type cannot change - create a new element instead."));
        }

        if (await ElementProblemAsync(companyId, element, ct).ConfigureAwait(false) is Error problem)
        {
            return Result.Failure(problem);
        }

        existing.Name = element.Name.Trim();
        existing.Reconfigure(element.Basis, element.Sequence, element.AccountCode.Trim().ToUpperInvariant());
        Result applied = Configure(existing, element);
        if (applied.IsFailure)
        {
            return applied;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    // Everything but the code, type, name, basis, sequence and account, as given.
    private static Result Configure(PayElement target, NewPayElement element)
    {
        target.FixedAmount = element.FixedAmount;
        target.Rate = element.Rate;
        target.Formula = Clean(element.Formula);
        target.Taxable = element.Taxable;
        target.TaxTableCode = Clean(element.TaxTableCode)?.ToUpperInvariant();
        target.ContributionExpenseAccountCode = Clean(element.ContributionExpenseAccountCode)?.ToUpperInvariant();
        target.Pensionable = element.Pensionable;
        target.SubjectToSocialSecurity = element.SubjectToSocialSecurity;
        target.OvertimeBase = element.OvertimeBase;
        target.LeavePayBase = element.LeavePayBase;
        target.ProRated = element.ProRated;
        target.ArrearsRecoverable = element.ArrearsRecoverable;
        target.TerminalBenefit = element.TerminalBenefit;
        target.UnionDues = element.UnionDues;
        target.Group = Clean(element.Group)?.ToUpperInvariant();
        target.BasisGroup = Clean(element.BasisGroup)?.ToUpperInvariant();
        target.Floor = element.Floor;
        target.Ceiling = element.Ceiling;
        target.RoundTo = element.RoundTo;
        target.Rounding = element.Rounding;
        target.DepartmentAccounts = Clean(element.DepartmentAccounts);
        target.LookupVariable = Clean(element.LookupVariable)?.ToUpperInvariant();
        target.LookupBands = Clean(element.LookupBands);
        return Apply(target, new PayElementOptions(element.Recovers, element.BenefitCategory, element.PaidInCurrency, element.CumulativeTax, element.SplitCurrencyTax, element.IsAnnualPayment,
            element.AmountsInPaidCurrency));
    }

    // What a new or edited element must satisfy: a name and account, a formula for a formula element, the contribution's expense account, a readable lookup
    // table, the group a percent-of-group element takes, a floor not above the ceiling, and an account that exists.
    private async Task<Error?> ElementProblemAsync(long companyId, NewPayElement element, CancellationToken ct)
    {
        string code = (element.Code ?? string.Empty).Trim().ToUpperInvariant();
        if (code.Length == 0 || string.IsNullOrWhiteSpace(element.Name) || string.IsNullOrWhiteSpace(element.AccountCode))
        {
            return Error.Validation("Payroll.ElementRequired", "A pay element needs a code, a name and the account it posts to.");
        }

        if (element.Basis == CalculationBasis.Formula && string.IsNullOrWhiteSpace(element.Formula))
        {
            return Error.Validation("Payroll.FormulaRequired", "A formula element needs its formula.");
        }

        if (element.Type is PayElementType.EmployerContribution or PayElementType.Provision && string.IsNullOrWhiteSpace(element.ContributionExpenseAccountCode))
        {
            return Error.Validation("Payroll.ContributionAccount", "An employer contribution or provision needs the expense account it is charged to.");
        }

        if (element.Basis == CalculationBasis.TableLookup && Application.Payroll.PayLookupTable.Parse(element.LookupBands) is { IsFailure: true } badTable)
        {
            return badTable.Error;
        }

        if (element.Basis == CalculationBasis.PercentOfGroup && string.IsNullOrWhiteSpace(element.BasisGroup))
        {
            return Error.Validation("Payroll.BasisGroupRequired", "A percent-of-group element needs the group it is a percentage of.");
        }

        if (element.Floor is decimal floor && element.Ceiling is decimal ceiling && floor > ceiling)
        {
            return Error.Validation("Payroll.FloorAboveCeiling", "An element's floor cannot be above its ceiling.");
        }

        string account = element.AccountCode.Trim().ToUpperInvariant();
        if (!await _db.Accounts.AnyAsync(a => a.CompanyId == companyId && a.AccountCode == account, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Payroll.AccountNotFound", $"Account {account} does not exist.");
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<Result<decimal>> PreviewFormulaAsync(long companyId, string formula, string? employeeNumber, IReadOnlyDictionary<string, decimal> values, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (string.IsNullOrWhiteSpace(formula))
        {
            return Error.Validation("Payroll.FormulaRequired", "Give the formula to preview.");
        }

        Dictionary<string, object?> vars = new(StringComparer.OrdinalIgnoreCase) { ["BASIC"] = 0m, ["GROSS"] = 0m, ["TAXABLE"] = 0m };
        if (!string.IsNullOrWhiteSpace(employeeNumber))
        {
            string number = employeeNumber.Trim().ToUpperInvariant();
            decimal? basic = await _db.Employees.AsNoTracking().Where(e => e.CompanyId == companyId && e.EmployeeNumber == number).Select(e => (decimal?)e.BasicSalary).FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
            if (basic is null)
            {
                return Error.NotFound("Payroll.EmployeeNotFound", $"No employee {number}.");
            }

            vars["BASIC"] = vars["GROSS"] = vars["TAXABLE"] = basic.Value;
        }

        foreach ((string name, decimal value) in values)
        {
            vars[name.Trim().ToUpperInvariant()] = value;
        }

        try
        {
            object? value = new One4All.Reporting.Expressions.ReportExpressionEngine().Evaluate(formula.Trim(), new One4All.Reporting.Expressions.ReportExpressionContext { Fields = vars });
            return value is null ? 0m : Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Error.Validation("Payroll.FormulaError", $"The formula fails: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<Result> SetPayElementOptionsAsync(long companyId, string code, PayElementOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        string key = (code ?? string.Empty).Trim().ToUpperInvariant();
        PayElement? element = await _db.PayElements.FirstOrDefaultAsync(p => p.CompanyId == companyId && p.Code == key, ct).ConfigureAwait(false);
        if (element is null)
        {
            return Result.Failure(Error.NotFound("Payroll.ElementNotFound", $"Pay element {key} not found."));
        }

        Result applied = Apply(element, options);
        if (applied.IsFailure)
        {
            return applied;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    // The options as the element keeps them; a recovery or a benefit category only makes sense on the right element type.
    private static Result Apply(PayElement element, PayElementOptions options)
    {
        if (options.Recovers != PayRecovery.None && element.Type != PayElementType.Deduction)
        {
            return Result.Failure(Error.Validation("Payroll.RecoveryType", "Only a deduction recovers staff purchases or housing rent."));
        }

        if (options.PaidInCurrency is string given && !CurrencyCode.TryParse(given, out _))
        {
            return Result.Failure(Error.Validation("Payroll.PaidInCurrency", $"{given} is not a currency code."));
        }

        if (options.AmountsInPaidCurrency && string.IsNullOrWhiteSpace(options.PaidInCurrency))
        {
            return Result.Failure(Error.Validation("Payroll.AmountCurrency", "Amounts keyed in the paid-in currency need the currency the element is paid in."));
        }

        element.Recovers = options.Recovers;
        element.BenefitCategory = Clean(options.BenefitCategory);
        element.PaidInCurrency = Clean(options.PaidInCurrency) is string currency ? CurrencyCode.Parse(currency.ToUpperInvariant()) : null;
        element.CumulativeTax = options.CumulativeTax;
        element.SplitCurrencyTax = options.SplitCurrencyTax;
        element.IsAnnualPayment = options.IsAnnualPayment;
        element.AmountsInPaidCurrency = options.AmountsInPaidCurrency;
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> UpdatePayElementAsync(long companyId, string code, decimal? fixedAmount, decimal? rate, string? formula, bool? taxable, CancellationToken ct = default)
    {
        string key = (code ?? string.Empty).Trim().ToUpperInvariant();
        PayElement? element = await _db.PayElements.FirstOrDefaultAsync(p => p.CompanyId == companyId && p.Code == key, ct).ConfigureAwait(false);
        if (element is null)
        {
            return Result.Failure(Error.NotFound("Payroll.ElementNotFound", $"Pay element {key} not found."));
        }

        element.FixedAmount = fixedAmount ?? element.FixedAmount;
        element.Rate = rate ?? element.Rate;
        element.Formula = formula is null ? element.Formula : Clean(formula);
        element.Taxable = taxable ?? element.Taxable;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> AssignElementAsync(long companyId, string employeeNumber, string elementCode, decimal? overrideAmount, decimal? overrideRate, decimal? units, decimal? balance,
        CancellationToken ct = default)
    {
        if (await EmployeeAsync(companyId, employeeNumber, ct).ConfigureAwait(false) is not Employee employee)
        {
            return Result.Failure(EmployeeNotFound(employeeNumber));
        }

        string key = (elementCode ?? string.Empty).Trim().ToUpperInvariant();
        PayElement? element = await _db.PayElements.FirstOrDefaultAsync(p => p.CompanyId == companyId && p.Code == key, ct).ConfigureAwait(false);
        if (element is null)
        {
            return Result.Failure(Error.NotFound("Payroll.ElementNotFound", $"Pay element {key} not found."));
        }

        if (employee.PayElements.Any(p => p.PayElementId == element.Id && p.IsActive))
        {
            return Result.Failure(Error.Conflict("Payroll.ElementAssigned", $"{employee.EmployeeNumber} already has {key}; end it first to change it."));
        }

        if (element.Basis == CalculationBasis.BalanceDriven && balance is not > 0m)
        {
            return Result.Failure(Error.Validation("Payroll.BalanceRequired", $"{key} recovers a balance: give the balance to recover."));
        }

        employee.AssignPayElement(element.Id, overrideAmount, overrideRate, units, element.Basis == CalculationBasis.BalanceDriven ? balance : null);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> EndElementAsync(long companyId, string employeeNumber, string elementCode, CancellationToken ct = default)
    {
        if (await EmployeeAsync(companyId, employeeNumber, ct).ConfigureAwait(false) is not Employee employee)
        {
            return Result.Failure(EmployeeNotFound(employeeNumber));
        }

        string key = (elementCode ?? string.Empty).Trim().ToUpperInvariant();
        long? elementId = await _db.PayElements.Where(p => p.CompanyId == companyId && p.Code == key).Select(p => (long?)p.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        List<EmployeePayElement> active = [.. employee.PayElements.Where(p => p.PayElementId == elementId && p.IsActive)];
        if (active.Count == 0)
        {
            return Result.Failure(Error.NotFound("Payroll.ElementNotAssigned", $"{employee.EmployeeNumber} does not have {key}."));
        }

        active.ForEach(p => p.IsActive = false);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> SaveTaxCreditAsync(long companyId, TaxCreditInput credit, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(credit);
        string code = (credit.Code ?? string.Empty).Trim().ToUpperInvariant();
        string? element = string.IsNullOrWhiteSpace(credit.ElementCode) ? null : credit.ElementCode.Trim().ToUpperInvariant();
        if (code.Length == 0 || string.IsNullOrWhiteSpace(credit.Name) || credit.MinimumAge is < 0
            || (credit.Basis == TaxCreditBasis.Fixed ? credit.Amount <= 0m : credit.Percent is <= 0m or > 100m || element is null))
        {
            return Result.Failure(Error.Validation("Pay.TaxCredit", "A tax credit needs a code, a name and a positive monthly amount - or a percentage up to 100 of a pay element."));
        }

        if (element is not null && !await _db.PayElements.AnyAsync(e => e.CompanyId == companyId && e.Code == element, ct).ConfigureAwait(false))
        {
            return Result.Failure(Error.NotFound("Pay.ElementNotFound", $"Pay element {element} not found."));
        }

        TaxCredit? saved = await _db.Set<TaxCredit>().FirstOrDefaultAsync(c => c.CompanyId == companyId && c.Code == code, ct).ConfigureAwait(false);
        if (saved is null)
        {
            saved = new TaxCredit(companyId, code, credit.Name);
            _db.Set<TaxCredit>().Add(saved);
        }

        saved.Name = credit.Name.Trim();
        saved.Basis = credit.Basis;
        saved.Amount = credit.Basis == TaxCreditBasis.Fixed ? credit.Amount : 0m;
        saved.Percent = credit.Basis == TaxCreditBasis.PercentOfElement ? credit.Percent : 0m;
        saved.ElementCode = credit.Basis == TaxCreditBasis.PercentOfElement ? element : null;
        saved.MinimumAge = credit.MinimumAge;
        saved.AppliesToAll = credit.AppliesToAll;
        saved.IsActive = credit.IsActive;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> SetEmployeeTaxCreditAsync(long companyId, string employeeNumber, string creditCode, bool given, CancellationToken ct = default)
    {
        string number = (employeeNumber ?? string.Empty).Trim().ToUpperInvariant(), code = (creditCode ?? string.Empty).Trim().ToUpperInvariant();
        long? employee = await _db.Employees.Where(e => e.CompanyId == companyId && e.EmployeeNumber == number).Select(e => (long?)e.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        long? credit = await _db.Set<TaxCredit>().Where(c => c.CompanyId == companyId && c.Code == code).Select(c => (long?)c.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (employee is null || credit is null)
        {
            return Result.Failure(Error.NotFound("Pay.TaxCreditNotFound", employee is null ? $"Employee {number} not found." : $"Tax credit {code} not found."));
        }

        EmployeeTaxCredit? link = await _db.Set<EmployeeTaxCredit>().FirstOrDefaultAsync(g => g.EmployeeId == employee && g.TaxCreditId == credit, ct).ConfigureAwait(false);
        if (given && link is null)
        {
            _db.Set<EmployeeTaxCredit>().Add(new EmployeeTaxCredit(companyId, employee.Value, credit.Value));
        }
        else if (!given && link is not null)
        {
            _db.Set<EmployeeTaxCredit>().Remove(link);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<long>> CreateTaxTableAsync(long companyId, string code, DateOnly effectiveFrom, CurrencyCode? currency, IReadOnlyList<TaxBandInput> bands, CancellationToken ct = default)
    {
        string key = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (key.Length == 0 || bands.Count == 0)
        {
            return Error.Validation("Payroll.TaxTableRequired", "A tax table needs a code and at least one band.");
        }

        List<TaxBandInput> ordered = [.. bands.OrderBy(b => b.From)];
        if (ordered[0].From != 0m || ordered.Any(b => b.RatePercent is < 0m or > 100m || (b.To is decimal to && to <= b.From)))
        {
            return Error.Validation("Payroll.TaxBands", "Bands start at nil, each ends above where it starts, and rates are between 0 and 100%.");
        }

        for (int i = 0; i < ordered.Count; i++)
        {
            bool last = i == ordered.Count - 1;
            if (last ? ordered[i].To is not null : ordered[i].To != ordered[i + 1].From)
            {
                return Error.Validation("Payroll.TaxBands", last ? "The last band is open-ended (no upper limit)." : $"Band {i + 2} must start where band {i + 1} ends ({ordered[i].To}).");
            }
        }

        if (await _db.Set<TaxTable>().AnyAsync(t => t.CompanyId == companyId && t.Code == key && t.EffectiveFrom == effectiveFrom, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Payroll.TaxTableExists", $"Tax table {key} already has bands from {effectiveFrom:yyyy-MM-dd}.");
        }

        TaxTable table = new(companyId, key, effectiveFrom, currency);
        ordered.ForEach(b => table.AddBand(b.From, b.To, b.RatePercent));
        _db.Set<TaxTable>().Add(table);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return table.Id;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<string>>> SeedStandardElementsAsync(long companyId, string salaryExpenseAccount, string payeLiabilityAccount, string taxTableCode, CancellationToken ct = default)
    {
        // The brief's standard elements (§4.15.2). Amounts and rates are set per employee (or entered as the period's variables for
        // once-off pay such as bonus, backpay or gratuity); overtime is a rate per hour the employee's hourly rate × the multiplier is set on.
        string pay = salaryExpenseAccount;
        string owed = payeLiabilityAccount;
        NewPayElement Earn(string code, string name, CalculationBasis basis, int sequence) => new(code, name, PayElementType.Earning, basis, sequence, pay) { Taxable = true };
        NewPayElement Deduct(string code, string name, CalculationBasis basis, int sequence) => new(code, name, PayElementType.Deduction, basis, sequence, owed);
        NewPayElement[] standard =
        [
            Earn("BASIC", "Basic salary", CalculationBasis.Formula, 1) with { Formula = "BASIC", Pensionable = true, SubjectToSocialSecurity = true, OvertimeBase = true, LeavePayBase = true },
            Earn("OVERTIME", "Overtime", CalculationBasis.RatePerUnit, 2),
            Earn("HOUSING", "Housing allowance", CalculationBasis.FixedAmount, 3) with { BenefitCategory = "Housing" },
            Earn("TRANSPORT", "Transport allowance", CalculationBasis.FixedAmount, 4) with { BenefitCategory = "Transport" },
            Deduct("PAYE", "PAYE", CalculationBasis.TaxTable, 50) with { TaxTableCode = taxTableCode },
            Earn("OVERTIME2", "Overtime at double time", CalculationBasis.RatePerUnit, 5),
            Earn("OVERTIMEPH", "Public-holiday overtime", CalculationBasis.RatePerUnit, 6),
            Earn("ACTING", "Acting allowance", CalculationBasis.Formula, 7) with { Formula = "ACTINGALLOWANCE" },
            Earn("RESPONSIBILITY", "Responsibility allowance", CalculationBasis.FixedAmount, 8),
            Earn("SHIFT", "Shift and night allowance", CalculationBasis.FixedAmount, 9),
            Earn("STANDBY", "Standby and call-out", CalculationBasis.RatePerUnit, 10),
            Earn("COMMISSION", "Commission", CalculationBasis.FixedAmount, 11),
            Earn("BONUS", "Bonus", CalculationBasis.FixedAmount, 12),
            Earn("THIRTEENTH", "13th cheque", CalculationBasis.FixedAmount, 13),
            Earn("BACKPAY", "Backpay", CalculationBasis.FixedAmount, 14),
            Earn("LEAVEPAY", "Leave pay", CalculationBasis.FixedAmount, 15),
            Earn("LEAVEENCASH", "Leave encashment", CalculationBasis.FixedAmount, 16),
            Earn("GRATUITY", "Gratuity", CalculationBasis.FixedAmount, 17),
            Earn("SEVERANCE", "Severance pay", CalculationBasis.FixedAmount, 18),
            Earn("NOTICEPAY", "Notice pay", CalculationBasis.FixedAmount, 19),
            Deduct("PENSION", "Pension fund", CalculationBasis.PercentOfBasic, 51),
            Deduct("SOCIALSEC", "Social security", CalculationBasis.PercentOfBasic, 52),
            Deduct("MEDICAL", "Medical aid", CalculationBasis.FixedAmount, 53),
            Deduct("UNION", "Union dues", CalculationBasis.FixedAmount, 54),
            Deduct("ADVANCE", "Salary advance recovery", CalculationBasis.FixedAmount, 55),
            Deduct("GARNISHEE", "Garnishee and court orders", CalculationBasis.FixedAmount, 56),
            Deduct("FUNERAL", "Funeral scheme", CalculationBasis.FixedAmount, 57),
            Deduct("SAVINGS", "Savings scheme", CalculationBasis.FixedAmount, 58),
            Deduct("STAFFPURCH", "Staff purchases", CalculationBasis.Formula, 59) with { Formula = "STAFFPURCHASES", Recovers = PayRecovery.StaffPurchases },
            Deduct("HOUSERENT", "Housing rent", CalculationBasis.Formula, 60) with { Formula = "HOUSINGRENT", Recovers = PayRecovery.HousingRent },
            Deduct("OVERPAYMENT", "Salary overpayment recovery", CalculationBasis.FixedAmount, 61),
            Deduct("LEAVEADV", "Leave pay advance recovery", CalculationBasis.FixedAmount, 62),
        ];
        List<string> created = [];
        foreach (NewPayElement element in standard)
        {
            if (await _db.PayElements.AnyAsync(p => p.CompanyId == companyId && p.Code == element.Code, ct).ConfigureAwait(false))
            {
                continue;
            }

            Result<long> added = await CreatePayElementAsync(companyId, element, ct).ConfigureAwait(false);
            if (added.IsFailure)
            {
                return added.Error;
            }

            created.Add(element.Code);
        }

        return created;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<long>>> RequestBankChangeAsync(long companyId, string employeeNumber, IReadOnlyDictionary<string, string> changes, long requestedByUserId, CancellationToken ct = default)
    {
        if (await EmployeeAsync(companyId, employeeNumber, ct).ConfigureAwait(false) is not Employee employee)
        {
            return EmployeeNotFound(employeeNumber);
        }

        if (changes.Count == 0 || changes.Keys.Any(k => !BankFields.Contains(k)) || changes.Values.Any(string.IsNullOrWhiteSpace))
        {
            return Error.Validation("Payroll.BankFields", $"Only {string.Join(", ", BankFields)} can be changed this way, each to a value.");
        }

        List<DetailChangeRequest> requests = [.. changes.Select(c => new DetailChangeRequest(companyId, employee.Id, c.Key, Current(employee, c.Key), c.Value.Trim()) { RequestedByUserId = requestedByUserId })];
        _db.DetailChangeRequests.AddRange(requests);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return requests.Select(r => r.Id).ToList();
    }

    /// <summary>The change-request field a net-pay split travels under.</summary>
    public const string PaymentSplitField = "PaymentSplit";

    /// <inheritdoc />
    public async Task<Result<long>> RequestPaymentSplitAsync(long companyId, string employeeNumber, PaymentSplitChange change, long requestedByUserId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (await EmployeeAsync(companyId, employeeNumber, ct).ConfigureAwait(false) is not Employee employee)
        {
            return EmployeeNotFound(employeeNumber);
        }

        string destination = (change.Destination ?? string.Empty).Trim();
        if (destination.Length == 0)
        {
            return Error.Validation("Payroll.SplitDestination", "Give the account or wallet number the split goes to.");
        }

        List<EmployeePaymentSplit> existing = await _db.PaymentSplits.AsNoTracking().Where(s => s.EmployeeId == employee.Id).ToListAsync(ct).ConfigureAwait(false);
        EmployeePaymentSplit? same = existing.FirstOrDefault(s => s.Method == change.Method && s.Destination == destination);
        if (change.Remove && same is null)
        {
            return Error.NotFound("Payroll.SplitNotFound", $"{employee.EmployeeNumber} has no split to {destination}.");
        }

        if (!change.Remove && EmployeePaymentSplit.Problem(change.Amount, change.Percent, existing.Where(s => s != same).Sum(s => s.Percent ?? 0m)) is string problem)
        {
            return Error.Validation("Payroll.Split", problem);
        }

        if (await _db.DetailChangeRequests.AnyAsync(r => r.CompanyId == companyId && r.EmployeeId == employee.Id && r.Field == PaymentSplitField && r.Status == SelfServiceStatus.Pending, ct)
            .ConfigureAwait(false))
        {
            return Error.Conflict("Payroll.SplitPending", $"{employee.EmployeeNumber} already has a split change waiting for approval.");
        }

        PaymentSplitChange clean = change with { Destination = destination, BankName = Clean(change.BankName), BankBranch = Clean(change.BankBranch) };
        DetailChangeRequest request = new(companyId, employee.Id, PaymentSplitField, same is null ? null : DescribeSplit(same), System.Text.Json.JsonSerializer.Serialize(clean))
        {
            RequestedByUserId = requestedByUserId,
        };
        _db.DetailChangeRequests.Add(request);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return request.Id;
    }

    // An approved split request applied: the split to that destination replaced, or taken off.
    private async Task ApplySplitAsync(long companyId, long employeeId, string json, CancellationToken ct)
    {
        PaymentSplitChange change = System.Text.Json.JsonSerializer.Deserialize<PaymentSplitChange>(json) ?? throw new InvalidOperationException("Unreadable split request.");
        _db.PaymentSplits.RemoveRange(await _db.PaymentSplits.Where(s => s.EmployeeId == employeeId && s.Method == change.Method && s.Destination == change.Destination).ToListAsync(ct).ConfigureAwait(false));
        if (!change.Remove)
        {
            _db.PaymentSplits.Add(new EmployeePaymentSplit(companyId, employeeId, change.Method, change.Destination, change.Amount ?? 0m, change.Percent)
            {
                BankName = change.BankName, BankBranch = change.BankBranch,
            });
        }
    }

    private static string DescribeSplit(EmployeePaymentSplit split)
        => $"{split.Method} {split.Destination} " + (split.Percent is decimal p ? p.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "%" : split.Amount.ToString("N2", System.Globalization.CultureInfo.InvariantCulture));

    private static string DescribeSplitRequest(string json)
    {
        PaymentSplitChange? change = System.Text.Json.JsonSerializer.Deserialize<PaymentSplitChange>(json);
        return change is null ? json
            : change.Remove ? $"Remove {change.Method} {change.Destination}"
            : $"{change.Method} {change.Destination} " + (change.Percent is decimal p ? p.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "%"
                : (change.Amount ?? 0m).ToString("N2", System.Globalization.CultureInfo.InvariantCulture)) + (change.BankName is null ? string.Empty : $" at {change.BankName}");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BankChangeRequest>> PendingBankChangesAsync(long companyId, CancellationToken ct = default)
    {
        List<string> fields = [.. BankFields, PaymentSplitField];
        List<BankChangeRequest> pending = await (from r in _db.DetailChangeRequests.AsNoTracking()
                      join e in _db.Employees.AsNoTracking() on r.EmployeeId equals e.Id
                      where r.CompanyId == companyId && r.Status == SelfServiceStatus.Pending && fields.Contains(r.Field)
                      orderby r.Id
                      select new BankChangeRequest(r.Id, e.EmployeeNumber, r.Field, r.OldValue, r.NewValue, r.RequestedByUserId)).ToListAsync(ct).ConfigureAwait(false);
        return [.. pending.Select(r => r.Field == PaymentSplitField ? r with { NewValue = DescribeSplitRequest(r.NewValue) } : r)];
    }

    /// <inheritdoc />
    public async Task<Result> DecideBankChangeAsync(long companyId, long requestId, bool approve, long decidedByUserId, string? note, CancellationToken ct = default)
    {
        DetailChangeRequest? request = await _db.DetailChangeRequests.FirstOrDefaultAsync(r => r.Id == requestId && r.CompanyId == companyId, ct).ConfigureAwait(false);
        if (request is null || request.Status != SelfServiceStatus.Pending || !(BankFields.Contains(request.Field) || request.Field == PaymentSplitField))
        {
            return Result.Failure(Error.NotFound("Payroll.ChangeNotPending", $"No pending bank-detail change {requestId}."));
        }

        if (request.RequestedByUserId == decidedByUserId)
        {
            return Result.Failure(Error.Rule("Payroll.SelfApproval", "The person who asked for a bank-detail change cannot approve it."));
        }

        request.Decide(approve, decidedByUserId, note);
        if (approve && request.Field == PaymentSplitField)
        {
            await ApplySplitAsync(companyId, request.EmployeeId, request.NewValue, ct).ConfigureAwait(false);
        }
        else if (approve)
        {
            Employee employee = await _db.Employees.SingleAsync(e => e.Id == request.EmployeeId, ct).ConfigureAwait(false);
            Apply(employee, request.Field, request.NewValue);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <summary>Writes an approved bank or mobile-money detail onto the employee.</summary>
    internal static void Apply(Employee employee, string field, string value)
    {
        switch (field)
        {
            case "BankName":
                employee.BankName = value;
                break;
            case "BankBranch":
                employee.BankBranch = value;
                break;
            case "BankAccountNumber":
                employee.BankAccountNumber = value;
                break;
            case "MobileMoneyNumber":
                employee.MobileMoneyNumber = value;
                break;
        }
    }

    private static string? Current(Employee employee, string field) => field switch
    {
        "BankName" => employee.BankName,
        "BankBranch" => employee.BankBranch,
        "BankAccountNumber" => employee.BankAccountNumber,
        _ => employee.MobileMoneyNumber,
    };

    private async Task<Employee?> EmployeeAsync(long companyId, string employeeNumber, CancellationToken ct)
    {
        string number = (employeeNumber ?? string.Empty).Trim().ToUpperInvariant();
        return await _db.Employees.Include(e => e.PayElements).FirstOrDefaultAsync(e => e.CompanyId == companyId && e.EmployeeNumber.ToUpper() == number, ct).ConfigureAwait(false);
    }

    private static Error EmployeeNotFound(string number) => Error.NotFound("Payroll.EmployeeNotFound", $"Employee {number} not found.");

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    // Pay is posted to the employee's department and cost centre, which the ledger keys by codes of up to three characters.
    private static Error? DimensionProblem(string? department, string? costCentre)
        => new[] { department, costCentre }.Any(d => d is not null && d.Trim().Length > 3)
            ? Error.Validation("Payroll.DimensionCode", "Department and cost centre are the ledger's codes (up to 3 characters), e.g. FIN or WKS.")
            : null;
}
