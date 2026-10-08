using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Security;
using One4All.Application.Security;
using One4All.Core.Inventory;
using One4All.Core.Payroll;
using One4All.Core.Security;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the scope-rule row filters (brief §5.1): a user scoped to a warehouse and a department reads only that warehouse's stock and that
/// department's (or untagged) employees once the row scope is applied; codes match whatever their case; an unrestricted user filters nothing.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class RowScopeFilterTests
{
    private readonly AccountingFixture _fixture;

    public RowScopeFilterTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Reads_keep_to_the_users_warehouses_and_departments()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        string tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        long company;
        long near;
        long far;
        long scopedUser;
        long openUser;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            company = await Scenario.SeedAsync(db);
            Warehouse w1 = new(company, $"N{tag}", "Near store");
            Warehouse w2 = new(company, $"F{tag}", "Far store");
            Item item = new(company, $"I{tag}", "Widget");
            db.AddRange(w1, w2, item);
            await db.SaveChangesAsync();
            (near, far) = (w1.Id, w2.Id);
            db.StockBalances.AddRange(new StockBalance(company, item.Id, near), new StockBalance(company, item.Id, far));
            db.Employees.AddRange(
                new Employee(company, $"E1{tag}", "Ops person", 100m, new DateOnly(2026, 1, 1)) { Department = "OPS" },
                new Employee(company, $"E2{tag}", "Finance person", 100m, new DateOnly(2026, 1, 1)) { Department = "FIN" },
                new Employee(company, $"E3{tag}", "Untagged person", 100m, new DateOnly(2026, 1, 1)));

            Role role = new($"Storeman {tag}");
            role.Grant("Inventory.Item", OperationFlags.View);
            db.Roles.Add(role);
            await db.SaveChangesAsync();
            User user = new($"store.{tag}", "Storeman");
            user.SetPasswordHash("hash", mustChange: false);
            var assignment = user.AssignRole(role.Id);
            assignment.AddScope(new AccessScope(ScopeDimension.Warehouse, [w1.Code.ToLowerInvariant()]));
            assignment.AddScope(new AccessScope(ScopeDimension.Department, ["ops"]));
            User open = new($"open.{tag}", "Unscoped");
            open.SetPasswordHash("hash", mustChange: false);
            open.AssignRole(role.Id);
            db.Users.AddRange(user, open);
            await db.SaveChangesAsync();
            (scopedUser, openUser) = (user.Id, open.Id);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IDataScopeService scopes = scope.ServiceProvider.GetRequiredService<IDataScopeService>();
            (await scopes.RowScopeAsync(openUser)).IsEmpty.Should().BeTrue();

            RowScope rows = await scopes.RowScopeAsync(scopedUser);
            rows.WarehouseIds.Should().Equal(near);
            rows.Departments.Should().Equal("OPS");
            rows.BranchIds.Should().BeNull("branches are not restricted");

            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            (await db.StockBalances.CountAsync(b => b.CompanyId == company)).Should().Be(2, "nothing is filtered until the row scope is applied");

            db.ApplyRowScope(rows);
            (await db.StockBalances.Where(b => b.CompanyId == company).Select(b => b.WarehouseId).ToListAsync()).Should().Equal(near);
            (await db.StockBalances.AnyAsync(b => b.WarehouseId == far)).Should().BeFalse();
            (await db.Employees.Where(e => e.CompanyId == company).Select(e => e.FullName).OrderBy(n => n).ToListAsync())
                .Should().Equal("Ops person", "Untagged person");

            db.ApplyRowScope(RowScope.None);
            (await db.StockBalances.CountAsync(b => b.CompanyId == company)).Should().Be(2);
        }
    }
}
