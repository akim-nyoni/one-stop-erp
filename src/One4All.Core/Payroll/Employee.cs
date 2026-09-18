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
