using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Payroll;
using One4All.Core.Common;
using One4All.Core.Payroll;

namespace One4All.Application.Api;

/// <summary>
/// An employee as the REST API takes it, to engage (POST) or to change (PUT; a field left out stays as it is). Enumerations are given by name.
/// Bank details are set at engagement only: later changes go through a bank detail change and a second person's approval.
/// </summary>
public sealed record EmployeeApi
{
    /// <summary>The employee number (engaging; on a change it must be the one in the route, or left out).</summary>
    public string? EmployeeNumber { get; init; }

    /// <summary>The full name.</summary>
    public string? FullName { get; init; }

    /// <summary>The basic salary (setting it needs the right to see pay).</summary>
    public decimal? BasicSalary { get; init; }

    /// <summary>The engagement date (engaging only).</summary>
    public DateOnly? EngagementDate { get; init; }

    /// <summary>Permanent, Contract, Casual... (by name).</summary>
    public string? EmploymentType { get; init; }

    /// <summary>The department code.</summary>
    public string? Department { get; init; }

    /// <summary>The cost centre code.</summary>
    public string? CostCentre { get; init; }

    /// <summary>The pay group.</summary>
    public string? PayGroup { get; init; }

    /// <summary>The grade.</summary>
    public string? Grade { get; init; }

    /// <summary>The notch.</summary>
    public int? Notch { get; init; }

    /// <summary>The national ID.</summary>
    public string? NationalIdNumber { get; init; }

    /// <summary>The tax number.</summary>
    public string? TaxNumber { get; init; }

    /// <summary>Female, Male or Unspecified (engaging only).</summary>
    public string? Gender { get; init; }

    /// <summary>The date of birth (engaging only).</summary>
    public DateOnly? DateOfBirth { get; init; }

    /// <summary>The contract's end (contract staff).</summary>
    public DateOnly? ContractEndDate { get; init; }

    /// <summary>The probation's end.</summary>
    public DateOnly? ProbationEndDate { get; init; }

    /// <summary>The work permit's expiry (changing only).</summary>
    public DateOnly? WorkPermitExpiry { get; init; }

    /// <summary>How they are paid: BankTransfer, Cash, MobileMoney, Cheque (by name).</summary>
    public string? PaymentMethod { get; init; }

    /// <summary>The bank (engaging only).</summary>
    public string? BankName { get; init; }

    /// <summary>The bank branch (engaging only).</summary>
    public string? BankBranch { get; init; }

    /// <summary>The bank account number (engaging only).</summary>
    public string? BankAccountNumber { get; init; }

    /// <summary>The mobile money number (engaging only).</summary>
    public string? MobileMoneyNumber { get; init; }

    /// <summary>The email.</summary>
    public string? Email { get; init; }

    /// <summary>How often they are paid (by name).</summary>
    public string? PayFrequency { get; init; }

    /// <summary>The branch code they work at.</summary>
    public string? Branch { get; init; }

    /// <summary>The GL project their pay is charged to.</summary>
    public string? Project { get; init; }

    /// <summary>The GL fund their pay is charged to.</summary>
    public string? Fund { get; init; }

    /// <summary>The next of kin's name.</summary>
    public string? NextOfKinName { get; init; }

    /// <summary>The next of kin's phone.</summary>
    public string? NextOfKinPhone { get; init; }

    /// <summary>How the next of kin is related.</summary>
    public string? NextOfKinRelationship { get; init; }

    /// <summary>The marital status (by name).</summary>
    public string? MaritalStatus { get; init; }

    /// <summary>The nationality.</summary>
    public string? Nationality { get; init; }

    /// <summary>The social security number.</summary>
    public string? SocialSecurityNumber { get; init; }

    /// <summary>The trade union.</summary>
    public string? UnionMembership { get; init; }

    /// <summary>The employee number of whom they report to.</summary>
    public string? ReportsTo { get; init; }

    /// <summary>The housing tenancy their rent is recovered from (changing only; empty clears it).</summary>
    public string? HousingTenancyNumber { get; init; }
}

/// <summary>An employee's record as the REST API reads it. The pay fields are removed for a user without Payroll › View Salary; bank details are not shown.</summary>
/// <param name="EmployeeNumber">The employee number.</param>
/// <param name="FullName">The full name.</param>
/// <param name="Status">Active, Suspended or Terminated.</param>
/// <param name="EmploymentType">The employment type.</param>
/// <param name="EngagementDate">When they were engaged.</param>
/// <param name="TerminationDate">When they left, if they have.</param>
/// <param name="BasicSalary">The basic salary (sensitive).</param>
/// <param name="IsSalaryHeld">Whether their pay is held (sensitive).</param>
/// <param name="Department">The department.</param>
/// <param name="CostCentre">The cost centre.</param>
/// <param name="Grade">The grade.</param>
/// <param name="Notch">The notch.</param>
/// <param name="JobTitle">The job title.</param>
/// <param name="PayGroup">The pay group.</param>
/// <param name="PayFrequency">How often they are paid.</param>
/// <param name="PaymentMethod">How they are paid.</param>
/// <param name="Gender">The gender.</param>
/// <param name="DateOfBirth">The date of birth.</param>
/// <param name="NationalIdNumber">The national ID.</param>
/// <param name="TaxNumber">The tax number.</param>
/// <param name="ContractEndDate">The contract's end.</param>
/// <param name="ProbationEndDate">The probation's end.</param>
/// <param name="WorkPermitExpiry">The work permit's expiry.</param>
/// <param name="Email">The email.</param>
/// <param name="Phone">The phone.</param>
/// <param name="Branch">The branch code they work at.</param>
/// <param name="Project">The project their pay is charged to.</param>
/// <param name="Fund">The fund their pay is charged to.</param>
/// <param name="ReportsTo">The employee number of whom they report to.</param>
/// <param name="MaritalStatus">The marital status.</param>
/// <param name="Nationality">The nationality.</param>
/// <param name="SocialSecurityNumber">The social security number.</param>
/// <param name="UnionMembership">The trade union.</param>
/// <param name="NextOfKinName">The next of kin.</param>
/// <param name="NextOfKinPhone">The next of kin's phone.</param>
/// <param name="NextOfKinRelationship">How the next of kin is related.</param>
/// <param name="HousingTenancyNumber">The housing tenancy their rent is recovered from.</param>
public sealed record EmployeeViewApi(string EmployeeNumber, string FullName, string Status, string EmploymentType, DateOnly EngagementDate, DateOnly? TerminationDate, decimal BasicSalary,
    bool IsSalaryHeld, string? Department, string? CostCentre, string? Grade, int? Notch, string? JobTitle, string? PayGroup, string PayFrequency, string PaymentMethod, string Gender,
    DateOnly? DateOfBirth, string? NationalIdNumber, string? TaxNumber, DateOnly? ContractEndDate, DateOnly? ProbationEndDate, DateOnly? WorkPermitExpiry, string? Email, string? Phone,
    string? Branch, string? Project, string? Fund, string? ReportsTo, string MaritalStatus, string? Nationality, string? SocialSecurityNumber, string? UnionMembership, string? NextOfKinName,
    string? NextOfKinPhone, string? NextOfKinRelationship, string? HousingTenancyNumber);

/// <summary>A payroll run as the REST API lists it. The pay totals are removed for a user without Payroll › View Salary.</summary>
/// <param name="RunNumber">The run's document number.</param>
/// <param name="Period">The pay period.</param>
/// <param name="PayGroup">The pay group (blank for the default payroll).</param>
/// <param name="Kind">Regular, Supplementary, Backpay or Reversal.</param>
/// <param name="PayDate">The pay date.</param>
/// <param name="Payslips">The payslips in the run.</param>
/// <param name="GrossPay">Gross earnings (sensitive).</param>
/// <param name="TotalDeductions">Deductions (sensitive).</param>
/// <param name="NetPay">Net pay (sensitive).</param>
/// <param name="Journal">The run's journal.</param>
/// <param name="PaidOn">When the net pay was paid out, if it has been.</param>
/// <param name="ClosedOn">When the run was closed, if it has been.</param>
/// <param name="ReversedOn">When the run was reversed, if it was.</param>
public sealed record PayrollRunApi(string RunNumber, string Period, string PayGroup, string Kind, DateOnly PayDate, int Payslips, decimal GrossPay, decimal TotalDeductions, decimal NetPay,
    string Journal, DateOnly? PaidOn, DateOnly? ClosedOn, DateOnly? ReversedOn);

/// <summary>One audited move of a stand's status as the REST API reads it.</summary>
/// <param name="From">Where it was.</param>
/// <param name="To">Where it went.</param>
/// <param name="Date">When.</param>
/// <param name="User">Who (blank when a document moved it).</param>
/// <param name="Authority">On what authority.</param>
/// <param name="Reason">Why.</param>
public sealed record StandMoveApi(string From, string To, DateOnly Date, string User, string Authority, string? Reason);

/// <summary>A stand's history as the REST API reads it.</summary>
/// <param name="StandNumber">The stand.</param>
/// <param name="Status">Where it is.</param>
/// <param name="Next">Where it can go.</param>
/// <param name="Moves">Every move, oldest first.</param>
/// <param name="Lineage">The stands it came from, nearest first, ending at the parcel.</param>
public sealed record StandHistoryApi(string StandNumber, string Status, IReadOnlyList<string> Next, IReadOnlyList<StandMoveApi> Moves, IReadOnlyList<string> Lineage);

/// <summary>
/// Turns the REST API's employee documents into the application's requests (enumerations parsed by name, fields a step does not take refused), and
/// employees, payroll runs and stand histories into the REST API's views. Pure.
/// </summary>
public static class PayrollApiMapping
{
    /// <summary>A new employee. The number, name and engagement date are needed; the housing tenancy and work permit are set once engaged.</summary>
    /// <param name="api">The employee as posted.</param>
    /// <returns>The request, or why not.</returns>
    public static Result<NewEmployee> NewEmployee(EmployeeApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        if (Clean(api.EmployeeNumber) is not string number || Clean(api.FullName) is not string name)
        {
            return Error.Validation("Api.Employee", "An employee needs a number and a name.");
        }

        if (api.EngagementDate is not DateOnly engaged)
        {
            return Error.Validation("Api.EngagementDate", "Give the engagement date.");
        }

        if (api.HousingTenancyNumber is not null || api.WorkPermitExpiry is not null)
        {
            return Error.Validation("Api.EngageOnly", "The housing tenancy and work permit are set with a change (PUT) once the employee is engaged.");
        }

        Result<Enums> enums = Parse(api);
        if (enums.IsFailure)
        {
            return enums.Error;
        }

        return new NewEmployee(number, name, api.BasicSalary ?? 0m, engaged)
        {
            EmploymentType = enums.Value.EmploymentType ?? default, Department = Clean(api.Department), CostCentre = Clean(api.CostCentre), PayGroup = Clean(api.PayGroup),
            Grade = Clean(api.Grade), Notch = api.Notch, NationalIdNumber = Clean(api.NationalIdNumber), TaxNumber = Clean(api.TaxNumber), Gender = enums.Value.Gender ?? Gender.Unspecified,
            DateOfBirth = api.DateOfBirth, ContractEndDate = api.ContractEndDate, ProbationEndDate = api.ProbationEndDate, PaymentMethod = enums.Value.PaymentMethod ?? PayMethod.BankTransfer,
            BankName = Clean(api.BankName), BankBranch = Clean(api.BankBranch), BankAccountNumber = Clean(api.BankAccountNumber), MobileMoneyNumber = Clean(api.MobileMoneyNumber),
            Email = Clean(api.Email), PayFrequency = enums.Value.PayFrequency, Branch = Clean(api.Branch), Project = Clean(api.Project), Fund = Clean(api.Fund),
            NextOfKinName = Clean(api.NextOfKinName), NextOfKinPhone = Clean(api.NextOfKinPhone), NextOfKinRelationship = Clean(api.NextOfKinRelationship),
            MaritalStatus = enums.Value.MaritalStatus, Nationality = Clean(api.Nationality), SocialSecurityNumber = Clean(api.SocialSecurityNumber),
            UnionMembership = Clean(api.UnionMembership), ReportsTo = Clean(api.ReportsTo),
        };
    }

    /// <summary>
    /// Changes to an employee (a field left out stays as it is). Bank details are refused (they change through an approved request), as are the
    /// engagement date, gender and date of birth, which the record keeps as engaged.
    /// </summary>
    /// <param name="employeeNumber">The employee in the route.</param>
    /// <param name="api">The changes as put.</param>
    /// <returns>The update, or why not.</returns>
    public static Result<EmployeeUpdate> Update(string employeeNumber, EmployeeApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        if (Clean(api.EmployeeNumber) is string number && !string.Equals(number, (employeeNumber ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return Error.Validation("Api.EmployeeNumber", $"The body names employee {number.ToUpperInvariant()}, the route {OrdersApiMapping.Key(employeeNumber)}; an employee number is not changed.");
        }

        if (api.BankName is not null || api.BankBranch is not null || api.BankAccountNumber is not null || api.MobileMoneyNumber is not null)
        {
            return Error.Validation("Api.BankDetails", "Bank details change through a bank detail change request and a second person's approval, not here.");
        }

        if (api.EngagementDate is not null || api.Gender is not null || api.DateOfBirth is not null)
        {
            return Error.Validation("Api.EngagedFields", "The engagement date, gender and date of birth are not changed here.");
        }

        Result<Enums> enums = Parse(api);
        if (enums.IsFailure)
        {
            return enums.Error;
        }

        return new EmployeeUpdate
        {
            FullName = Clean(api.FullName), BasicSalary = api.BasicSalary, EmploymentType = enums.Value.EmploymentType, Department = api.Department, CostCentre = api.CostCentre,
            PayGroup = api.PayGroup, Grade = api.Grade, Notch = api.Notch, NationalIdNumber = api.NationalIdNumber, TaxNumber = api.TaxNumber, ContractEndDate = api.ContractEndDate,
            ProbationEndDate = api.ProbationEndDate, WorkPermitExpiry = api.WorkPermitExpiry, PaymentMethod = enums.Value.PaymentMethod, Email = api.Email,
            PayFrequency = enums.Value.PayFrequency, Branch = api.Branch, Project = api.Project, Fund = api.Fund, NextOfKinName = api.NextOfKinName, NextOfKinPhone = api.NextOfKinPhone,
            NextOfKinRelationship = api.NextOfKinRelationship, MaritalStatus = enums.Value.MaritalStatus, Nationality = api.Nationality, SocialSecurityNumber = api.SocialSecurityNumber,
            UnionMembership = api.UnionMembership, ReportsTo = api.ReportsTo, HousingTenancyNumber = api.HousingTenancyNumber,
        };
    }

    /// <summary>An employee's record.</summary>
    /// <param name="employee">The employee.</param>
    /// <param name="branch">The branch code they work at.</param>
    /// <param name="reportsTo">The employee number of whom they report to.</param>
    /// <param name="tenancy">The housing tenancy number.</param>
    /// <returns>The API view.</returns>
    public static EmployeeViewApi Employee(Employee employee, string? branch, string? reportsTo, string? tenancy)
    {
        ArgumentNullException.ThrowIfNull(employee);
        return new EmployeeViewApi(employee.EmployeeNumber, employee.FullName, employee.Status.ToString(), employee.EmploymentType.ToString(), employee.EngagementDate, employee.TerminationDate,
            employee.BasicSalary, employee.IsSalaryHeld, employee.Department, employee.CostCentre, employee.Grade, employee.Notch, employee.JobTitle, employee.PayGroup,
            employee.PayFrequency.ToString(), employee.PaymentMethod.ToString(), employee.Gender.ToString(), employee.DateOfBirth, employee.NationalIdNumber, employee.TaxNumber,
            employee.ContractEndDate, employee.ProbationEndDate, employee.WorkPermitExpiry, employee.Email, employee.Phone, branch, employee.Project, employee.Fund, reportsTo,
            employee.MaritalStatus.ToString(), employee.Nationality, employee.SocialSecurityNumber, employee.UnionMembership, employee.NextOfKinName, employee.NextOfKinPhone,
            employee.NextOfKinRelationship, tenancy);
    }

    /// <summary>A payroll run.</summary>
    /// <param name="run">The run as posted.</param>
    /// <returns>The API view.</returns>
    public static PayrollRunApi Run(PayrollRunRecord run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return new PayrollRunApi(run.DocumentNumber, run.PeriodLabel, run.PayGroup, run.Kind.ToString(), run.PayDate, run.Employees, run.Gross, run.Deductions, run.Net, run.JournalNumber,
            run.PaidOn, run.ClosedOn, run.ReversedOn);
    }

    /// <summary>A stand's history, each move's user by name.</summary>
    /// <param name="history">The history.</param>
    /// <param name="users">User names by id.</param>
    /// <returns>The API view.</returns>
    public static StandHistoryApi StandHistory(StandHistory history, IReadOnlyDictionary<long, string> users)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(users);
        return new StandHistoryApi(history.StandNumber, history.Status.ToString(), [.. history.Next.Select(s => s.ToString())],
            [.. history.Moves.Select(m => new StandMoveApi(m.From.ToString(), m.To.ToString(), m.Date, m.UserId is long id ? users.GetValueOrDefault(id, $"user {id}") : string.Empty, m.Authority, m.Reason))],
            history.Lineage);
    }

    private static Result<Enums> Parse(EmployeeApi api)
    {
        Result<EmploymentType?> type = Optional<EmploymentType>(api.EmploymentType, "employment type");
        Result<PayMethod?> method = Optional<PayMethod>(api.PaymentMethod, "payment method");
        Result<PayFrequency?> frequency = Optional<PayFrequency>(api.PayFrequency, "pay frequency");
        Result<MaritalStatus?> marital = Optional<MaritalStatus>(api.MaritalStatus, "marital status");
        Result<Gender?> gender = Optional<Gender>(api.Gender, "gender");
        Error? error = new[] { type.IsFailure ? type.Error : null, method.IsFailure ? method.Error : null, frequency.IsFailure ? frequency.Error : null,
            marital.IsFailure ? marital.Error : null, gender.IsFailure ? gender.Error : null }.OfType<Error>().FirstOrDefault();
        return error is null ? new Enums(type.Value, method.Value, frequency.Value, marital.Value, gender.Value) : error;
    }

    // An enumeration given by name, or null when left out.
    private static Result<T?> Optional<T>(string? text, string what)
        where T : struct, Enum
    {
        if (Clean(text) is not string given)
        {
            return Result<T?>.Success(null);
        }

        return Enum.TryParse(given, ignoreCase: true, out T value) && Enum.IsDefined(value) && !int.TryParse(given, out _)
            ? Result<T?>.Success(value)
            : Error.Validation("Api.Enum", $"'{given}' is not a {what} ({string.Join(", ", Enum.GetNames<T>())}).");
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record Enums(EmploymentType? EmploymentType, PayMethod? PaymentMethod, PayFrequency? PayFrequency, MaritalStatus? MaritalStatus, Gender? Gender);
}
