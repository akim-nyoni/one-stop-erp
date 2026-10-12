using One4All.Core.Common;

namespace One4All.Core.Payroll;

/// <summary>What a person on an employee's record is to the employee.</summary>
public enum DependantKind
{
    /// <summary>A beneficiary of death benefits (pension, gratuity, funeral scheme), with a share.</summary>
    Beneficiary = 0,

    /// <summary>A dependant covered by the employee's medical aid.</summary>
    MedicalAidDependant = 1,

    /// <summary>Both a medical-aid dependant and a beneficiary.</summary>
    Both = 2,
}

/// <summary>
/// A beneficiary or medical-aid dependant on an employee's record (<c>hr.employee_dependant</c>; brief §4.15.1 employee master:
/// beneficiaries and medical aid dependants).
/// </summary>
public sealed class EmployeeDependant : Entity, ICompanyScoped
{
    private EmployeeDependant()
    {
        Name = string.Empty;
        Relationship = string.Empty;
    }

    /// <summary>Records a dependant or beneficiary.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="employeeId">The employee.</param>
    /// <param name="kind">Beneficiary, medical-aid dependant or both.</param>
    /// <param name="name">The person's name.</param>
    /// <param name="relationship">The relationship to the employee (spouse, child, parent …).</param>
    public EmployeeDependant(long companyId, long employeeId, DependantKind kind, string name, string relationship)
    {
        CompanyId = companyId;
        EmployeeId = employeeId;
        Kind = kind;
        Name = name;
        Relationship = relationship;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the employee.</summary>
    public long EmployeeId { get; private set; }

    /// <summary>Gets what the person is to the employee's benefits.</summary>
    public DependantKind Kind { get; private set; }

    /// <summary>Gets the person's name.</summary>
    public string Name { get; private set; }

    /// <summary>Gets the relationship to the employee.</summary>
    public string Relationship { get; private set; }

    /// <summary>Gets or sets the date of birth.</summary>
    public DateOnly? DateOfBirth { get; set; }

    /// <summary>Gets or sets the national ID number.</summary>
    public string? IdNumber { get; set; }

    /// <summary>Gets or sets the beneficiary's share of death benefits, in percent.</summary>
    public decimal? SharePercent { get; set; }

    /// <summary>Gets or sets the dependant's medical-aid member number.</summary>
    public string? MedicalAidNumber { get; set; }
}
