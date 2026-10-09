using One4All.Core.Common;

namespace One4All.Core.Payroll;

/// <summary>How a tax credit is worked out.</summary>
public enum TaxCreditBasis
{
    /// <summary>A fixed amount a month (an elderly or disability credit).</summary>
    Fixed = 0,

    /// <summary>A percentage of a pay element this period (a medical-aid credit of half the contributions).</summary>
    PercentOfElement = 1,
}

/// <summary>
/// A tax credit (<c>hr.tax_credit</c>, brief §4.15.3): taken off the tax the table works out, never below nil. It applies to every employee
/// or only those it is given to, and optionally only from an age.
/// </summary>
public sealed class TaxCredit : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private TaxCredit()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    /// <summary>Creates a credit.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The code.</param>
    /// <param name="name">The name.</param>
    public TaxCredit(long companyId, string code, string name)
    {
        CompanyId = companyId;
        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
        IsActive = true;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>Gets the code.</summary>
    public string Code { get; private set; }

    /// <summary>Gets or sets the name.</summary>
    public string Name { get; set; }

    /// <summary>Gets or sets how it is worked out.</summary>
    public TaxCreditBasis Basis { get; set; }

    /// <summary>Gets or sets the monthly amount (fixed).</summary>
    public decimal Amount { get; set; }

    /// <summary>Gets or sets the percentage (of an element).</summary>
    public decimal Percent { get; set; }

    /// <summary>Gets or sets the pay element it is a percentage of.</summary>
    public string? ElementCode { get; set; }

    /// <summary>Gets or sets the age from which it applies (null: any age).</summary>
    public int? MinimumAge { get; set; }

    /// <summary>Gets or sets whether every employee has it (otherwise only those it is given to).</summary>
    public bool AppliesToAll { get; set; }

    /// <summary>Gets or sets whether it is in use.</summary>
    public bool IsActive { get; set; }
}

/// <summary>A tax credit given to an employee (<c>hr.employee_tax_credit</c>).</summary>
public sealed class EmployeeTaxCredit : Entity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private EmployeeTaxCredit()
    {
    }

    /// <summary>Gives a credit to an employee.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="employeeId">The employee.</param>
    /// <param name="taxCreditId">The credit.</param>
    public EmployeeTaxCredit(long companyId, long employeeId, long taxCreditId)
    {
        CompanyId = companyId;
        EmployeeId = employeeId;
        TaxCreditId = taxCreditId;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>Gets the employee.</summary>
    public long EmployeeId { get; private set; }

    /// <summary>Gets the credit.</summary>
    public long TaxCreditId { get; private set; }
}
