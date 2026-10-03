using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Budgeting;
using One4All.Core.Budgeting;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the budget seal end to end: a draft has none; activation seals it and the seal verifies after reloading; an
/// approved virement leaves it intact; a line altered directly in the database breaks it.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class BudgetSealAccountingTests
{
    private readonly AccountingFixture _fixture;

    public BudgetSealAccountingTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task An_active_budget_is_sealed_and_tampering_is_detected()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        long yearId = await db.FiscalYears.Where(y => y.CompanyId == companyId).Select(y => y.Id).FirstAsync();

        IBudgetingService budgeting = scope.ServiceProvider.GetRequiredService<IBudgetingService>();
        IBudgetControlService control = scope.ServiceProvider.GetRequiredService<IBudgetControlService>();
        long budgetId = (await budgeting.CreateBudgetAsync(companyId, yearId, "2026", BudgetType.OriginalApproved, null,
            [new NewBudgetLine("6800", 3, 1_000m), new NewBudgetLine("6700", 3, 500m)])).Value;
        (await control.VerifySealAsync(companyId, budgetId)).Error.Code.Should().Be("Budget.NotSealed");

        (await budgeting.ActivateAsync(companyId, budgetId)).IsSuccess.Should().BeTrue();
        (await control.VerifySealAsync(companyId, budgetId)).Value.Should().BeTrue();

        long virement = (await control.RequestVirementAsync(companyId, budgetId, new VirementRequest("6800", "6700", 3, 100m, "Travel"), 1)).Value;
        (await control.ApproveVirementAsync(companyId, virement, 2)).IsSuccess.Should().BeTrue();
        (await control.VerifySealAsync(companyId, budgetId)).Value.Should().BeTrue();

        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE bud.budget_line SET amount = 1500 WHERE budget_id = {budgetId} AND account_code = '6800' AND virement_id IS NULL");
        (await control.VerifySealAsync(companyId, budgetId)).Value.Should().BeFalse();
    }
}
