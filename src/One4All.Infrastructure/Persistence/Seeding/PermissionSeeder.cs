using Microsoft.EntityFrameworkCore;
using One4All.Core.Security;

namespace One4All.Infrastructure.Persistence.Seeding;

/// <summary>
/// Idempotently synchronises the <c>sec.permission</c> table with the code-defined
/// <see cref="PermissionCatalogue"/> (brief §18 item 3). Runs at startup/deployment: inserts new
/// permissions, refreshes changed names/available-flags, and leaves customer data untouched.
/// </summary>
public sealed class PermissionSeeder
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the seeder.</summary>
    /// <param name="db">The database context.</param>
    public PermissionSeeder(One4AllDbContext db) => _db = db;

    /// <summary>Synchronises the permission catalogue into the database.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of permissions inserted or updated.</returns>
    public async Task<int> SeedAsync(CancellationToken ct = default)
    {
        Dictionary<string, Permission> existing = await _db.Permissions
            .ToDictionaryAsync(p => p.Code, StringComparer.OrdinalIgnoreCase, ct)
            .ConfigureAwait(false);

        int changed = 0;
        foreach (PermissionDefinition def in PermissionCatalogue.All)
        {
            if (!existing.TryGetValue(def.Code, out Permission? permission))
            {
                _db.Permissions.Add(new Permission(def));
                changed++;
            }
            else if (!permission.MatchesDefinition(def))
            {
                permission.SyncWith(def);
                changed++;
            }
        }

        if (changed > 0)
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return changed;
    }
}
