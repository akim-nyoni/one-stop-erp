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
    public Task<Result<RoleAssignment>> AssignRoleAsync(long userId, long roleId, long actingUserId, IReadOnlyDictionary<ScopeDimension, IReadOnlyList<string>>? scopes = null,
        CancellationToken ct = default)
        => _db.AtomicAsync(() => AssignCoreAsync(userId, roleId, actingUserId, scopes, ct), ct);

    private async Task<Result<RoleAssignment>> AssignCoreAsync(long userId, long roleId, long actingUserId, IReadOnlyDictionary<ScopeDimension, IReadOnlyList<string>>? scopes,
        CancellationToken ct)
    {
        if (actingUserId == userId)
        {
            return Error.Rule("Sec.SelfGrant", "Nobody grants a role to themselves; another administrator must assign it.");
        }

        User? user = await UserWithAccessAsync(userId, ct).ConfigureAwait(false);
        string? roleName = await _db.Roles.Where(r => r.Id == roleId).Select(r => r.Name).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (user is null || roleName is null)
        {
            return Error.NotFound("Security.NotFound", "No such user or role.");
        }

        if (scopes?.Any(s => s.Value.Count == 0 || s.Value.Any(string.IsNullOrWhiteSpace)) == true)
        {
            return Error.Validation("Security.ScopeInvalid", "A scope lists at least one value, none blank.");
        }

        // A role that would break a segregation-of-duties rule the user does not already break waits for a second administrator.
        List<long> current = [.. user.Roles.Select(r => r.RoleId)];
        EffectivePermissions before = await EffectiveAsync(user, current, ct).ConfigureAwait(false);
        EffectivePermissions after = await EffectiveAsync(user, current.Append(roleId), ct).ConfigureAwait(false);
        List<string> fresh = [.. (await _permissions.SodRulesAsync(ct).ConfigureAwait(false)).Where(r => r.IsViolatedBy(after) && !r.IsViolatedBy(before)).Select(r => r.Name)];
        if (fresh.Count > 0)
        {
            if (await _db.Set<RoleGrantRequest>().AnyAsync(r => r.UserId == userId && r.RoleId == roleId && r.Status == RoleGrantStatus.Pending, ct).ConfigureAwait(false))
            {
                return Error.Conflict("Sec.GrantPending", $"Role {roleName} for {user.Username} is already waiting for a second administrator.");
            }

            RoleGrantRequest held = new(userId, roleId, FormatScopes(scopes), string.Join("; ", fresh), actingUserId, DateTimeOffset.UtcNow);
            _db.Set<RoleGrantRequest>().Add(held);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            await RecordAsync(Core.Audit.AuditAction.PermissionChange, "User", userId,
                $"Role {roleName} held for a second administrator (request {held.Id}): it would break {held.Conflicts}", ct, actingUserId).ConfigureAwait(false);
            return new RoleAssignment(false, held.Id, fresh);
        }

        await GrantAsync(user, roleId, scopes, ct).ConfigureAwait(false);
        await RecordAsync(Core.Audit.AuditAction.PermissionChange, "User", userId, $"Role {roleId} assigned ({roleName}){DescribeScopes(scopes)}", ct, actingUserId).ConfigureAwait(false);
        return new RoleAssignment(true, null, (await ConflictsAsync(userId, ct).ConfigureAwait(false)).Value);
    }

    /// <inheritdoc />
    public Task<Result> RemoveRoleAsync(long userId, long roleId, long actingUserId, CancellationToken ct = default)
        => _db.AtomicAsync(async () =>
        {
            User? user = await UserWithAccessAsync(userId, ct).ConfigureAwait(false);
            if (user is null)
            {
                return Result.Failure(Error.NotFound("Security.UserNotFound", $"User {userId} not found."));
            }

            if (actingUserId == userId && !user.IsSystemAdministrator && user.Roles.Any(r => r.RoleId == roleId))
            {
                List<long> current = [.. user.Roles.Select(r => r.RoleId)];
                bool hasRight = (await EffectiveAsync(user, current, ct).ConfigureAwait(false)).Has(UserAdministrationRight, OperationFlags.Edit);
                bool keepsRight = (await EffectiveAsync(user, current.Where(r => r != roleId), ct).ConfigureAwait(false)).Has(UserAdministrationRight, OperationFlags.Edit);
                if (hasRight && !keepsRight)
                {
                    return Result.Failure(Error.Rule("Sec.LastAdminRole",
                        "That is your last role with the user-administration right; another administrator must remove it, so nobody locks themselves out by mistake."));
                }
            }

            user.RemoveRole(roleId);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            await RecordAsync(Core.Audit.AuditAction.PermissionChange, "User", userId, $"Role {roleId} removed", ct, actingUserId).ConfigureAwait(false);
            return Result.Success();
        }, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<PendingRoleGrant>> PendingRoleGrantsAsync(CancellationToken ct = default)
        => await (
            from r in _db.Set<RoleGrantRequest>().AsNoTracking()
            join u in _db.Users.AsNoTracking() on r.UserId equals u.Id
            join ro in _db.Roles.AsNoTracking() on r.RoleId equals ro.Id
            join q in _db.Users.AsNoTracking() on r.RequestedByUserId equals q.Id into requesters
            from q in requesters.DefaultIfEmpty()
            where r.Status == RoleGrantStatus.Pending
            orderby r.RequestedOnUtc, r.Id
            select new PendingRoleGrant(r.Id, u.Username, ro.Name, r.Scopes, r.Conflicts, q == null ? string.Empty : q.Username, r.RequestedOnUtc))
            .ToListAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public Task<Result> DecideRoleGrantAsync(long requestId, bool approve, long actingUserId, string? note, CancellationToken ct = default)
        => _db.AtomicAsync(async () =>
        {
            RoleGrantRequest? request = await _db.Set<RoleGrantRequest>().FirstOrDefaultAsync(r => r.Id == requestId, ct).ConfigureAwait(false);
            if (request is null)
            {
                return Result.Failure(Error.NotFound("Sec.GrantNotFound", $"Held role grant {requestId} not found."));
            }

            if (request.Status != RoleGrantStatus.Pending)
            {
                return Result.Failure(Error.Rule("Sec.GrantDecided", $"Held role grant {requestId} has already been decided."));
            }

            if (actingUserId == request.RequestedByUserId || actingUserId == request.UserId)
            {
                return Result.Failure(Error.Rule("Sec.SecondAdministrator",
                    "A second administrator decides a held role grant: not the one who asked for it, nor the user it is for."));
            }

            if (!(await _permissions.ResolveAsync(actingUserId, ct).ConfigureAwait(false)).Has(UserAdministrationRight, OperationFlags.Edit))
            {
                return Result.Failure(Error.Rule("Sec.NotAdministrator", "Only a user with the user-administration right (Roles, Edit) decides a held role grant."));
            }

            if (!approve && string.IsNullOrWhiteSpace(note))
            {
                return Result.Failure(Error.Validation("Sec.RejectReason", "Say why the role grant is rejected."));
            }

            string? roleName = await _db.Roles.Where(r => r.Id == request.RoleId).Select(r => r.Name).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            request.Decide(approve, actingUserId, DateTimeOffset.UtcNow, string.IsNullOrWhiteSpace(note) ? null : note.Trim());
            if (approve)
            {
                User? user = await UserWithAccessAsync(request.UserId, ct).ConfigureAwait(false);
                if (user is null || roleName is null)
                {
                    return Result.Failure(Error.NotFound("Security.NotFound", "The user or role of that grant no longer exists."));
                }

                await GrantAsync(user, request.RoleId, ParseScopes(request.Scopes), ct).ConfigureAwait(false);
            }

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            await RecordAsync(Core.Audit.AuditAction.PermissionChange, "User", request.UserId,
                approve
                    ? $"Role {request.RoleId} assigned ({roleName}) on approval of held grant {requestId} despite {request.Conflicts}{(request.Scopes is null ? string.Empty : " with scopes " + request.Scopes)}"
                    : $"Held grant {requestId} of role {roleName} rejected: {request.DecisionNote}", ct, actingUserId).ConfigureAwait(false);
            return Result.Success();
        }, ct);

    // The feature whose Edit right makes a user an administrator of users and roles.
    private const string UserAdministrationRight = "Security.Role";

    private Task<User?> UserWithAccessAsync(long userId, CancellationToken ct)
        => _db.Users.Include(u => u.Roles).ThenInclude(r => r.Scopes).Include(u => u.Groups).Include(u => u.Overrides).AsSplitQuery()
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

    // Replaces the assignment so its scopes are exactly those given (the removal is saved first so the new row does not clash with the old).
    private async Task GrantAsync(User user, long roleId, IReadOnlyDictionary<ScopeDimension, IReadOnlyList<string>>? scopes, CancellationToken ct)
    {
        user.RemoveRole(roleId);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        UserRole assignment = user.AssignRole(roleId);
        foreach ((ScopeDimension dimension, IReadOnlyList<string> values) in scopes ?? new Dictionary<ScopeDimension, IReadOnlyList<string>>())
        {
            assignment.AddScope(new AccessScope(dimension, values.Select(v => v.Trim())));
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    // What a user could do holding exactly these direct roles, with their groups' roles and their overrides.
    private async Task<EffectivePermissions> EffectiveAsync(User user, IEnumerable<long> roleIds, CancellationToken ct)
    {
        HashSet<long> ids = [.. roleIds];
        long[] groupIds = [.. user.Groups.Select(g => g.GroupId)];
        if (groupIds.Length > 0)
        {
            ids.UnionWith(await _db.Set<GroupRole>().Where(g => groupIds.Contains(g.GroupId)).Select(g => g.RoleId).ToListAsync(ct).ConfigureAwait(false));
        }

        List<PermissionGrant> grants = ids.Count == 0 ? []
            : await _db.Set<RolePermission>().Where(p => ids.Contains(p.RoleId)).Select(p => new PermissionGrant(p.PermissionCode, p.Operations)).ToListAsync(ct).ConfigureAwait(false);
        return PermissionResolution.Resolve(grants, user.Overrides.Select(o => o.ToOverride()), user.IsSystemAdministrator);
    }

    private static string DescribeScopes(IReadOnlyDictionary<ScopeDimension, IReadOnlyList<string>>? scopes)
        => FormatScopes(scopes) is string text ? " with scopes " + text : string.Empty;

    // Scopes as Dimension:value,value;Dimension:value - the form the desktop takes them in.
    private static string? FormatScopes(IReadOnlyDictionary<ScopeDimension, IReadOnlyList<string>>? scopes)
        => scopes is { Count: > 0 } ? string.Join(";", scopes.Select(s => $"{s.Key}:{string.Join(",", s.Value.Select(v => v.Trim()))}")) : null;

    private static Dictionary<ScopeDimension, IReadOnlyList<string>>? ParseScopes(string? text)
        => string.IsNullOrWhiteSpace(text) ? null
            : text.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(part => part.Split(':', 2))
                .ToDictionary(bits => Enum.Parse<ScopeDimension>(bits[0]), bits => (IReadOnlyList<string>)[.. bits[1].Split(',', StringSplitOptions.RemoveEmptyEntries)]);

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

    /// <inheritdoc />
    public async Task<Result> SetCashierCardAsync(long userId, string? cardRead, CancellationToken ct = default)
    {
        if (await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct).ConfigureAwait(false) is not User user)
        {
            return Result.Failure(Error.NotFound("Security.UserNotFound", $"User {userId} not found."));
        }

        string? hash = null;
        if (!string.IsNullOrWhiteSpace(cardRead))
        {
            hash = CashierCards.Hash(cardRead);
            if (hash.Length == 0)
            {
                return Result.Failure(Error.Validation("Security.CardUnreadable", "No card number could be read from that."));
            }

            if (await _db.Users.AnyAsync(u => u.Id != userId && u.CardHash == hash, ct).ConfigureAwait(false))
            {
                return Result.Failure(Error.Conflict("Security.CardInUse", "That card already belongs to another user."));
            }
        }

        user.SetCardHash(hash);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        await RecordAsync(Core.Audit.AuditAction.Update, "User", userId, hash is null ? "Cashier card removed" : "Cashier card issued", ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> SetBiometricIdAsync(long userId, string? biometricId, CancellationToken ct = default)
    {
        if (await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct).ConfigureAwait(false) is not User user)
        {
            return Result.Failure(Error.NotFound("Security.UserNotFound", $"User {userId} not found."));
        }

        string? id = string.IsNullOrWhiteSpace(biometricId) ? null : biometricId.Trim();
        if (id is not null && await _db.Users.AnyAsync(u => u.Id != userId && u.BiometricId == id, ct).ConfigureAwait(false))
        {
            return Result.Failure(Error.Conflict("Security.BiometricInUse", "That fingerprint enrolment already belongs to another user."));
        }

        user.SetBiometricId(id);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        await RecordAsync(Core.Audit.AuditAction.Update, "User", userId, id is null ? "Fingerprint enrolment removed" : "Fingerprint enrolled", ct).ConfigureAwait(false);
        return Result.Success();
    }

    // A permission or account change in the change log, by the acting administrator when given, else whoever is signed in.
    private async Task RecordAsync(Core.Audit.AuditAction action, string entityType, long entityId, string what, CancellationToken ct, long? actingUserId = null)
    {
        if (_audit is not null)
        {
            long? who = actingUserId ?? _currentUser?.UserId;
            string name = actingUserId is long acting && acting != _currentUser?.UserId
                ? await _db.Users.AsNoTracking().Where(u => u.Id == acting).Select(u => u.Username).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? $"user {acting}"
                : _currentUser?.UserName ?? "system";
            await _audit.RecordAsync(new AuditEvent(null, who, name, "Security", entityType,
                entityId.ToString(System.Globalization.CultureInfo.InvariantCulture), action, what), ct).ConfigureAwait(false);
        }
    }
}
