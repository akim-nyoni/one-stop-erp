using One4All.Core.Common;

namespace One4All.Core.Payroll;

/// <summary>The lifecycle of an employee (brief §4.15.1).</summary>
public enum EmployeeStatus
{
    /// <summary>Active on the payroll.</summary>
    Active = 1,

    /// <summary>Suspended (not paid).</summary>
    Suspended = 2,

    /// <summary>Terminated.</summary>
    Terminated = 3,
}

/// <summary>How an employee's net pay is paid (brief §14 PR11–PR13).</summary>
public enum PayMethod
{
    /// <summary>By bank transfer (EFT).</summary>
    BankTransfer = 1,

    /// <summary>In cash (see the denomination breakdown).</summary>
    Cash = 2,

    /// <summary>To a mobile-money wallet.</summary>
    MobileMoney = 3,
}

/// <summary>An employee's gender, for statutory headcount returns.</summary>
public enum Gender
{
    /// <summary>Not recorded.</summary>
    Unspecified = 0,

    /// <summary>Female.</summary>
    Female = 1,

    /// <summary>Male.</summary>
    Male = 2,
}

/// <summary>
/// An employee (brief §4.15.1, <c>hr.employee</c>). Carries the basic salary and the department/cost-centre
/// that drive the payroll journal; pay is assembled from assigned pay elements at run time.
/// </summary>
public sealed class Employee : SoftDeletableEntity, ICompanyScoped
{
    private readonly List<EmployeePayElement> _payElements = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private Employee()
    {
        EmployeeNumber = string.Empty;
        FullName = string.Empty;
    }

    /// <summary>Creates an employee.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="employeeNumber">The employee number.</param>
    /// <param name="fullName">The full name as on the national ID.</param>
    /// <param name="basicSalary">The basic salary per period.</param>
    /// <param name="engagementDate">The engagement date.</param>
    public Employee(long companyId, string employeeNumber, string fullName, decimal basicSalary, DateOnly engagementDate)
    {
        CompanyId = companyId;
        EmployeeNumber = employeeNumber.Trim().ToUpperInvariant();
        FullName = fullName;
        BasicSalary = basicSalary;
        EngagementDate = engagementDate;
        Status = EmployeeStatus.Active;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The employee number.</summary>
    public string EmployeeNumber { get; private set; }

    /// <summary>The full name as on the national ID.</summary>
    public string FullName { get; set; }

    /// <summary>The basic salary per period.</summary>
    public decimal BasicSalary { get; set; }

    /// <summary>The engagement date.</summary>
    public DateOnly EngagementDate { get; private set; }

    /// <summary>The department that drives the payroll journal.</summary>
    public string? Department { get; set; }

    /// <summary>The cost centre that drives the payroll journal.</summary>
    public string? CostCentre { get; set; }

    /// <summary>The status.</summary>
    public EmployeeStatus Status { get; private set; }

    /// <summary>The assigned pay elements.</summary>
    public IReadOnlyList<EmployeePayElement> PayElements => _payElements.AsReadOnly();

    /// <summary>Assigns a pay element to the employee.</summary>
    /// <param name="payElementId">The pay element.</param>
    /// <param name="overrideAmount">An override fixed amount, if any.</param>
    /// <param name="overrideRate">An override rate, if any.</param>
    /// <param name="units">Units for a rate-per-unit element, if any.</param>
    /// <returns>The created assignment.</returns>
    public EmployeePayElement AssignPayElement(long payElementId, decimal? overrideAmount = null, decimal? overrideRate = null, decimal? units = null)
    {
        EmployeePayElement assignment = new(payElementId, overrideAmount, overrideRate, units);
        _payElements.Add(assignment);
        return assignment;
    }

    /// <summary>Terminates the employee.</summary>
    public void Terminate() => Status = EmployeeStatus.Terminated;

    /// <summary>The national ID number.</summary>
    public string? NationalIdNumber { get; set; }

    /// <summary>The tax authority's number for the employee.</summary>
    public string? TaxNumber { get; set; }

    /// <summary>Gender, for statutory returns.</summary>
    public Gender Gender { get; set; }

    /// <summary>Date of birth.</summary>
    public DateOnly? DateOfBirth { get; set; }

    /// <summary>Salary grade.</summary>
    public string? Grade { get; set; }

    /// <summary>Notch within the grade.</summary>
    public int? Notch { get; set; }

    /// <summary>When a fixed-term contract ends.</summary>
    public DateOnly? ContractEndDate { get; set; }

    /// <summary>When probation ends.</summary>
    public DateOnly? ProbationEndDate { get; set; }

    /// <summary>When the work permit expires (non-citizens).</summary>
    public DateOnly? WorkPermitExpiry { get; set; }

    /// <summary>How net pay is paid.</summary>
    public PayMethod PaymentMethod { get; set; } = PayMethod.BankTransfer;

    /// <summary>The bank, for bank transfers.</summary>
    public string? BankName { get; set; }

    /// <summary>The branch code, for bank transfers.</summary>
    public string? BankBranch { get; set; }

    /// <summary>The account number, for bank transfers.</summary>
    public string? BankAccountNumber { get; set; }

    /// <summary>The wallet number, for mobile-money payment.</summary>
    public string? MobileMoneyNumber { get; set; }

    /// <summary>When employment ended.</summary>
    public DateOnly? TerminationDate { get; private set; }

    /// <summary>Why employment ended.</summary>
    public string? TerminationReason { get; private set; }

    /// <summary>True while the employee's pay is held back from payment.</summary>
    public bool IsSalaryHeld { get; private set; }

    /// <summary>Why pay is held.</summary>
    public string? HoldReason { get; private set; }

    /// <summary>Ends employment on a date, with the reason.</summary>
    /// <param name="date">The last day.</param>
    /// <param name="reason">Why.</param>
    public void Terminate(DateOnly date, string reason)
    {
        Status = EmployeeStatus.Terminated;
        TerminationDate = date;
        TerminationReason = reason;
    }

    /// <summary>Holds the employee's pay back from payment.</summary>
    /// <param name="reason">Why.</param>
    public void HoldSalary(string reason)
    {
        IsSalaryHeld = true;
        HoldReason = reason;
    }

    /// <summary>Releases held pay for payment.</summary>
    public void ReleaseSalary()
    {
        IsSalaryHeld = false;
        HoldReason = null;
    }
}

/// <summary>An assignment of a pay element to an employee (<c>hr.employee_pay_element</c>).</summary>
public sealed class EmployeePayElement : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private EmployeePayElement()
    {
    }

    /// <summary>Creates an assignment.</summary>
    /// <param name="payElementId">The pay element.</param>
    /// <param name="overrideAmount">An override fixed amount, if any.</param>
    /// <param name="overrideRate">An override rate, if any.</param>
    /// <param name="units">Units for a rate-per-unit element, if any.</param>
    public EmployeePayElement(long payElementId, decimal? overrideAmount, decimal? overrideRate, decimal? units)
    {
        PayElementId = payElementId;
        OverrideAmount = overrideAmount;
        OverrideRate = overrideRate;
        Units = units;
        IsActive = true;
    }

    /// <summary>The owning employee id.</summary>
    public long EmployeeId { get; private set; }

    /// <summary>The pay element.</summary>
    public long PayElementId { get; private set; }

    /// <summary>An override fixed amount, if any.</summary>
    public decimal? OverrideAmount { get; private set; }

    /// <summary>An override rate, if any.</summary>
    public decimal? OverrideRate { get; private set; }

    /// <summary>Units for a rate-per-unit element, if any.</summary>
    public decimal? Units { get; private set; }

    /// <summary>Whether the assignment is active.</summary>
    public bool IsActive { get; set; }
}
