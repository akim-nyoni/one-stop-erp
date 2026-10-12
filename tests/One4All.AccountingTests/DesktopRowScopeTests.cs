using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Security;
using One4All.Core.Payroll;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers a host-wide row scope: once set, every new database context reads only the user's branches and departments (payroll isolation).</summary>
[Collection(AccountingCollection.Name)]
public sealed class DesktopRowScopeTests
{
    private readonly AccountingFixture _fixture;

    public DesktopRowScopeTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Payroll_rows_follow_the_signed_in_users_scope()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        long companyId, north, south;
        await using (AsyncServiceScope setup = provider.CreateAsyncScope())
        {
            One4AllDbContext db = setup.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Branch n = new(companyId, "RS-N", "North"), s = new(companyId, "RS-S", "South");
            db.Branches.AddRange(n, s);
            await db.SaveChangesAsync();
            (north, south) = (n.Id, s.Id);
            db.Employees.AddRange(
                new Employee(companyId, "RS-1", "North clerk", 100m, new DateOnly(2025, 1, 1)) { BranchId = north, Department = "FIN" },
                new Employee(companyId, "RS-2", "South clerk", 100m, new DateOnly(2025, 1, 1)) { BranchId = south, Department = "FIN" },
                new Employee(companyId, "RS-3", "North driver", 100m, new DateOnly(2025, 1, 1)) { BranchId = north, Department = "OPS" });
            await db.SaveChangesAsync();
        }

        async Task<List<string>> Visible()
        {
            await using AsyncServiceScope scope = provider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<One4AllDbContext>().Employees.AsNoTracking().Where(e => e.CompanyId == companyId)
                .OrderBy(e => e.EmployeeNumber).Select(e => e.EmployeeNumber).ToListAsync();
        }

        (await Visible()).Should().Equal("RS-1", "RS-2", "RS-3");
        RowScopeHolder holder = provider.GetRequiredService<RowScopeHolder>();
        holder.Scope = new RowScope([north], null, null, null, null, null, null);
        (await Visible()).Should().Equal("RS-1", "RS-3");
        holder.Scope = new RowScope([north], null, null, null, null, ["FIN"], null);
        (await Visible()).Should().Equal("RS-1");
        holder.Scope = null;
        (await Visible()).Should().HaveCount(3);
    }
}
