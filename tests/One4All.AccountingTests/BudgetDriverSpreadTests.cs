using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Budgeting;
using One4All.Application.Budgeting;
using One4All.Core.Budgeting;
using One4All.Core.Payroll;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers budget planning: driver-based lines re-computed (previewed, then applied) when an assumption changes; annual figures spread by
/// profile; the staff budget built from the establishment; and an approved budget re-phased between periods by someone other than the
/// requester, the annual total unchanged and the seal intact.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class BudgetDriverSpreadTests
{
    private readonly AccountingFixture _fixture;

    public BudgetDriverSpreadTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Assumptions_spreads_staff_and_rephasing_shape_the_budget()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        long yearId = await db.FiscalYears.Where(y => y.CompanyId == companyId).Select(y => y.Id).FirstAsync();
        db.EstablishmentPosts.AddRange(new EstablishmentPost(companyId, "ADM", "C1", 3, 12_000m), new EstablishmentPost(companyId, "OPS", "B2", 9, 8_000m));
        await db.SaveChangesAsync();
        IBudgetingService budgeting = scope.ServiceProvider.GetRequiredService<IBudgetingService>();
        long budgetId = (await budgeting.CreateBudgetAsync(companyId, yearId, "2026", BudgetType.OriginalApproved, "ADM", [])).Value;
        IBudgetPlanningService planning = scope.ServiceProvider.GetRequiredService<IBudgetPlanningService>();
        Budget Load() => db.Set<Budget>().AsNoTracking().Include(b => b.Lines).Single(b => b.Id == budgetId);
        decimal Sum(string account, int? period = null) => Load().Lines.Where(l => l.AccountCode == account && (period == null || l.Period == period)).Sum(l => l.Amount);

        // 40 learners × the FEE assumption; raising the fee is previewed, then applied.
        (await planning.SetDriverAsync(companyId, yearId, "fee", 100m, commit: true, "Fee per learner", "USD")).IsSuccess.Should().BeTrue();
        (await planning.SetDriverLineAsync(companyId, budgetId, "4200", 1, "FEE", 40m)).Value.Should().Be(4_000m);
        (await planning.SetDriverAsync(companyId, yearId, "FEE", 110m, commit: false)).Value.Should().ContainSingle(x => x.Before == 4_000m && x.After == 4_400m);
        Sum("4200").Should().Be(4_000m);
        (await planning.SetDriverAsync(companyId, yearId, "FEE", 110m, commit: true)).IsSuccess.Should().BeTrue();
        Sum("4200").Should().Be(4_400m);

        // Spreading.
        (await planning.SpreadAsync(companyId, budgetId, "6200", 1_200m, SpreadProfile.Even)).Value.Should().OnlyContain(x => x == 100m);
        (await planning.SpreadAsync(companyId, budgetId, "6300", 1_000m, SpreadProfile.Custom, [1m, 2m])).Error.Code.Should().Be("Budget.WeightsInvalid");
        (await planning.SpreadAsync(companyId, budgetId, "6300", 1_000m, SpreadProfile.SCurve)).Value.Sum().Should().Be(1_000m);
        Sum("6300", 1).Should().BeLessThan(Sum("6300", 6));

        // Admin's three posts at 12,000, +5% increment, −10% vacancy = 34,020; contributions 10% = 3,402.
        (await planning.BuildStaffBudgetAsync(companyId, budgetId, "6000", 5m, 10m, 10m, "6100")).Value.Should().Be(new StaffBudget(3, 34_020m, 3_402m));
        Sum("6000").Should().Be(34_020m);
        Sum("6100").Should().Be(3_402m);

        // Approved (activation seals it), then re-phased.
        (await budgeting.ActivateAsync(companyId, budgetId)).IsSuccess.Should().BeTrue();
        (await planning.SpreadAsync(companyId, budgetId, "6200", 1m, SpreadProfile.Even)).Error.Code.Should().Be("Budget.WrongStage");
        (await planning.RequestRephaseAsync(companyId, budgetId, "6200", 1, 6, 500m, "Contract delayed", 1)).Error.Code.Should().Be("Budget.RephaseInvalid");
        long rephase = (await planning.RequestRephaseAsync(companyId, budgetId, "6200", 1, 6, 50m, "Contract delayed", 1)).Value;
        (await planning.DecideRephaseAsync(companyId, rephase, true, 1)).Error.Code.Should().Be("Budget.SameUser");
        (await planning.DecideRephaseAsync(companyId, rephase, true, 2)).IsSuccess.Should().BeTrue();
        (Sum("6200", 1), Sum("6200", 6), Sum("6200")).Should().Be((50m, 150m, 1_200m));
        (await scope.ServiceProvider.GetRequiredService<IBudgetControlService>().VerifySealAsync(companyId, budgetId)).Value.Should().BeTrue();
    }
}
