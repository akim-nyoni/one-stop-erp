using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Workflow;
using One4All.Core.Workflow;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class WorkflowServiceTests
{
    private readonly DatabaseFixture _fixture;

    public WorkflowServiceTests(DatabaseFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Two_stage_workflow_routes_approvals_and_enforces_eligibility()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        const long companyId = 900;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            WorkflowDefinition definition = new(companyId, "PurchaseOrder", "Two-tier");
            WorkflowStage s1 = definition.AddStage(1, "Buyer");
            s1.AddEligibleUser(10);
            WorkflowStage s2 = definition.AddStage(2, "Finance", minValue: 1000m);
            s2.AddEligibleUser(20);
            db.WorkflowDefinitions.Add(definition);
            await db.SaveChangesAsync();
        }

        long instanceId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IWorkflowService workflow = scope.ServiceProvider.GetRequiredService<IWorkflowService>();
            var start = await workflow.StartAsync(companyId, "PurchaseOrder", "PO-1", 5000m, initiatedByUserId: 1);
            start.IsSuccess.Should().BeTrue(start.IsFailure ? start.Error.ToString() : null);
            start.Value.Status.Should().Be(WorkflowStatus.InProgress);
            instanceId = start.Value.InstanceId;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IWorkflowService workflow = scope.ServiceProvider.GetRequiredService<IWorkflowService>();

            // Stage 1: buyer 10 approves -> advances to stage 2.
            (await workflow.ActAsync(instanceId, 10, WorkflowActionType.Approve, null)).Value.Should().Be(WorkflowStatus.InProgress);

            // An ineligible user cannot approve stage 2.
            var forbidden = await workflow.ActAsync(instanceId, 99, WorkflowActionType.Approve, null);
            forbidden.IsFailure.Should().BeTrue();
            forbidden.Error.Code.Should().Be("Workflow.NotEligible");

            // Finance 20 approves -> fully approved.
            (await workflow.ActAsync(instanceId, 20, WorkflowActionType.Approve, null)).Value.Should().Be(WorkflowStatus.Approved);
            (await workflow.GetStatusAsync(instanceId)).Value.Should().Be(WorkflowStatus.Approved);
        }
    }
}
