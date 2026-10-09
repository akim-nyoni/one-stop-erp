using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Budgeting;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Workflow;
using One4All.Core.Budgeting;
using One4All.Core.Housing;
using One4All.Core.Security;
using One4All.Core.Workflow;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers routing a recommended budget and a proposed housing allocation run through the approval workflow: neither can be approved directly
/// while its workflow runs; the workflow's approval approves the budget (the approver's comment is the resolution) and sends the run's
/// offers; a rejection returns the budget to its preparers.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class BudgetAndAllocationWorkflowTests
{
    private readonly AccountingFixture _fixture;

    public BudgetAndAllocationWorkflowTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Budgets_and_allocation_runs_are_approved_through_their_workflows()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        string tag = Guid.NewGuid().ToString("N")[..8];
        User council = new($"baw-c{tag}", "Council");
        db.Users.Add(council);
        db.Companies.Single(c => c.Id == companyId).BudgetApprovalRequired = true;
        await db.SaveChangesAsync();
        foreach (string type in new[] { WorkflowDocumentTypes.Budget, WorkflowDocumentTypes.AllocationRun })
        {
            WorkflowDefinition definition = new(companyId, type, type);
            definition.AddStage(1, "Council").AddEligibleUser(council.Id);
            db.WorkflowDefinitions.Add(definition);
        }

        await db.SaveChangesAsync();
        IWorkflowService workflow = scope.ServiceProvider.GetRequiredService<IWorkflowService>();
        async Task<long> Pending(string type) => await db.WorkflowInstances.AsNoTracking()
            .Where(i => i.CompanyId == companyId && i.DocumentType == type && i.Status == WorkflowStatus.InProgress).Select(i => i.Id).SingleAsync();

        // A budget recommended for approval goes to the workflow; a rejection returns it, an approval approves it.
        long yearId = await db.FiscalYears.Where(y => y.CompanyId == companyId).Select(y => y.Id).FirstAsync();
        long budgetId = (await scope.ServiceProvider.GetRequiredService<IBudgetingService>().CreateBudgetAsync(companyId, yearId, "2026", BudgetType.OriginalApproved, "ADM",
            [new NewBudgetLine("6000", 1, 1_000m)])).Value;
        IBudgetPreparationService prep = scope.ServiceProvider.GetRequiredService<IBudgetPreparationService>();
        async Task Recommend()
        {
            (await prep.SubmitAsync(companyId, budgetId, 11)).IsSuccess.Should().BeTrue();
            (await prep.StartReviewAsync(companyId, budgetId, 12)).IsSuccess.Should().BeTrue();
            (await prep.RecommendAsync(companyId, budgetId, 13)).IsSuccess.Should().BeTrue();
        }

        await Recommend();
        (await prep.ApproveAsync(companyId, budgetId, 14, "RES-1")).Error.Code.Should().Be("Wf.Governed");
        (await workflow.ActAsync(await Pending(WorkflowDocumentTypes.Budget), council.Id, WorkflowActionType.Reject, "Too high")).IsSuccess.Should().BeTrue();
        (await db.Set<Budget>().AsNoTracking().SingleAsync(b => b.Id == budgetId)).Status.Should().Be(BudgetStatus.Returned);
        await Recommend();
        (await workflow.ActAsync(await Pending(WorkflowDocumentTypes.Budget), council.Id, WorkflowActionType.Approve, "COUNCIL-2026-9")).Value.Should().Be(WorkflowStatus.Approved);
        Budget approved = await db.Set<Budget>().AsNoTracking().SingleAsync(b => b.Id == budgetId);
        (approved.Status, approved.ResolutionReference).Should().Be((BudgetStatus.Approved, "COUNCIL-2026-9"));

        // An allocation run waits for its workflow, whose approval sends the offers.
        HousingScheme scheme = new(companyId, $"SCH-BAW-{companyId}", "Workflow Scheme", SchemeType.CouncilDelivered);
        db.HousingSchemes.Add(scheme);
        await db.SaveChangesAsync();
        IHousingService housing = scope.ServiceProvider.GetRequiredService<IHousingService>();
        long parcelId = (await housing.AcquireLandAsync(companyId, $"LP-BAW-{companyId}", "Layout", 600m, 6000m, new DateOnly(2026, 1, 2), "1450", "1100", "LAND-BAW")).Value.LandParcelId;
        (await housing.SubdivideAsync(companyId, parcelId, scheme.Id, [new NewStand("BAW-1", 300m), new NewStand("BAW-2", 300m)], ApportionMethod.Equal, new DateOnly(2026, 1, 5), "1450", "1460"))
            .IsSuccess.Should().BeTrue();
        HousingApplicant applicant = new(companyId, $"BAW-A-{companyId}", "Applicant", new DateOnly(2010, 1, 1));
        applicant.SetDetails(HousingCategory.HighDensity, new DateOnly(1980, 1, 1), new DateOnly(2000, 1, 1), 1, false, 300m, CurrentTenure.Lodger);
        applicant.Register($"WL-BAW-{companyId}", applicant.RegistrationDate);
        db.HousingApplicants.Add(applicant);
        await db.SaveChangesAsync();

        IAllocationService allocations = scope.ServiceProvider.GetRequiredService<IAllocationService>();
        AllocationRunView run = (await allocations.ProposeAsync(new AllocationProposal
        {
            CompanyId = companyId, SchemeId = scheme.Id, Category = HousingCategory.HighDensity, Method = AllocationMethod.Seniority, AsAtDate = new DateOnly(2026, 3, 1), UserId = 71, OfferDays = 14,
        })).Value;
        (await allocations.ApproveAsync(companyId, run.RunNumber, 72, new DateOnly(2026, 3, 2), 30)).Error.Code.Should().Be("Wf.Governed");
        (await workflow.ActAsync(await Pending(WorkflowDocumentTypes.AllocationRun), council.Id, WorkflowActionType.Approve, null)).Value.Should().Be(WorkflowStatus.Approved);
        AllocationRun sent = await db.Set<AllocationRun>().AsNoTracking().SingleAsync(r => r.CompanyId == companyId && r.RunNumber == run.RunNumber);
        (sent.Status, sent.ApprovedByUserId).Should().Be((AllocationRunStatus.Approved, (long?)council.Id));
    }
}
