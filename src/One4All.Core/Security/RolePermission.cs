using One4All.Core.Common;

namespace One4All.Core.Security;

/// <summary>
/// The operations a <see cref="Role"/> grants on one permission code (<c>sec.role_permission</c>).
/// </summary>
public sealed class RolePermission : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private RolePermission() => PermissionCode = string.Empty;

    /// <summary>Creates a role permission grant.</summary>
    /// <param name="permissionCode">The <c>Module.Feature</c> code.</param>
    /// <param name="operations">The granted operations.</param>
    public RolePermission(string permissionCode, OperationFlags operations)
    {
        PermissionCode = permissionCode;
        Operations = operations;
    }

    /// <summary>The foreign key to the owning role.</summary>
    public long RoleId { get; private set; }

    /// <summary>The <c>Module.Feature</c> code granted.</summary>
    public string PermissionCode { get; private set; }

    /// <summary>The granted operations.</summary>
    public OperationFlags Operations { get; private set; }

    /// <summary>Replaces the granted operations.</summary>
    /// <param name="operations">The new operation set.</param>
    public void SetOperations(OperationFlags operations) => Operations = operations;

    /// <summary>Projects this grant into the resolution input type.</summary>
    /// <returns>A <see cref="PermissionGrant"/>.</returns>
    public PermissionGrant ToGrant() => new(PermissionCode, Operations);
}
