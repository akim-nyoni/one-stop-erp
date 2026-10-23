using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Security;
using One4All.Core.Security;
using One4All.Infrastructure.Persistence;

namespace One4All.AccountingTests;

/// <summary>Users for the report tests: each with a role of its own granting the general report right (view and export) and any further rights, with optional scopes.</summary>
internal static class ReportUsers
{
    /// <summary>The acting user id the tests assign roles as: a fixed high id no created user shares.</summary>
    public const long Acting = 900_001;

    /// <summary>Creates a user who may run and export reports, plus the given rights, limited to the given scopes.</summary>
    /// <param name="services">The scope's services.</param>
    /// <param name="name">The login name prefix.</param>
    /// <param name="scopes">The scopes of the assignment (none: unrestricted).</param>
    /// <param name="rights">Further permission codes granted view and export.</param>
    /// <returns>The user id.</returns>
    public static async Task<long> CreateAsync(IServiceProvider services, string name, IReadOnlyDictionary<ScopeDimension, IReadOnlyList<string>>? scopes = null, params string[] rights)
    {
        IUserAdminService admin = services.GetRequiredService<IUserAdminService>();
        One4AllDbContext db = services.GetRequiredService<One4AllDbContext>();
        string tag = Guid.NewGuid().ToString("N")[..8];
        long user = (await admin.CreateUserAsync($"{name}.{tag}", name, null, "Reports2026x")).Value;
        long roleId = (await admin.CreateRoleAsync($"{name} {tag}", null)).Value;
        Role role = db.Roles.Single(r => r.Id == roleId);
        foreach (string right in rights.Prepend("Reporting.Report"))
        {
            role.Grant(right, OperationFlags.View | OperationFlags.Export);
        }

        await db.SaveChangesAsync();
        (await admin.AssignRoleAsync(user, roleId, Acting, scopes ?? new Dictionary<ScopeDimension, IReadOnlyList<string>>())).IsSuccess.Should().BeTrue();
        return user;
    }
}
