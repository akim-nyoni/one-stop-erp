using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Security;
using One4All.Core.Security;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers user administration: policy-checked passwords, a reset that forces a change and lifts a lockout, roles copied from others,
/// scoped role assignments, groups, and segregation-of-duties warnings when one user gains both sides of a conflict.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class UserAdminTests
{
    private readonly AccountingFixture _fixture;

    public UserAdminTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Users_roles_and_groups_are_administered()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        IUserAdminService admin = scope.ServiceProvider.GetRequiredService<IUserAdminService>();
        IAuthenticationService auth = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
        string tag = Guid.NewGuid().ToString("N")[..8];
        string login = $"ua.{tag}";

        (await admin.CreateUserAsync(login, "Clerk", null, "short1")).Error.Code.Should().Be("Security.PasswordPolicy");
        (await admin.CreateUserAsync(login, "Clerk", null, $"x{login}9")).Error.Code.Should().Be("Security.PasswordPolicy");
        long userId = (await admin.CreateUserAsync(login, "Clerk", "clerk@example.com", "Spring2026a")).Value;
        (await admin.CreateUserAsync(login.ToUpperInvariant(), "Again", null, "Spring2026a")).Error.Code.Should().Be("Security.UserExists");
        AuthenticationResult first = await auth.AuthenticateAsync(login, "Spring2026a");
        (first.Succeeded, first.MustChangePassword).Should().Be((true, true));
        (await auth.ChangePasswordAsync(userId, "Spring2026a", "weak")).Should().BeFalse();
        (await auth.ChangePasswordAsync(userId, "Spring2026a", "Summer2026b")).Should().BeTrue();

        // Locked out by failed attempts, then reset by an administrator.
        for (int n = 0; n < 6; n++)
        {
            await auth.AuthenticateAsync(login, "wrong-password1");
        }

        (await auth.AuthenticateAsync(login, "Summer2026b")).Outcome.Should().Be(AuthenticationOutcome.LockedOut);
        (await admin.ResetPasswordAsync(userId, "Autumn2026c")).IsSuccess.Should().BeTrue();
        AuthenticationResult afterReset = await auth.AuthenticateAsync(login, "Autumn2026c");
        (afterReset.Succeeded, afterReset.MustChangePassword).Should().Be((true, true));

        // A creditors role and a payments role: together they break SOD-AP-01.
        long creditors = (await admin.CreateRoleAsync($"Creditors {tag}", "Maintains suppliers")).Value;
        db.Roles.Single(r => r.Id == creditors).Grant("Purchasing.Supplier", OperationFlags.View | OperationFlags.Create);
        await db.SaveChangesAsync();
        long copy = (await admin.CreateRoleAsync($"Creditors copy {tag}", null, creditors)).Value;
        db.Roles.AsNoTracking().Include(r => r.Permissions).Single(r => r.Id == copy).Permissions.Should().ContainSingle(p => p.PermissionCode == "Purchasing.Supplier");
        (await admin.CreateRoleAsync($"creditors {tag}", null)).Error.Code.Should().Be("Security.RoleExists");
        long payments = (await admin.CreateRoleAsync($"Payments {tag}", null)).Value;
        db.Roles.Single(r => r.Id == payments).Grant("Purchasing.PaymentRun", OperationFlags.View | OperationFlags.Approve);
        await db.SaveChangesAsync();

        (await admin.AssignRoleAsync(userId, creditors, new Dictionary<ScopeDimension, IReadOnlyList<string>> { [ScopeDimension.Branch] = ["HO", "NORTH"] })).Value.Should().BeEmpty();
        long group = (await admin.CreateGroupAsync($"Treasury {tag}", null)).Value;
        (await admin.AddRoleToGroupAsync(group, payments)).IsSuccess.Should().BeTrue();
        (await admin.SetGroupMembershipAsync(userId, group, true)).Value.Should().Contain("Create supplier and approve payment run");
        (await admin.SetGroupMembershipAsync(userId, group, false)).Value.Should().BeEmpty();

        // Re-assigning replaces the scopes.
        (await admin.AssignRoleAsync(userId, creditors, new Dictionary<ScopeDimension, IReadOnlyList<string>> { [ScopeDimension.Branch] = ["SOUTH"] })).IsSuccess.Should().BeTrue();
        UserRole assignment = db.Users.AsNoTracking().Include(u => u.Roles).ThenInclude(r => r.Scopes).Single(u => u.Id == userId).Roles.Single();
        assignment.Scopes.Single().Values.Should().Equal("SOUTH");

        (await admin.SetActiveAsync(userId, false)).IsSuccess.Should().BeTrue();
        (await auth.AuthenticateAsync(login, "Autumn2026c")).Outcome.Should().Be(AuthenticationOutcome.Inactive);
    }
}
