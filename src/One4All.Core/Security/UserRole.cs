using One4All.Core.Common;

namespace One4All.Core.Security;

/// <summary>
/// A direct assignment of a <see cref="Role"/> to a <see cref="User"/> (<c>sec.user_role</c>), optionally
/// scoped to specific data via <see cref="Scopes"/>.
/// </summary>
public sealed class UserRole : Entity
{
    private readonly List<AccessScope> _scopes = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private UserRole()
    {
    }

    /// <summary>Creates the assignment.</summary>
    /// <param name="roleId">The role id.</param>
    public UserRole(long roleId) => RoleId = roleId;

    /// <summary>The owning user id.</summary>
    public long UserId { get; private set; }

    /// <summary>The role id.</summary>
    public long RoleId { get; private set; }

    /// <summary>The data scopes applied to this assignment (empty = unrestricted).</summary>
    public IReadOnlyCollection<AccessScope> Scopes => _scopes.AsReadOnly();

    /// <summary>Adds a scope to this assignment.</summary>
    /// <param name="scope">The scope.</param>
    public void AddScope(AccessScope scope) => _scopes.Add(scope);
}

/// <summary>Membership of a <see cref="User"/> in a <see cref="Group"/> (<c>sec.user_group</c>).</summary>
public sealed class UserGroupMember : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private UserGroupMember()
    {
    }

    /// <summary>Creates the membership.</summary>
    /// <param name="groupId">The group id.</param>
    public UserGroupMember(long groupId) => GroupId = groupId;

    /// <summary>The owning user id.</summary>
    public long UserId { get; private set; }

    /// <summary>The group id.</summary>
    public long GroupId { get; private set; }
}

/// <summary>
/// An explicit per-user permission override (<c>sec.user_permission_override</c>). A deny override
/// always wins over any granted operation (brief §5.1).
/// </summary>
public sealed class UserPermissionOverrideEntity : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private UserPermissionOverrideEntity() => PermissionCode = string.Empty;

    /// <summary>Creates an override.</summary>
    /// <param name="permissionCode">The <c>Module.Feature</c> code.</param>
    /// <param name="operations">The operations affected.</param>
    /// <param name="isGrant">True to grant, false to deny.</param>
    public UserPermissionOverrideEntity(string permissionCode, OperationFlags operations, bool isGrant)
    {
        PermissionCode = permissionCode;
        Operations = operations;
        IsGrant = isGrant;
    }

    /// <summary>The owning user id.</summary>
    public long UserId { get; private set; }

    /// <summary>The <c>Module.Feature</c> code.</summary>
    public string PermissionCode { get; private set; }

    /// <summary>The operations affected.</summary>
    public OperationFlags Operations { get; private set; }

    /// <summary>True to grant the operations; false to deny them.</summary>
    public bool IsGrant { get; private set; }

    /// <summary>Projects into the resolution input type.</summary>
    /// <returns>A <see cref="PermissionOverride"/>.</returns>
    public PermissionOverride ToOverride() => new(PermissionCode, Operations, IsGrant);
}
