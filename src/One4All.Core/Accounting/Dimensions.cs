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
