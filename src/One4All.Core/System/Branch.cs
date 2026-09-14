using One4All.Core.Common;

namespace One4All.Core.System;

/// <summary>
/// A branch / site / store within a company (<c>sys.branch</c>). Used for row-level scoping, POS
/// terminal grouping, and per-branch reporting.
/// </summary>
public sealed class Branch : SoftDeletableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private Branch()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    /// <summary>Creates a branch.</summary>
    /// <param name="companyId">The owning company id.</param>
    /// <param name="code">Short code unique within the company.</param>
    /// <param name="name">Branch name.</param>
    public Branch(long companyId, string code, string name)
    {
        CompanyId = companyId;
        Code = code;
        Name = name;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>Short code unique within the company.</summary>
    public string Code { get; private set; }

    /// <summary>Branch name.</summary>
    public string Name { get; set; }

    /// <summary>Whether this is the company's head office / default branch.</summary>
    public bool IsHeadOffice { get; set; }

    /// <summary>Optional physical address.</summary>
    public string? Address { get; set; }
}
