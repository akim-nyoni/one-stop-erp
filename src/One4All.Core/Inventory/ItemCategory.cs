using One4All.Core.Common;

namespace One4All.Core.Inventory;

/// <summary>A category items are grouped under (brief §12: seeded per vertical; items keep the category's name).</summary>
public sealed class ItemCategory : AuditableEntity, ICompanyScoped
{
    private ItemCategory()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    /// <summary>Creates a category.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The code (upper-cased).</param>
    /// <param name="name">The name.</param>
    public ItemCategory(long companyId, string code, string name)
    {
        CompanyId = companyId;
        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
        IsActive = true;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the code.</summary>
    public string Code { get; private set; }

    /// <summary>Gets or sets the name.</summary>
    public string Name { get; set; }

    /// <summary>Gets or sets a value indicating whether it is offered for new items.</summary>
    public bool IsActive { get; set; }

    /// <summary>Gets or sets the category this one sits under (null: a top-level category), for drilling down at the till.</summary>
    public string? ParentCode { get; set; }

    /// <summary>The costing method an item created in this category takes when none is given (brief §3.4); null leaves the company default.</summary>
    public CostingMethod? CostingMethod { get; set; }
}
