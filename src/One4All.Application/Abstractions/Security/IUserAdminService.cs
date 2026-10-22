using One4All.Core.Common;
using One4All.Core.Security;

namespace One4All.Application.Abstractions.Security;

/// <summary>The outcome of assigning a role.</summary>
/// <param name="Granted">Whether the role was assigned now.</param>
/// <param name="RequestId">The held grant awaiting a second administrator, when it was not.</param>
/// <param name="Conflicts">The segregation-of-duties rules the user breaks (when held: the ones the grant would newly break).</param>
public sealed record RoleAssignment(bool Granted, long? RequestId, IReadOnlyList<string> Conflicts);

/// <summary>A role grant waiting for a second administrator.</summary>
/// <param name="RequestId">The held grant.</param>
/// <param name="Username">The user the role is for.</param>
/// <param name="RoleName">The role.</param>
/// <param name="Scopes">The scopes, or null for unrestricted.</param>
/// <param name="Conflicts">The rules the grant would newly break.</param>
/// <param name="RequestedBy">The administrator who asked.</param>
/// <param name="RequestedOnUtc">When.</param>
public sealed record PendingRoleGrant(long RequestId, string Username, string RoleName, string? Scopes, string Conflicts, string RequestedBy, DateTimeOffset RequestedOnUtc);

/// <summary>
/// User, role and group administration (brief §4.17, §5): accounts with policy-checked passwords, roles, groups, and role assignments
/// restricted by data scopes. A role assignment that would create a segregation-of-duties conflict waits for a second administrator.
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

    /// <summary>Gives a user a cashier card for till login (what the swipe or scan reads; blank removes it). A card belongs to one user.</summary>
    Task<Result> SetCashierCardAsync(long userId, string? cardRead, CancellationToken ct = default);

    /// <summary>Links a user to their fingerprint reader enrolment (blank removes it). An enrolment belongs to one user.</summary>
    Task<Result> SetBiometricIdAsync(long userId, string? biometricId, CancellationToken ct = default);

    /// <summary>Creates a role, optionally copying another role's grants.</summary>
    /// <param name="name">The role name (unique).</param>
    /// <param name="description">The description.</param>
    /// <param name="copyFromRoleId">A role whose grants to copy.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The role id.</returns>
    Task<Result<long>> CreateRoleAsync(string name, string? description, long? copyFromRoleId = null, CancellationToken ct = default);

    /// <summary>
    /// Assigns a role to a user, restricted by data scopes (company, branch, warehouse... codes or ids); an existing assignment's scopes are
    /// replaced, in one unit. Nobody grants a role to themselves. When the role would break a segregation-of-duties rule the user does
    /// not already break, it is not granted: it is held as a <see cref="RoleGrantRequest"/> for a second administrator to approve.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="roleId">The role.</param>
    /// <param name="actingUserId">The administrator making the change (recorded and audited).</param>
    /// <param name="scopes">The scopes, by dimension.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Whether it was granted or held, with the conflicts it breaks.</returns>
    Task<Result<RoleAssignment>> AssignRoleAsync(long userId, long roleId, long actingUserId, IReadOnlyDictionary<ScopeDimension, IReadOnlyList<string>>? scopes = null, CancellationToken ct = default);

    /// <summary>
    /// Removes a role from a user. An administrator cannot remove from themselves the last role that gives them the user-administration
    /// right (<c>Security.Role</c> edit), so nobody locks themselves out of user administration by mistake.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="roleId">The role.</param>
    /// <param name="actingUserId">The administrator making the change (audited).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    Task<Result> RemoveRoleAsync(long userId, long roleId, long actingUserId, CancellationToken ct = default);

    /// <summary>The role grants held for a second administrator, oldest first.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The pending grants.</returns>
    Task<IReadOnlyList<PendingRoleGrant>> PendingRoleGrantsAsync(CancellationToken ct = default);

    /// <summary>
    /// Approves (the role is assigned with its scopes) or rejects a held role grant, in one unit. The decider must hold the
    /// user-administration right and be neither the administrator who asked nor the user the role is for.
    /// </summary>
    /// <param name="requestId">The held grant.</param>
    /// <param name="approve">Approve or reject.</param>
    /// <param name="actingUserId">The deciding administrator.</param>
    /// <param name="note">A note (required to reject).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    Task<Result> DecideRoleGrantAsync(long requestId, bool approve, long actingUserId, string? note, CancellationToken ct = default);

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

    /// <summary>Sets (or clears) a user's manager, who approves stages that ask for the requester's manager; never the user themselves.</summary>
    /// <param name="userId">The user.</param>
    /// <param name="managerUserId">The manager, or null.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    Task<Result> SetManagerAsync(long userId, long? managerUserId, CancellationToken ct = default);

    /// <summary>The permissions on which two roles differ.</summary>
    Task<Result<IReadOnlyList<Core.Security.RoleDifference>>> CompareRolesAsync(long firstRoleId, long secondRoleId, CancellationToken ct = default);

    /// <summary>
    /// Adds, removes or sets operations on every permission a pattern covers (an exact code, or a prefix such as <c>Payroll.*</c>), across the
    /// given roles at once. System roles are refused.
    /// </summary>
    /// <returns>How many grants changed.</returns>
    Task<Result<int>> BulkGrantAsync(IReadOnlyList<long> roleIds, string codePattern, Core.Security.OperationFlags operations, Core.Security.BulkGrantMode mode, CancellationToken ct = default);
}
