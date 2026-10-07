using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Budgeting;
using One4All.Core.Budgeting;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the multi-year framework and the end of a budget's life: outer years are set before approval and shown beside year 1, an approved framework
/// rolls forward into next year's draft (year 2 spread over the periods, later years moved up), and a superseded version is archived then locked —
/// after which it cannot be activated or changed.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class BudgetFrameworkTests
{
    private readonly AccountingFixture _fixture;

    public BudgetFrameworkTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Frameworks_roll_forward_and_superseded_versions_are_archived_then_locked()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        long year2026 = await db.FiscalYears.Where(y => y.CompanyId == companyId).Select(y => y.Id).FirstAsync();
        FiscalYear next = new(companyId, "2027", new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31));
        db.FiscalYears.Add(next);
        await db.SaveChangesAsync();

        IBudgetingService budgeting = scope.ServiceProvider.GetRequiredService<IBudgetingService>();
        IBudgetFrameworkService framework = scope.ServiceProvider.GetRequiredService<IBudgetFrameworkService>();
        long first = (await budgeting.CreateBudgetAsync(companyId, year2026, "MTEF 2026", BudgetType.BaseCase, "ADMIN",
            [new NewBudgetLine("6100", 1, 600m), new NewBudgetLine("6100", 2, 600m)])).Value;
        (await framework.SetOuterYearsAsync(companyId, first, [new OuterYearAmount(5, "6100", 1m)])).Error.Code.Should().Be("Budget.OuterYears");
        (await framework.SetOuterYearsAsync(companyId, first, [new OuterYearAmount(1, "6100", 1_300m), new OuterYearAmount(2, "6100", 1_400m)])).IsSuccess.Should().BeTrue();
        (await framework.FrameworkAsync(companyId, first)).Value.Should().ContainSingle().Which.Years.Should().Equal(1_200m, 1_300m, 1_400m);
        (await framework.RollForwardAsync(companyId, first, next.Id, "MTEF 2027")).Error.Code.Should().Be("Budget.RollForward");

        // Approved and active, the framework is fixed and rolls into 2027: 1,300 spread (108.33 × 11 + 108.37), 1,400 moves up and carries on.
        (await budgeting.ActivateAsync(companyId, first)).IsSuccess.Should().BeTrue();
        (await framework.SetOuterYearsAsync(companyId, first, [])).Error.Code.Should().Be("Budget.FrameworkSealed");
        long rolled = (await framework.RollForwardAsync(companyId, first, next.Id, "MTEF 2027")).Value;
        (await framework.RollForwardAsync(companyId, first, next.Id, "Again")).Error.Code.Should().Be("Budget.RolledAlready");
        Budget draft = await db.Set<Budget>().AsNoTracking().Include(b => b.Lines).SingleAsync(b => b.Id == rolled);
        (draft.FiscalYearId, draft.Type, draft.Status, draft.RolledFromBudgetId, draft.Lines.Count, draft.AnnualTotal).Should().Be((next.Id, BudgetType.BaseCase, BudgetStatus.Draft, (long?)first, 12, 1_300m));
        draft.Lines.Single(l => l.Period == 12).Amount.Should().Be(108.37m);
        (await framework.FrameworkAsync(companyId, rolled)).Value.Single().Years.Should().Equal(1_300m, 1_400m, 1_400m);

        // A new version supersedes the first; it is archived (seal verified), then locked, and neither step can be skipped or undone.
        (await framework.ArchiveAsync(companyId, first)).Error.Code.Should().Be("Budget.PutAway");
        long second = (await budgeting.CreateBudgetAsync(companyId, year2026, "MTEF 2026 v2", BudgetType.BaseCase, "ADMIN", [new NewBudgetLine("6100", 1, 700m)])).Value;
        (await budgeting.ActivateAsync(companyId, second)).IsSuccess.Should().BeTrue();
        (await framework.LockAsync(companyId, first)).Error.Code.Should().Be("Budget.PutAway");
        (await framework.ArchiveAsync(companyId, first)).IsSuccess.Should().BeTrue();
        (await framework.LockAsync(companyId, first)).IsSuccess.Should().BeTrue();
        (await budgeting.ActivateAsync(companyId, first)).Error.Code.Should().Be("Budget.PutAway");
        (await db.Set<Budget>().AsNoTracking().SingleAsync(b => b.Id == first)).Status.Should().Be(BudgetStatus.Locked);
    }
}
