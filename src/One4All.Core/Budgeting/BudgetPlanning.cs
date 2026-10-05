using One4All.Core.Common;

namespace One4All.Core.Budgeting;

/// <summary>How a budget line's amount was arrived at.</summary>
public enum BudgetBasis
{
    /// <summary>Last year's figure adjusted.</summary>
    Incremental = 0,

    /// <summary>Built up from nothing and justified.</summary>
    ZeroBased = 1,

    /// <summary>From recent actual spend.</summary>
    ActualsBased = 2,

    /// <summary>Volume or driver × rate.</summary>
    DriverBased = 3,
}

/// <summary>A planning assumption or driver (<c>bgt.driver</c>): inflation, exchange rate, headcount, enrolment, volumes.</summary>
public sealed class BudgetDriver : Entity, ICompanyScoped
{
    private BudgetDriver()
    {
        Code = string.Empty;
        Name = string.Empty;
        Unit = string.Empty;
    }

    /// <summary>Creates a driver.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="fiscalYearId">The year it applies to.</param>
    /// <param name="code">The driver code.</param>
    /// <param name="name">The name.</param>
    /// <param name="unit">The unit (%, students, kl…).</param>
    /// <param name="value">The assumed value.</param>
    public BudgetDriver(long companyId, long fiscalYearId, string code, string name, string unit, decimal value)
    {
        CompanyId = companyId;
        FiscalYearId = fiscalYearId;
        Code = code.Trim().ToUpperInvariant();
        Name = name;
        Unit = unit;
        Value = value;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the year.</summary>
    public long FiscalYearId { get; private set; }

    /// <summary>Gets the code.</summary>
    public string Code { get; private set; }

    /// <summary>Gets the name.</summary>
    public string Name { get; private set; }

    /// <summary>Gets the unit.</summary>
    public string Unit { get; private set; }

    /// <summary>Gets or sets the assumed value.</summary>
    public decimal Value { get; set; }
}

/// <summary>The spending ceiling issued to a department for the year (<c>bgt.ceiling</c>).</summary>
public sealed class BudgetCeiling : Entity, ICompanyScoped
{
    private BudgetCeiling()
    {
        Department = string.Empty;
    }

    /// <summary>Issues a ceiling.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="fiscalYearId">The year.</param>
    /// <param name="department">The department.</param>
    /// <param name="amount">The ceiling.</param>
    public BudgetCeiling(long companyId, long fiscalYearId, string department, decimal amount)
    {
        CompanyId = companyId;
        FiscalYearId = fiscalYearId;
        Department = department;
        Amount = amount;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the year.</summary>
    public long FiscalYearId { get; private set; }

    /// <summary>Gets the department.</summary>
    public string Department { get; private set; }

    /// <summary>Gets or sets the ceiling.</summary>
    public decimal Amount { get; set; }
}

/// <summary>Where a department sits in the organisation (<c>bgt.department_unit</c>): division and directorate, for consolidation.</summary>
public sealed class DepartmentUnit : Entity, ICompanyScoped
{
    private DepartmentUnit()
    {
        Department = string.Empty;
        Division = string.Empty;
        Directorate = string.Empty;
    }

    /// <summary>Places a department.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="department">The department.</param>
    /// <param name="division">Its division.</param>
    /// <param name="directorate">Its directorate.</param>
    public DepartmentUnit(long companyId, string department, string division, string directorate)
    {
        CompanyId = companyId;
        Department = department;
        Division = division;
        Directorate = directorate;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the department.</summary>
    public string Department { get; private set; }

    /// <summary>Gets or sets the division.</summary>
    public string Division { get; set; }

    /// <summary>Gets or sets the directorate.</summary>
    public string Directorate { get; set; }

    /// <summary>Gets or sets the manager who holds the department's budget.</summary>
    public string? BudgetHolder { get; set; }

    /// <summary>Gets or sets where the holder's monthly statement is sent.</summary>
    public string? BudgetHolderEmail { get; set; }
}
