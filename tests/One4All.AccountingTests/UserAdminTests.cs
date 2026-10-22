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

        (await admin.AssignRoleAsync(userId, creditors, 900_001, new Dictionary<ScopeDimension, IReadOnlyList<string>> { [ScopeDimension.Branch] = ["HO", "NORTH"] })).Value.Conflicts.Should().BeEmpty();
        long group = (await admin.CreateGroupAsync($"Treasury {tag}", null)).Value;
        (await admin.AddRoleToGroupAsync(group, payments)).IsSuccess.Should().BeTrue();
        (await admin.SetGroupMembershipAsync(userId, group, true)).Value.Should().Contain("Create supplier and approve payment run");
        (await admin.SetGroupMembershipAsync(userId, group, false)).Value.Should().BeEmpty();

        // Re-assigning replaces the scopes.
        (await admin.AssignRoleAsync(userId, creditors, 900_001, new Dictionary<ScopeDimension, IReadOnlyList<string>> { [ScopeDimension.Branch] = ["SOUTH"] })).IsSuccess.Should().BeTrue();
        UserRole assignment = db.Users.AsNoTracking().Include(u => u.Roles).ThenInclude(r => r.Scopes).Single(u => u.Id == userId).Roles.Single();
        assignment.Scopes.Single().Values.Should().Equal("SOUTH");

        (await admin.SetActiveAsync(userId, false)).IsSuccess.Should().BeTrue();
        (await auth.AuthenticateAsync(login, "Autumn2026c")).Outcome.Should().Be(AuthenticationOutcome.Inactive);
    }

    [SkippableFact]
    public async Task Nobody_grants_themselves_a_role_and_a_conflicting_grant_waits_for_a_second_administrator()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        IUserAdminService admin = scope.ServiceProvider.GetRequiredService<IUserAdminService>();
        string tag = Guid.NewGuid().ToString("N")[..8];
        DateTimeOffset start = DateTimeOffset.UtcNow.AddSeconds(-1);

        long clerk = (await admin.CreateUserAsync($"sod.{tag}", "Clerk", null, "Spring2026a")).Value;
        long asker = (await admin.CreateUserAsync($"adm1.{tag}", "First administrator", null, "Spring2026a")).Value;
        long second = (await admin.CreateUserAsync($"adm2.{tag}", "Second administrator", null, "Spring2026a")).Value;
        long admins = (await admin.CreateRoleAsync($"User admins {tag}", null)).Value;
        long creditors = (await admin.CreateRoleAsync($"Creditors {tag}", null)).Value;
        long payments = (await admin.CreateRoleAsync($"Payments {tag}", null)).Value;
        db.Roles.Single(r => r.Id == admins).Grant("Security.Role", OperationFlags.View | OperationFlags.Create | OperationFlags.Edit);
        db.Roles.Single(r => r.Id == creditors).Grant("Purchasing.Supplier", OperationFlags.View | OperationFlags.Create);
        db.Roles.Single(r => r.Id == payments).Grant("Purchasing.PaymentRun", OperationFlags.View | OperationFlags.Approve);
        await db.SaveChangesAsync();
        (await admin.AssignRoleAsync(asker, admins, 900_002)).Value.Granted.Should().BeTrue();
        (await admin.AssignRoleAsync(second, admins, 900_002)).Value.Granted.Should().BeTrue();

        // Nobody grants a role to themselves.
        (await admin.AssignRoleAsync(asker, creditors, asker)).Error.Code.Should().Be("Sec.SelfGrant");

        // A role with no new conflict is granted at once, and the change log names who granted it.
        RoleAssignment plain = (await admin.AssignRoleAsync(clerk, creditors, asker)).Value;
        (plain.Granted, plain.RequestId, plain.Conflicts.Count).Should().Be((true, (long?)null, 0));
        (await db.ChangeLog.AsNoTracking().Where(c => c.OccurredOnUtc >= start && c.EntityId == clerk.ToString() && c.Action == Core.Audit.AuditAction.PermissionChange).ToListAsync())
            .Should().Contain(c => c.UserId == asker && c.UserName == $"adm1.{tag}" && c.Reason!.Contains($"Role {creditors} assigned"));

        // Payments on top of creditors breaks a segregation-of-duties rule: held, the clerk's roles unchanged.
        RoleAssignment held = (await admin.AssignRoleAsync(clerk, payments, asker)).Value;
        held.Granted.Should().BeFalse();
        held.Conflicts.Should().Contain("Create supplier and approve payment run");
        long request = held.RequestId!.Value;
        RolesOf(clerk).Should().Equal(creditors);
        (await admin.AssignRoleAsync(clerk, payments, asker)).Error.Code.Should().Be("Sec.GrantPending");
        (await admin.PendingRoleGrantsAsync()).Should().ContainSingle(p => p.RequestId == request)
            .Which.Should().Match<PendingRoleGrant>(p => p.Username == $"sod.{tag}" && p.RoleName == $"Payments {tag}" && p.RequestedBy == $"adm1.{tag}");

        // Decided by a second administrator only: not the one who asked, not the user, not someone without the right.
        (await admin.DecideRoleGrantAsync(request, true, asker, null)).Error.Code.Should().Be("Sec.SecondAdministrator");
        (await admin.DecideRoleGrantAsync(request, true, clerk, null)).Error.Code.Should().Be("Sec.SecondAdministrator");
        (await admin.DecideRoleGrantAsync(request, true, 900_003, null)).Error.Code.Should().Be("Sec.NotAdministrator");
        (await admin.DecideRoleGrantAsync(request, true, second, "Small branch, compensating review")).IsSuccess.Should().BeTrue();
        RolesOf(clerk).Should().BeEquivalentTo([creditors, payments]);
        (await admin.DecideRoleGrantAsync(request, false, second, "Again")).Error.Code.Should().Be("Sec.GrantDecided");
        (await admin.PendingRoleGrantsAsync()).Should().NotContain(p => p.RequestId == request);

        // A rejection leaves the roles as they were and needs its reason.
        (await admin.RemoveRoleAsync(clerk, payments, second)).IsSuccess.Should().BeTrue();
        long rejected = (await admin.AssignRoleAsync(clerk, payments, asker)).Value.RequestId!.Value;
        (await admin.DecideRoleGrantAsync(rejected, false, second, " ")).Error.Code.Should().Be("Sec.RejectReason");
        (await admin.DecideRoleGrantAsync(rejected, false, second, "Not without a compensating control")).IsSuccess.Should().BeTrue();
        RolesOf(clerk).Should().Equal(creditors);
        (await db.Set<RoleGrantRequest>().AsNoTracking().SingleAsync(r => r.Id == rejected)).Should().Match<RoleGrantRequest>(r =>
            r.Status == RoleGrantStatus.Rejected && r.DecidedByUserId == second && r.RequestedByUserId == asker);

        // Nobody takes away their own last role with the user-administration right; another administrator may.
        (await admin.RemoveRoleAsync(second, admins, second)).Error.Code.Should().Be("Sec.LastAdminRole");
        (await admin.RemoveRoleAsync(second, admins, asker)).IsSuccess.Should().BeTrue();
        RolesOf(second).Should().BeEmpty();

        List<long> RolesOf(long user) => [.. db.Users.AsNoTracking().Include(u => u.Roles).Single(u => u.Id == user).Roles.Select(r => r.RoleId).Order()];
    }
}
