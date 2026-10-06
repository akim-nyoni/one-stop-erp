using One4All.Core.Common;
using One4All.Core.Security;

namespace One4All.Application.Abstractions.Security;

/// <summary>
/// User, role and group administration (brief §4.17, §5): accounts with policy-checked passwords, roles, groups, and role assignments
/// restricted by data scopes. Assignments report any segregation-of-duties conflicts they create.
/// </summary>
public interface IUserAdminService
{
    /// <summary>Creates a user who must change the initial password at first sign-in.</summary>
    /// <param name="username">The login name (unique).</param>
    /// <param name="displayName">The display name.</param>
    /// <param name="email">The e-mail address.</param>
    /// <param name="initialPassword">The initial password (policy-checked).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The user id.</returns>
    Task<Result<long>> CreateUserAsync(string username, string displayName, string? email, string initialPassword, CancellationToken ct = default);

    /// <summary>Resets a user's password (they must change it at next sign-in) and clears any lockout.</summary>
    /// <param name="userId">The user.</param>
    /// <param name="newPassword">The new password (policy-checked).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    Task<Result> ResetPasswordAsync(long userId, string newPassword, CancellationToken ct = default);

    /// <summary>Activates or deactivates a user; the last active system administrator cannot be deactivated.</summary>
    /// <param name="userId">The user.</param>
    /// <param name="active">Active or not.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    Task<Result> SetActiveAsync(long userId, bool active, CancellationToken ct = default);

    /// <summary>Creates a role, optionally copying another role's grants.</summary>
    /// <param name="name">The role name (unique).</param>
    /// <param name="description">The description.</param>
    /// <param name="copyFromRoleId">A role whose grants to copy.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The role id.</returns>
    Task<Result<long>> CreateRoleAsync(string name, string? description, long? copyFromRoleId = null, CancellationToken ct = default);

    /// <summary>
    /// Assigns a role to a user, restricted by data scopes (company, branch, warehouse… codes or ids); an existing assignment's scopes are
    /// replaced. Returns the names of segregation-of-duties rules the user now breaks, as warnings.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="roleId">The role.</param>
    /// <param name="scopes">The scopes, by dimension.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Any SoD conflicts.</returns>
    Task<Result<IReadOnlyList<string>>> AssignRoleAsync(long userId, long roleId, IReadOnlyDictionary<ScopeDimension, IReadOnlyList<string>>? scopes = null, CancellationToken ct = default);

    /// <summary>Removes a role from a user.</summary>
    /// <param name="userId">The user.</param>
    /// <param name="roleId">The role.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    Task<Result> RemoveRoleAsync(long userId, long roleId, CancellationToken ct = default);

    /// <summary>Creates a group (a set of roles users inherit by joining).</summary>
    /// <param name="name">The name (unique).</param>
    /// <param name="description">The description.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The group id.</returns>
    Task<Result<long>> CreateGroupAsync(string name, string? description, CancellationToken ct = default);

    /// <summary>Adds a role to a group.</summary>
    /// <param name="groupId">The group.</param>
    /// <param name="roleId">The role.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    Task<Result> AddRoleToGroupAsync(long groupId, long roleId, CancellationToken ct = default);

    /// <summary>Puts a user in a group (or takes them out); returns any SoD conflicts as warnings.</summary>
    /// <param name="userId">The user.</param>
    /// <param name="groupId">The group.</param>
    /// <param name="member">Join or leave.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Any SoD conflicts.</returns>
    Task<Result<IReadOnlyList<string>>> SetGroupMembershipAsync(long userId, long groupId, bool member, CancellationToken ct = default);
}
