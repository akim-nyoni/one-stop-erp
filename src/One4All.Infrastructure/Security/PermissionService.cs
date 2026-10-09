using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Security;
using One4All.Core.Security;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Security;

/// <summary>
/// Resolves a user's effective permissions from the database: direct role assignments plus roles
/// inherited through group membership, unioned, then adjusted by the user's grant/deny overrides
/// (deny wins). Implemented with set-based queries so it stays fast as the permission set grows.
/// </summary>
public sealed class PermissionService : IPermissionService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public PermissionService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<EffectivePermissions> ResolveAsync(long userId, CancellationToken ct = default)
    {
        User? user = await _db.Users
            .Include(u => u.Roles)
            .Include(u => u.Groups)
            .Include(u => u.Overrides)
            .FirstOrDefaultAsync(u => u.Id == userId, ct)
            .ConfigureAwait(false);

        if (user is null || !user.IsActive)
        {
            return EffectivePermissions.None;
        }

        List<PermissionGrant> grants = await GrantsAsync(user, ct).ConfigureAwait(false);
        IEnumerable<PermissionOverride> overrides = user.Overrides.Select(o => o.ToOverride());

        return PermissionResolution.Resolve(grants, overrides, user.IsSystemAdministrator);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PermissionGrant>> RoleGrantsAsync(long userId, CancellationToken ct = default)
    {
        User? user = await _db.Users.Include(u => u.Roles).Include(u => u.Groups).AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct).ConfigureAwait(false);
        return user is null ? [] : await GrantsAsync(user, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Core.Common.Result> SetOverridesAsync(long userId, IReadOnlyList<UserOverrideEdit> overrides, CancellationToken ct = default)
    {
        User? user = await _db.Users.Include(u => u.Overrides).FirstOrDefaultAsync(u => u.Id == userId, ct).ConfigureAwait(false);
        if (user is null)
        {
            return Core.Common.Result.Failure(Core.Common.Error.NotFound("Perm.User", "User not found."));
        }

        foreach (UserOverrideEdit edit in overrides)
        {
            PermissionDefinition? feature = PermissionCatalogue.Find(edit.PermissionCode);
            if (feature is null)
            {
                return Core.Common.Result.Failure(Core.Common.Error.Validation("Perm.Feature", $"Unknown feature {edit.PermissionCode}."));
            }

            user.SetOverride(feature.Code, edit.Grant & feature.Available, isGrant: true);
            user.SetOverride(feature.Code, edit.Deny & feature.Available, isGrant: false);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Core.Common.Result.Success();
    }

    // The grants of the user's roles, directly assigned and through group membership.
    private async Task<List<PermissionGrant>> GrantsAsync(User user, CancellationToken ct)
    {
        // Role ids from direct assignments and from group membership.
        HashSet<long> roleIds = user.Roles.Select(r => r.RoleId).ToHashSet();

        long[] groupIds = user.Groups.Select(g => g.GroupId).ToArray();
        if (groupIds.Length > 0)
        {
            List<long> groupRoleIds = await _db.Set<GroupRole>()
                .Where(gr => groupIds.Contains(gr.GroupId))
                .Select(gr => gr.RoleId)
                .ToListAsync(ct)
                .ConfigureAwait(false);
            roleIds.UnionWith(groupRoleIds);
        }

        List<PermissionGrant> grants = [];
        if (roleIds.Count > 0)
        {
            grants = await _db.Set<RolePermission>()
                .Where(rp => roleIds.Contains(rp.RoleId))
                .Select(rp => new PermissionGrant(rp.PermissionCode, rp.Operations))
                .ToListAsync(ct)
                .ConfigureAwait(false);
        }

        return grants;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SodRule>> GetSodViolationsAsync(long userId, CancellationToken ct = default)
    {
        EffectivePermissions permissions = await ResolveAsync(userId, ct).ConfigureAwait(false);
        return SegregationOfDutiesCatalogue.Violations(permissions).ToList();
    }
}
