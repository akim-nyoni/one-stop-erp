using One4All.Core.Common;

namespace One4All.Core.Security;

/// <summary>
/// A collection of users that inherit a set of roles (<c>sec.group</c>). Groups let an administrator
/// manage access by team/function rather than per user.
/// </summary>
public sealed class Group : SoftDeletableEntity
{
    private readonly List<GroupRole> _roles = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private Group()
    {
        Name = string.Empty;
        NormalizedName = string.Empty;
    }

    /// <summary>Creates a group.</summary>
    /// <param name="name">The group name.</param>
    /// <param name="description">Optional description.</param>
    public Group(string name, string? description = null)
    {
        Name = name;
        NormalizedName = name.Trim().ToUpperInvariant();
        Description = description;
    }

    /// <summary>The group name.</summary>
    public string Name { get; private set; }

    /// <summary>Upper-cased name for case-insensitive uniqueness.</summary>
    public string NormalizedName { get; private set; }

    /// <summary>Optional description.</summary>
    public string? Description { get; set; }

    /// <summary>The roles conferred on members of this group.</summary>
    public IReadOnlyCollection<GroupRole> Roles => _roles.AsReadOnly();

    /// <summary>Adds a role to the group if not already present.</summary>
    /// <param name="roleId">The role id.</param>
    public void AddRole(long roleId)
    {
        if (_roles.All(r => r.RoleId != roleId))
        {
            _roles.Add(new GroupRole(roleId));
        }
    }

    /// <summary>Removes a role from the group.</summary>
    /// <param name="roleId">The role id.</param>
    public void RemoveRole(long roleId) => _roles.RemoveAll(r => r.RoleId == roleId);
}

/// <summary>Association between a <see cref="Group"/> and a <see cref="Role"/> (<c>sec.group_role</c>).</summary>
public sealed class GroupRole : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private GroupRole()
    {
    }

    /// <summary>Creates the association.</summary>
    /// <param name="roleId">The role id.</param>
    public GroupRole(long roleId) => RoleId = roleId;

    /// <summary>The owning group id.</summary>
    public long GroupId { get; private set; }

    /// <summary>The role id.</summary>
    public long RoleId { get; private set; }
}
