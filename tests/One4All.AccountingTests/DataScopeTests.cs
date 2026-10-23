using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Security;
using One4All.Core.Inventory;
using One4All.Core.Security;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers row-level scopes in use: a storeman limited to one warehouse (by code) reaches it and not the other, through the scope
/// service and API requests naming the warehouse; a ward code is checked by code; joining a group with roles keeps the user's own scopes,
/// and only a member with no direct assignments is unrestricted by the group.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class DataScopeTests
{
    private readonly AccountingFixture _fixture;

    public DataScopeTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Warehouse_and_ward_scopes_limit_what_a_user_reaches()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Warehouse mine = new(companyId, "DSC-MINE", "Mine"), theirs = new(companyId, "DSC-THEIRS", "Theirs");
        db.AddRange(mine, theirs);
        await db.SaveChangesAsync();

        IUserAdminService admin = scope.ServiceProvider.GetRequiredService<IUserAdminService>();
        string tag = Guid.NewGuid().ToString("N")[..8];
        long storeman = (await admin.CreateUserAsync($"store.{tag}", "Storeman", null, "Warehouse2026x")).Value;
        long role = (await admin.CreateRoleAsync($"Storeman {tag}", null)).Value;
        db.Roles.Single(r => r.Id == role).Grant("Inventory.Item", OperationFlags.View);
        db.Roles.Single(r => r.Id == role).Grant("Municipal.Property", OperationFlags.View);
        await db.SaveChangesAsync();
        (await admin.AssignRoleAsync(storeman, role, 900_001, new Dictionary<ScopeDimension, IReadOnlyList<string>>
        {
            [ScopeDimension.Warehouse] = ["dsc-mine"], [ScopeDimension.WardZone] = ["W7"],
        })).IsSuccess.Should().BeTrue();

        IDataScopeService scopes = scope.ServiceProvider.GetRequiredService<IDataScopeService>();
        (await scopes.AllowsAsync(storeman, ScopeDimension.Warehouse, mine.Id)).Should().BeTrue();
        (await scopes.AllowsAsync(storeman, ScopeDimension.Warehouse, theirs.Id)).Should().BeFalse();
        (await scopes.AllowsAsync(storeman, ScopeDimension.Branch, 1)).Should().BeTrue();

        IApiAccessService access = scope.ServiceProvider.GetRequiredService<IApiAccessService>();
        Task<ApiAccessDecision> Ask(string path, params (string Key, string Value)[] query)
            => access.AuthoriseAsync(new ApiAccessRequest(storeman, "GET", path, query.ToDictionary(q => q.Key, q => q.Value)));
        (await Ask("/api/stock/replenishment", ("companyId", $"{companyId}"), ("warehouseId", $"{mine.Id}"))).Allowed.Should().BeTrue();
        (await Ask("/api/stock/replenishment", ("companyId", $"{companyId}"), ("warehouseId", $"{theirs.Id}"))).StatusCode.Should().Be(403);
        (await Ask("/api/municipal/arrears", ("companyId", $"{companyId}"), ("ward", "W7"))).Allowed.Should().BeTrue();
        (await Ask("/api/municipal/arrears", ("companyId", $"{companyId}"), ("ward", "W8"))).StatusCode.Should().Be(403);

        // A group's roles inherit the member's own scopes: the scoped storeman stays limited to their warehouse after joining.
        long group = (await admin.CreateGroupAsync($"Stores {tag}", null)).Value;
        (await admin.AddRoleToGroupAsync(group, role)).IsSuccess.Should().BeTrue();
        (await admin.SetGroupMembershipAsync(storeman, group, true)).IsSuccess.Should().BeTrue();
        (await scopes.AllowsAsync(storeman, ScopeDimension.Warehouse, theirs.Id)).Should().BeFalse();
        (await scopes.AllowsAsync(storeman, ScopeDimension.Warehouse, mine.Id)).Should().BeTrue();
        (await Ask("/api/stock/replenishment", ("companyId", $"{companyId}"), ("warehouseId", $"{theirs.Id}"))).StatusCode.Should().Be(403);

        // Only a member with no direct assignments at all is unrestricted by the group's roles.
        long newcomer = (await admin.CreateUserAsync($"new.{tag}", "Newcomer", null, "Warehouse2026x")).Value;
        (await admin.SetGroupMembershipAsync(newcomer, group, true)).IsSuccess.Should().BeTrue();
        (await scopes.AllowsAsync(newcomer, ScopeDimension.Warehouse, theirs.Id)).Should().BeTrue();
    }
}
