using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Security;
using One4All.Application.Security;
using One4All.Core.Common;
using One4All.Core.Security;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Security;

/// <summary>User, role and group administration (brief §4.17, §5).</summary>
public sealed class UserAdminService : IUserAdminService
{
    private readonly One4AllDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IPermissionService _permissions;
    private readonly IAuditService? _audit;
    private readonly ICurrentUser? _currentUser;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="hasher">Hashes passwords.</param>
    /// <param name="permissions">Checks segregation of duties after changes.</param>
    /// <param name="audit">Records permission and account changes.</param>
    /// <param name="currentUser">Who is making the change.</param>
    public UserAdminService(One4AllDbContext db, IPasswordHasher hasher, IPermissionService permissions, IAuditService? audit = null, ICurrentUser? currentUser = null)
    {
        _audit = audit;
        _currentUser = currentUser;
        _db = db;
        _hasher = hasher;
        _permissions = permissions;
    }

    /// <inheritdoc />
    public async Task<Result<long>> CreateUserAsync(string username, string displayName, string? email, string initialPassword, CancellationToken ct = default)
    {
        string name = (username ?? string.Empty).Trim();
        if (name.Length is 0 or > 100 || string.IsNullOrWhiteSpace(displayName))
        {
            return Error.Validation("Security.UserInvalid", "A user needs a login name of up to 100 characters and a display name.");
        }

        if (PasswordPolicy.Problem(initialPassword, name) is string problem)
        {
            return Error.Validation("Security.PasswordPolicy", problem);
        }

        string normalized = name.ToUpperInvariant();
        if (await _db.Users.AnyAsync(u => u.NormalizedUsername == normalized, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Security.UserExists", $"User {name} already exists.");
        }

        User user = new(name, displayName.Trim(), string.IsNullOrWhiteSpace(email) ? null : email.Trim());
        user.SetPasswordHash(_hasher.Hash(initialPassword), mustChange: true);
        user.StampPasswordChange(DateTimeOffset.UtcNow);
        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return user.Id;
    }

    /// <inheritdoc />
    public async Task<Result> ResetPasswordAsync(long userId, string newPassword, CancellationToken ct = default)
    {
        User? user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct).ConfigureAwait(false);
        if (user is null)
        {
            return Result.Failure(Error.NotFound("Security.UserNotFound", $"User {userId} not found."));
        }

        if (PasswordPolicy.Problem(newPassword, user.Username) is string problem)
        {
            return Result.Failure(Error.Validation("Security.PasswordPolicy", problem));
        }

        if (user.PasswordHash is string previous)
        {
            _db.PasswordHistories.Add(new PasswordHistory(user.Id, previous, DateTimeOffset.UtcNow));
        }

        user.SetPasswordHash(_hasher.Hash(newPassword), mustChange: true);
        user.StampPasswordChange(DateTimeOffset.UtcNow);
        user.Unlock();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        await RecordAsync(Core.Audit.AuditAction.ConfigChange, "User", userId, "Password reset by an administrator", ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> SetActiveAsync(long userId, bool active, CancellationToken ct = default)
    {
        User? user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct).ConfigureAwait(false);
        if (user is null)
        {
            return Result.Failure(Error.NotFound("Security.UserNotFound", $"User {userId} not found."));
        }

        if (!active && user.IsSystemAdministrator && user.IsActive
            && !await _db.Users.AnyAsync(u => u.Id != userId && u.IsActive && u.IsSystemAdministrator, ct).ConfigureAwait(false))
        {
            return Result.Failure(Error.Rule("Security.LastAdministrator", "The last active system administrator cannot be deactivated."));
        }

        user.SetActive(active);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        await RecordAsync(Core.Audit.AuditAction.ConfigChange, "User", userId, active ? "Activated" : "Deactivated", ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<long>> CreateRoleAsync(string name, string? description, long? copyFromRoleId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation("Security.RoleInvalid", "A role needs a name.");
        }

        string normalized = name.Trim().ToUpperInvariant();
        if (await _db.Roles.AnyAsync(r => r.NormalizedName == normalized, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Security.RoleExists", $"Role {name.Trim()} already exists.");
        }

        Role role = new(name.Trim(), description);
        if (copyFromRoleId is long sourceId)
        {
            Role? source = await _db.Roles.AsNoTracking().Include(r => r.Permissions).FirstOrDefaultAsync(r => r.Id == sourceId, ct).ConfigureAwait(false);
            if (source is null)
            {
                return Error.NotFound("Security.RoleNotFound", $"Role {sourceId} not found.");
            }

            foreach (RolePermission grant in source.Permissions)
            {
                role.Grant(grant.PermissionCode, grant.Operations);
            }
        }

        _db.Roles.Add(role);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return role.Id;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<string>>> AssignRoleAsync(long userId, long roleId, IReadOnlyDictionary<ScopeDimension, IReadOnlyList<string>>? scopes = null, CancellationToken ct = default)
    {
        User? user = await _db.Users.Include(u => u.Roles).ThenInclude(r => r.Scopes).FirstOrDefaultAsync(u => u.Id == userId, ct).ConfigureAwait(false);
        if (user is null || !await _db.Roles.AnyAsync(r => r.Id == roleId, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Security.NotFound", "No such user or role.");
        }

        if (scopes?.Any(s => s.Value.Count == 0 || s.Value.Any(string.IsNullOrWhiteSpace)) == true)
        {
            return Error.Validation("Security.ScopeInvalid", "A scope lists at least one value, none blank.");
        }

        // Replace the assignment so its scopes are exactly those given.
        user.RemoveRole(roleId);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        UserRole assignment = user.AssignRole(roleId);
        foreach ((ScopeDimension dimension, IReadOnlyList<string> values) in scopes ?? new Dictionary<ScopeDimension, IReadOnlyList<string>>())
        {
            assignment.AddScope(new AccessScope(dimension, values.Select(v => v.Trim())));
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        await RecordAsync(Core.Audit.AuditAction.PermissionChange, "User", userId, $"Role {roleId} assigned{(scopes is { Count: > 0 } ? " with scopes " + string.Join("; ", scopes.Select(s => $"{s.Key}: {string.Join(",", s.Value)}")) : string.Empty)}", ct)
            .ConfigureAwait(false);
        return await ConflictsAsync(userId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result> RemoveRoleAsync(long userId, long roleId, CancellationToken ct = default)
    {
        User? user = await _db.Users.Include(u => u.Roles).FirstOrDefaultAsync(u => u.Id == userId, ct).ConfigureAwait(false);
        if (user is null)
        {
            return Result.Failure(Error.NotFound("Security.UserNotFound", $"User {userId} not found."));
        }

        user.RemoveRole(roleId);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        await RecordAsync(Core.Audit.AuditAction.PermissionChange, "User", userId, $"Role {roleId} removed", ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<long>> CreateGroupAsync(string name, string? description, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation("Security.GroupInvalid", "A group needs a name.");
        }

        string normalized = name.Trim().ToUpperInvariant();
        if (await _db.Groups.AnyAsync(g => g.NormalizedName == normalized, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Security.GroupExists", $"Group {name.Trim()} already exists.");
        }

        Group group = new(name.Trim(), description);
        _db.Groups.Add(group);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return group.Id;
    }

    /// <inheritdoc />
    public async Task<Result> AddRoleToGroupAsync(long groupId, long roleId, CancellationToken ct = default)
    {
        Group? group = await _db.Groups.Include(g => g.Roles).FirstOrDefaultAsync(g => g.Id == groupId, ct).ConfigureAwait(false);
        if (group is null || !await _db.Roles.AnyAsync(r => r.Id == roleId, ct).ConfigureAwait(false))
        {
            return Result.Failure(Error.NotFound("Security.NotFound", "No such group or role."));
        }

        group.AddRole(roleId);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<string>>> SetGroupMembershipAsync(long userId, long groupId, bool member, CancellationToken ct = default)
    {
        User? user = await _db.Users.Include(u => u.Groups).FirstOrDefaultAsync(u => u.Id == userId, ct).ConfigureAwait(false);
        if (user is null || !await _db.Groups.AnyAsync(g => g.Id == groupId, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Security.NotFound", "No such user or group.");
        }

        if (member)
        {
            user.JoinGroup(groupId);
        }
        else
        {
            user.LeaveGroup(groupId);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        await RecordAsync(Core.Audit.AuditAction.PermissionChange, "User", userId, $"{(member ? "Joined" : "Left")} group {groupId}", ct).ConfigureAwait(false);
        return await ConflictsAsync(userId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result> SetManagerAsync(long userId, long? managerUserId, CancellationToken ct = default)
    {
        User? user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct).ConfigureAwait(false);
        if (user is null || (managerUserId is long m && (m == userId || !await _db.Users.AnyAsync(u => u.Id == m, ct).ConfigureAwait(false))))
        {
            return Result.Failure(Error.Validation("Security.ManagerInvalid", "The user and manager must exist and be different people."));
        }

        user.ManagerUserId = managerUserId;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    private async Task<Result<IReadOnlyList<string>>> ConflictsAsync(long userId, CancellationToken ct)
    {
        List<string> names = [.. (await _permissions.GetSodViolationsAsync(userId, ct).ConfigureAwait(false)).Select(r => r.Name)];
        return names;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<RoleDifference>>> CompareRolesAsync(long firstRoleId, long secondRoleId, CancellationToken ct = default)
    {
        List<Role> roles = await _db.Roles.AsNoTracking().Include(r => r.Permissions).Where(r => r.Id == firstRoleId || r.Id == secondRoleId).ToListAsync(ct).ConfigureAwait(false);
        if (roles.Find(r => r.Id == firstRoleId) is not Role first || roles.Find(r => r.Id == secondRoleId) is not Role second)
        {
            return Error.NotFound("Security.RoleNotFound", "Both roles must exist to compare them.");
        }

        static Dictionary<string, OperationFlags> Grants(Role role) => role.Permissions.ToDictionary(p => p.PermissionCode, p => p.Operations, StringComparer.OrdinalIgnoreCase);
        return Result<IReadOnlyList<RoleDifference>>.Success(RoleComparison.Compare(Grants(first), Grants(second)));
    }

    /// <inheritdoc />
    public async Task<Result<int>> BulkGrantAsync(IReadOnlyList<long> roleIds, string codePattern, OperationFlags operations, BulkGrantMode mode, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(roleIds);
        List<Role> roles = await _db.Roles.Include(r => r.Permissions).Where(r => roleIds.Contains(r.Id)).ToListAsync(ct).ConfigureAwait(false);
        if (roles.Count == 0 || roles.Count != roleIds.Distinct().Count())
        {
            return Error.NotFound("Security.RoleNotFound", "Every role in a bulk change must exist.");
        }

        if (roles.Find(r => r.IsSystemRole) is Role system)
        {
            return Error.Rule("Security.SystemRole", $"{system.Name} is a system role; copy it to a role of your own to change it.");
        }

        IReadOnlyList<string> codes = RoleComparison.Matching(codePattern, PermissionCatalogue.All.Select(p => p.Code).Concat(roles.SelectMany(r => r.Permissions.Select(p => p.PermissionCode))));
        if (codes.Count == 0 || (operations == OperationFlags.None && mode != BulkGrantMode.Set))
        {
            return Error.Validation("Security.BulkGrant", "A bulk change needs a permission code or pattern that matches, and the operations to change.");
        }

        int changed = 0;
        foreach (Role role in roles)
        {
            foreach (string code in codes)
            {
                OperationFlags current = role.Permissions.FirstOrDefault(p => string.Equals(p.PermissionCode, code, StringComparison.OrdinalIgnoreCase))?.Operations ?? OperationFlags.None;
                OperationFlags next = RoleComparison.Apply(current, operations, mode);
                if (next != current)
                {
                    role.Grant(code, next);
                    changed++;
                }
            }

            await RecordAsync(Core.Audit.AuditAction.Update, "Role", role.Id, $"Bulk {mode} {operations} on {codePattern.Trim()}", ct).ConfigureAwait(false);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return changed;
    }

    // A permission or account change in the change log, by whoever is signed in.
    private async Task RecordAsync(Core.Audit.AuditAction action, string entityType, long entityId, string what, CancellationToken ct)
    {
        if (_audit is not null)
        {
            await _audit.RecordAsync(new AuditEvent(null, _currentUser?.UserId, _currentUser?.UserName ?? "system", "Security", entityType,
                entityId.ToString(System.Globalization.CultureInfo.InvariantCulture), action, what), ct).ConfigureAwait(false);
        }
    }
}
