using One4All.Core.Common;

namespace One4All.Core.Restaurant;

/// <summary>
/// A section (zone) of the restaurant floor plan (<c>res.floor_section</c>) - e.g. Main Dining, Patio, Bar,
/// Private Room. Tables belong to a section and a section can be assigned to a server. Operational only (no GL).
/// </summary>
public sealed class FloorSection : SoftDeletableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private FloorSection()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    /// <summary>Creates a floor section.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The short section code.</param>
    /// <param name="name">The section name.</param>
    /// <param name="description">A description, if any.</param>
    public FloorSection(long companyId, string code, string name, string? description = null)
    {
        CompanyId = companyId;
        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
        Description = description;
        IsActive = true;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The short section code.</summary>
    public string Code { get; private set; }

    /// <summary>The section name.</summary>
    public string Name { get; set; }

    /// <summary>A description, if any.</summary>
    public string? Description { get; set; }

    /// <summary>The server (waiter) assigned to the section, if any.</summary>
    public long? AssignedWaiterUserId { get; private set; }

    /// <summary>Whether the section is currently in service.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Assigns a server to the section (or clears it with null).</summary>
    /// <param name="waiterUserId">The server, or null to clear.</param>
    public void AssignServer(long? waiterUserId) => AssignedWaiterUserId = waiterUserId;

    /// <summary>Sets whether the section is in service.</summary>
    /// <param name="isActive">Whether the section is active.</param>
    public void SetActive(bool isActive) => IsActive = isActive;
}
