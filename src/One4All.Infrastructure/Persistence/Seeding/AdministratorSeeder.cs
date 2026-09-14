using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Security;
using One4All.Core.Security;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Persistence.Seeding;

/// <summary>
/// Ensures a built-in <c>System Administrator</c> role and an <c>admin</c> user exist so a fresh
/// deployment can be signed into. The admin is created with a default password that must be changed on
/// first login. Idempotent: does nothing if the admin already exists.
/// </summary>
public sealed class AdministratorSeeder
{
    /// <summary>The default administrator login name.</summary>
    public const string AdminUsername = "admin";

    /// <summary>The default administrator password (must be changed at first login).</summary>
    public const string DefaultPassword = "Admin@123";

    private readonly One4AllDbContext _db;
    private readonly IPasswordHasher _hasher;

    /// <summary>Creates the seeder.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="hasher">The password hasher.</param>
    public AdministratorSeeder(One4AllDbContext db, IPasswordHasher hasher)
    {
        _db = db;
        _hasher = hasher;
    }

    /// <summary>Ensures the administrator role and user exist.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True when the admin user was created; false if it already existed.</returns>
    public async Task<bool> SeedAsync(CancellationToken ct = default)
    {
        Role adminRole = await EnsureAdminRoleAsync(ct).ConfigureAwait(false);

        bool exists = await _db.Users.AnyAsync(u => u.NormalizedUsername == "ADMIN", ct).ConfigureAwait(false);
        if (exists)
        {
            return false;
        }

        User admin = new(AdminUsername, "System Administrator");
        admin.SetSystemAdministrator(true);
        admin.SetPasswordHash(_hasher.Hash(DefaultPassword), mustChange: true);
        admin.AssignRole(adminRole.Id);

        _db.Users.Add(admin);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    private async Task<Role> EnsureAdminRoleAsync(CancellationToken ct)
    {
        Role? role = await _db.Roles
            .FirstOrDefaultAsync(r => r.NormalizedName == "SYSTEM ADMINISTRATOR", ct)
            .ConfigureAwait(false);
        if (role is not null)
        {
            return role;
        }

        role = new Role("System Administrator", "Full access to every module and feature.", isSystemRole: true);

        // Grant every catalogue permission's full available operation set (belt-and-braces; the
        // system-administrator flag already bypasses checks, but an explicit grant is auditable).
        foreach (PermissionDefinition def in PermissionCatalogue.All)
        {
            role.Grant(def.Code, def.Available);
        }

        _db.Roles.Add(role);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return role;
    }
}
