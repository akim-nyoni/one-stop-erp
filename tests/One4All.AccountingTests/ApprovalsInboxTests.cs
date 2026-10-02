using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Workflow;
using One4All.Core.Workflow;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the approvals inbox: an in-progress workflow appears for the user its stage names, not for
/// others, and disappears once that user has acted.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ApprovalsInboxTests
{
    private readonly AccountingFixture _fixture;

    public ApprovalsInboxTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Inbox_lists_workflows_waiting_on_the_eligible_approver()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        const long approver = 10;
        const long bystander = 20;

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);

            WorkflowDefinition definition = new(companyId, "PurchaseOrder", "Single approval");
            WorkflowStage buyer = definition.AddStage(1, "Buyer");
            buyer.AddEligibleUser(approver);
            db.WorkflowDefinitions.Add(definition);
            await db.SaveChangesAsync();
        }

        long instanceId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IWorkflowService workflow = scope.ServiceProvider.GetRequiredService<IWorkflowService>();
            var start = await workflow.StartAsync(companyId, "PurchaseOrder", "PO-9", 500m, initiatedByUserId: 1);
            start.IsSuccess.Should().BeTrue(start.IsFailure ? start.Error.ToString() : null);
            start.Value.Status.Should().Be(WorkflowStatus.InProgress);
            instanceId = start.Value.InstanceId;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IWorkflowService workflow = scope.ServiceProvider.GetRequiredService<IWorkflowService>();

            IReadOnlyList<PendingApproval> mine = await workflow.PendingForUserAsync(companyId, approver);
            mine.Should().ContainSingle();
            PendingApproval item = mine.Single();
            item.InstanceId.Should().Be(instanceId);
            item.DocumentReference.Should().Be("PO-9");
            item.StageName.Should().Be("Buyer");
            item.ApprovalsSoFar.Should().Be(0);
            item.Quorum.Should().Be(1);

            // The stage names only the approver, so a bystander sees nothing.
            (await workflow.PendingForUserAsync(companyId, bystander)).Should().BeEmpty();

            // Once the approver acts, the workflow completes and leaves the inbox.
            (await workflow.ActAsync(instanceId, approver, WorkflowActionType.Approve, "ok")).Value.Should().Be(WorkflowStatus.Approved);
            (await workflow.PendingForUserAsync(companyId, approver)).Should().BeEmpty();
        }
    }
}
