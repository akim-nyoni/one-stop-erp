using One4All.Core.Common;

namespace One4All.Core.Security;

/// <summary>
/// A named collection of permission grants (<c>sec.role</c>). Roles are assigned to users directly or
/// via groups. System roles ship seeded and cannot be deleted, only extended by copying.
/// </summary>
public sealed class Role : SoftDeletableEntity
{
    private readonly List<RolePermission> _permissions = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private Role()
    {
        Name = string.Empty;
        NormalizedName = string.Empty;
    }

    /// <summary>Creates a role.</summary>
    /// <param name="name">The role name (unique per company).</param>
    /// <param name="description">Optional description.</param>
    /// <param name="isSystemRole">True for a seeded system role.</param>
    public Role(string name, string? description = null, bool isSystemRole = false)
    {
        Name = name;
        NormalizedName = name.Trim().ToUpperInvariant();
        Description = description;
        IsSystemRole = isSystemRole;
    }

    /// <summary>The role name.</summary>
    public string Name { get; private set; }

    /// <summary>Upper-cased name for case-insensitive uniqueness.</summary>
    public string NormalizedName { get; private set; }

    /// <summary>Optional description.</summary>
    public string? Description { get; set; }

    /// <summary>True for seeded system roles (protected from deletion/edit-in-place).</summary>
    public bool IsSystemRole { get; private set; }

    /// <summary>The permission grants held by this role.</summary>
    public IReadOnlyCollection<RolePermission> Permissions => _permissions.AsReadOnly();

    /// <summary>
    /// Grants (or updates) the operations this role confers on a permission code. Passing
    /// <see cref="OperationFlags.None"/> removes the grant.
    /// </summary>
    /// <param name="permissionCode">The <c>Module.Feature</c> code.</param>
    /// <param name="operations">The operations to grant.</param>
    public void Grant(string permissionCode, OperationFlags operations)
    {
        RolePermission? existing = _permissions.FirstOrDefault(p =>
            string.Equals(p.PermissionCode, permissionCode, StringComparison.OrdinalIgnoreCase));

        if (operations == OperationFlags.None)
        {
            if (existing is not null)
            {
                _permissions.Remove(existing);
            }

            return;
        }

        if (existing is null)
        {
            _permissions.Add(new RolePermission(permissionCode, operations));
        }
        else
        {
            existing.SetOperations(operations);
        }
    }
}
