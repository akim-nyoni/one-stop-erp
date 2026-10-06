using One4All.Core.Common;

namespace One4All.Core.Accounting;

/// <summary>
/// A cost centre (brief §3.1): an analysis dimension combined with the account code and department to
/// form the composite posting key. Company-scoped (<c>gl.cost_centre</c>).
/// </summary>
public sealed class CostCentre : SoftDeletableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private CostCentre()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    /// <summary>Creates a cost centre.</summary>
    /// <param name="companyId">The owning company.</param>
    /// <param name="code">The code (up to 3 chars).</param>
    /// <param name="name">The name.</param>
    public CostCentre(long companyId, string code, string name)
    {
        CompanyId = companyId;
        Code = code.Trim().ToUpperInvariant();
        Name = name;
        IsActive = true;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The cost-centre code.</summary>
    public string Code { get; private set; }

    /// <summary>The cost-centre name.</summary>
    public string Name { get; set; }

    /// <summary>Whether the cost centre is active.</summary>
    public bool IsActive { get; set; }
}

/// <summary>
/// A department (brief §3.1): the second analysis dimension on the composite posting key. Company-scoped
/// (<c>gl.department</c>). Distinct from the HR/payroll organisational department, though a client may
/// align them.
/// </summary>
public sealed class Department : SoftDeletableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private Department()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    /// <summary>Creates a department.</summary>
    /// <param name="companyId">The owning company.</param>
    /// <param name="code">The code (up to 3 chars).</param>
    /// <param name="name">The name.</param>
    public Department(long companyId, string code, string name)
    {
        CompanyId = companyId;
        Code = code.Trim().ToUpperInvariant();
        Name = name;
        IsActive = true;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The department code.</summary>
    public string Code { get; private set; }

    /// <summary>The department name.</summary>
    public string Name { get; set; }

    /// <summary>Whether the department is active.</summary>
    public bool IsActive { get; set; }
}

/// <summary>A fund (vote) for fund accounting (<c>gl.fund</c>): councils keep each fund in balance, and budgets and actuals by fund.</summary>
public sealed class Fund : SoftDeletableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private Fund()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    /// <summary>Creates a fund.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The code.</param>
    /// <param name="name">The name.</param>
    /// <param name="isRestricted">Whether its money may only be spent on its purpose (grants, ring-fenced levies).</param>
    public Fund(long companyId, string code, string name, bool isRestricted)
    {
        CompanyId = companyId;
        Code = code.Trim().ToUpperInvariant();
        Name = name;
        IsRestricted = isRestricted;
        IsActive = true;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the code.</summary>
    public string Code { get; private set; }

    /// <summary>Gets or sets the name.</summary>
    public string Name { get; set; }

    /// <summary>Gets or sets a value indicating whether the fund is restricted.</summary>
    public bool IsRestricted { get; set; }

    /// <summary>Gets or sets a value indicating whether it takes postings.</summary>
    public bool IsActive { get; set; }
}

/// <summary>A project (<c>gl.project</c>): capital works, programmes and jobs analysed across accounts and budgeted on their own.</summary>
public sealed class Project : SoftDeletableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private Project()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    /// <summary>Creates a project.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The code (a job contract may use its contract number).</param>
    /// <param name="name">The name.</param>
    public Project(long companyId, string code, string name)
    {
        CompanyId = companyId;
        Code = code.Trim().ToUpperInvariant();
        Name = name;
        IsActive = true;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the code.</summary>
    public string Code { get; private set; }

    /// <summary>Gets or sets the name.</summary>
    public string Name { get; set; }

    /// <summary>Gets or sets the fund it is financed from, if one.</summary>
    public string? FundCode { get; set; }

    /// <summary>Gets or sets a value indicating whether it takes postings (closed projects do not).</summary>
    public bool IsActive { get; set; }
}
