using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Budgeting;
using One4All.Core.Budgeting;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers budget preparation: a draft is submitted, reviewed, cut by a Finance proposal and returned; the department accepts the cut,
/// revises another line and resubmits as round two; it is recommended and approved under a council resolution (not by the submitter),
/// sealed, and only then activated; the trail keeps requested beside proposed figures.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class BudgetPreparationTests
{
    private readonly AccountingFixture _fixture;

    public BudgetPreparationTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_budget_is_negotiated_and_approved_before_activation()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Companies.Single(c => c.Id == companyId).BudgetApprovalRequired = true;
        await db.SaveChangesAsync();
        long yearId = await db.FiscalYears.Where(y => y.CompanyId == companyId).Select(y => y.Id).FirstAsync();
        IBudgetingService budgeting = scope.ServiceProvider.GetRequiredService<IBudgetingService>();
        long budgetId = (await budgeting.CreateBudgetAsync(companyId, yearId, "2026", BudgetType.OriginalApproved, "ADM", [new NewBudgetLine("6000", 1, 1_000m), new NewBudgetLine("6100", 1, 500m)])).Value;
        const long department = 11, finance = 12, committee = 13, council = 14;

        IBudgetPreparationService prep = scope.ServiceProvider.GetRequiredService<IBudgetPreparationService>();
        (await budgeting.ActivateAsync(companyId, budgetId)).Error.Code.Should().Be("Budget.NotApproved");
        (await prep.SubmitAsync(companyId, budgetId, department, "Admin budget")).IsSuccess.Should().BeTrue();
        (await prep.ApproveAsync(companyId, budgetId, council, "RES-1")).Error.Code.Should().Be("Budget.WrongStage");
        (await prep.StartReviewAsync(companyId, budgetId, finance)).IsSuccess.Should().BeTrue();
        (await prep.ProposeAsync(companyId, budgetId, finance, [new ProposedFigure("6000", 1, 800m, Comment: "Hold salaries flat")])).IsSuccess.Should().BeTrue();
        (await prep.ReturnAsync(companyId, budgetId, finance, " ")).Error.Code.Should().Be("Budget.ReasonRequired");
        (await prep.ReturnAsync(companyId, budgetId, finance, "Cut admin")).IsSuccess.Should().BeTrue();

        // The department takes the cut, trims rent itself and resubmits (round two).
        (await prep.AcceptProposalsAsync(companyId, budgetId, department)).Value.Should().Be(1);
        (await prep.ReviseAsync(companyId, budgetId, department, new ProposedFigure("6100", 1, 450m))).IsSuccess.Should().BeTrue();
        (await prep.SubmitAsync(companyId, budgetId, department)).IsSuccess.Should().BeTrue();
        (await prep.StartReviewAsync(companyId, budgetId, finance)).IsSuccess.Should().BeTrue();
        (await prep.RecommendAsync(companyId, budgetId, committee, "Executive recommends")).IsSuccess.Should().BeTrue();
        (await prep.ApproveAsync(companyId, budgetId, department, "RES-1")).Error.Code.Should().Be("Budget.SameUser");
        (await prep.ApproveAsync(companyId, budgetId, council, "")).Error.Code.Should().Be("Budget.ResolutionRequired");
        (await prep.ApproveAsync(companyId, budgetId, council, "COUNCIL-2026-14")).IsSuccess.Should().BeTrue();
        (await prep.ReviseAsync(companyId, budgetId, department, new ProposedFigure("6100", 1, 999m))).Error.Code.Should().Be("Budget.WrongStage");
        (await budgeting.ActivateAsync(companyId, budgetId, council)).Error.Code.Should().Be("Budget.SelfActivation", "the approver does not put it into force (thirteenth audit #6)");
        (await budgeting.ActivateAsync(companyId, budgetId, department)).Error.Code.Should().Be("Budget.SelfActivation");
        (await budgeting.ActivateAsync(companyId, budgetId, finance)).IsSuccess.Should().BeTrue();

        Budget budget = await db.Set<Budget>().AsNoTracking().Include(b => b.Lines).SingleAsync(b => b.Id == budgetId);
        (budget.Round, budget.ResolutionReference, budget.IsSealed, budget.IsActive, budget.ActivatedByUserId).Should().Be((2, "COUNCIL-2026-14", true, true, finance));
        budget.Lines.Single(l => l.AccountCode == "6000").Amount.Should().Be(800m);
        budget.Lines.Single(l => l.AccountCode == "6100").Amount.Should().Be(450m);

        BudgetTrail trail = (await prep.TrailAsync(companyId, budgetId)).Value;
        trail.Proposals.Should().ContainSingle(p => p.Round == 1 && p.Requested == 1_000m && p.Proposed == 800m);
        trail.Events.Select(e => e.Action).Should().Equal(BudgetReviewAction.Submitted, BudgetReviewAction.ReviewStarted, BudgetReviewAction.Proposed, BudgetReviewAction.Returned,
            BudgetReviewAction.ProposalsAccepted, BudgetReviewAction.Revised, BudgetReviewAction.Submitted, BudgetReviewAction.ReviewStarted, BudgetReviewAction.Recommended,
            BudgetReviewAction.Approved);
    }
}
