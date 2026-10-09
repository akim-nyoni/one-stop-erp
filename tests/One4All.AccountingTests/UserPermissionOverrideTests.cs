using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Security;
using One4All.Core.Security;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers the permission grid's user overrides: grant and deny on top of the roles, and clearing them to inherit again.</summary>
[Collection(AccountingCollection.Name)]
public sealed class UserPermissionOverrideTests
{
    private readonly AccountingFixture _fixture;

    public UserPermissionOverrideTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_user_is_granted_and_denied_operations_on_top_of_their_roles()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        string tag = Guid.NewGuid().ToString("N")[..8];
        Role role = new($"Override readers {tag}");
        role.Grant("Reporting.Report", OperationFlags.View | OperationFlags.Print);
        User user = new($"ovr-{tag}", "Override User");
        db.AddRange(role, user);
        await db.SaveChangesAsync();
        user.AssignRole(role.Id);
        await db.SaveChangesAsync();

        IPermissionService permissions = scope.ServiceProvider.GetRequiredService<IPermissionService>();
        (await permissions.RoleGrantsAsync(user.Id)).Should().ContainSingle(g => g.PermissionCode == "Reporting.Report" && g.Operations == (OperationFlags.View | OperationFlags.Print));
        (await permissions.SetOverridesAsync(user.Id, [new UserOverrideEdit("Nope.Nothing", OperationFlags.View, OperationFlags.None)])).Error.Code.Should().Be("Perm.Feature");

        (await permissions.SetOverridesAsync(user.Id, [new UserOverrideEdit("Reporting.Report", OperationFlags.Export, OperationFlags.Print)])).IsSuccess.Should().BeTrue();
        (await permissions.ResolveAsync(user.Id)).OperationsFor("Reporting.Report").Should().Be(OperationFlags.View | OperationFlags.Export);

        (await permissions.SetOverridesAsync(user.Id, [new UserOverrideEdit("Reporting.Report", OperationFlags.None, OperationFlags.None)])).IsSuccess.Should().BeTrue();
        (await db.Set<UserPermissionOverrideEntity>().AsNoTracking().CountAsync(o => o.UserId == user.Id)).Should().Be(0, "cleared cells inherit from the roles");
        (await permissions.ResolveAsync(user.Id)).OperationsFor("Reporting.Report").Should().Be(OperationFlags.View | OperationFlags.Print);
    }
}
