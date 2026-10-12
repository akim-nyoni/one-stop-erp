using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Security;
using One4All.Core.Security;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers comparing two stored roles and changing many roles' grants at once.</summary>
[Collection(AccountingCollection.Name)]
public sealed class RoleBulkChangeTests
{
    private readonly AccountingFixture _fixture;

    public RoleBulkChangeTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Roles_are_compared_and_changed_in_bulk()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        string tag = Guid.NewGuid().ToString("N")[..8];
        Role clerk = new($"Bulk clerk {tag}");
        clerk.Grant("Payroll.Employee", OperationFlags.View);
        Role officer = new($"Bulk officer {tag}");
        officer.Grant("Payroll.Employee", OperationFlags.View | OperationFlags.Edit);
        db.Roles.AddRange(clerk, officer);
        await db.SaveChangesAsync();

        IUserAdminService admin = scope.ServiceProvider.GetRequiredService<IUserAdminService>();
        (await admin.CompareRolesAsync(clerk.Id, officer.Id)).Value.Should().ContainSingle().Which.OnlySecond.Should().Be(OperationFlags.Edit);
        (await admin.BulkGrantAsync([clerk.Id, officer.Id], "Payroll.Employee", OperationFlags.Print, BulkGrantMode.Add)).Value.Should().Be(2);
        (await admin.BulkGrantAsync([clerk.Id], "Payroll.Employee", OperationFlags.None, BulkGrantMode.Add)).Error.Code.Should().Be("Security.BulkGrant");
        (await admin.BulkGrantAsync([officer.Id], "Payroll.Employee", OperationFlags.Edit, BulkGrantMode.Remove)).Value.Should().Be(1);
        (await admin.CompareRolesAsync(clerk.Id, officer.Id)).Value.Should().BeEmpty();

        Role reloaded = await db.Roles.AsNoTracking().Include(r => r.Permissions).SingleAsync(r => r.Id == clerk.Id);
        reloaded.Permissions.Single(p => p.PermissionCode == "Payroll.Employee").Operations.Should().Be(OperationFlags.View | OperationFlags.Print);
    }
}
