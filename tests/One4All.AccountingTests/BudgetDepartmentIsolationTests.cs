using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Budgeting;
using One4All.Core.Budgeting;
using One4All.Core.Payroll;
using One4All.Core.Security;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers department isolation in budget preparation: a Roads preparer (by their employee record) submits and revises the Roads budget but not Water's,
/// and sees only Roads; a user tied to no department is not confined.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class BudgetDepartmentIsolationTests
{
    private readonly AccountingFixture _fixture;

    public BudgetDepartmentIsolationTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Departments_prepare_only_their_own_budgets()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        User roads = new($"bdi-roads-{companyId}", "Roads Preparer"), finance = new($"bdi-fin-{companyId}", "Finance Officer");
        db.Users.AddRange(roads, finance);
        await db.SaveChangesAsync();
        db.Employees.Add(new Employee(companyId, $"BDI-{companyId % 100000}", "Roads Preparer", 1000m, new DateOnly(2025, 1, 1)) { Department = "ROADS", UserId = roads.Id });
        await db.SaveChangesAsync();
        long yearId = await db.FiscalYears.Where(y => y.CompanyId == companyId).Select(y => y.Id).FirstAsync();
        IBudgetingService budgeting = scope.ServiceProvider.GetRequiredService<IBudgetingService>();
        long roadsBudget = (await budgeting.CreateBudgetAsync(companyId, yearId, "Roads", BudgetType.OriginalApproved, "ROADS", [new NewBudgetLine("6000", 1, 1000m)])).Value;
        long waterBudget = (await budgeting.CreateBudgetAsync(companyId, yearId, "Water", BudgetType.OriginalApproved, "WATER", [new NewBudgetLine("6000", 1, 2000m)])).Value;

        IBudgetPreparationService prep = scope.ServiceProvider.GetRequiredService<IBudgetPreparationService>();
        (await prep.DepartmentsForAsync(companyId, roads.Id)).Should().BeEquivalentTo(["ROADS"]);
        (await prep.DepartmentsForAsync(companyId, finance.Id)).Should().BeNull("tied to no department");

        (await prep.ReviseAsync(companyId, waterBudget, roads.Id, new ProposedFigure("6000", 1, 2500m))).Error.Code.Should().Be("Budget.OtherDepartment");
        (await prep.SubmitAsync(companyId, waterBudget, roads.Id)).Error.Code.Should().Be("Budget.OtherDepartment");
        (await prep.ReviseAsync(companyId, roadsBudget, roads.Id, new ProposedFigure("6000", 1, 1200m))).IsSuccess.Should().BeTrue();
        (await prep.SubmitAsync(companyId, roadsBudget, roads.Id)).IsSuccess.Should().BeTrue();
        (await prep.SubmitAsync(companyId, waterBudget, finance.Id)).IsSuccess.Should().BeTrue();
    }
}
