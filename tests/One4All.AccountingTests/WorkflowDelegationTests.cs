using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Workflow;
using One4All.Core.Workflow;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers delegation, ad hoc approvers and escalation: a delegate sees and approves on the away approver's behalf (once for both),
/// an approver added by hand can act, and an overdue stage escalates to its escalation user exactly once.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class WorkflowDelegationTests
{
    private readonly AccountingFixture _fixture;

    public WorkflowDelegationTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Delegates_added_approvers_and_escalation_can_act()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        const long manager = 31, deputy = 32, auditor = 33, director = 34, stranger = 35;

        WorkflowDefinition definition = new(companyId, "WfDelegTest", "Two stages");
        definition.AddStage(1, "Manager").AddEligibleUser(manager);
        WorkflowStage board = definition.AddStage(2, "Board");
        board.AddEligibleUser(auditor + 100);
        board.EscalateAfterHours = 0;
        board.EscalateToUserId = director;
        db.WorkflowDefinitions.Add(definition);
        await db.SaveChangesAsync();

        IWorkflowService workflow = scope.ServiceProvider.GetRequiredService<IWorkflowService>();
        long instanceId = (await workflow.StartAsync(companyId, "WfDelegTest", "DOC-1", 1000m, initiatedByUserId: 1)).Value.InstanceId;

        // The manager is away: the deputy sees the document as theirs on the manager's behalf.
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        (await workflow.DelegateAsync(companyId, manager, manager, today, today, "Leave", manager)).Error.Code.Should().Be("Workflow.DelegationInvalid");

        // Only the approver hands over their own approvals: someone else cannot delegate the manager's to themselves.
        (await workflow.DelegateAsync(companyId, manager, stranger, today, today, "Leave", stranger)).Error.Code.Should().Be("Workflow.DelegationNotOwn");
        (await workflow.DelegateAsync(companyId, manager, deputy, today.AddDays(-1), today.AddDays(5), "Leave", manager)).IsSuccess.Should().BeTrue();
        (await workflow.DelegateAsync(companyId, manager, stranger, today, today, "Leave", manager)).Error.Code.Should().Be("Workflow.DelegationOverlap");
        (await workflow.PendingForUserAsync(companyId, deputy)).Should().ContainSingle(p => p.InstanceId == instanceId && p.OnBehalfOfUserId == manager);
        (await workflow.ActAsync(instanceId, stranger, WorkflowActionType.Approve, null)).Error.Code.Should().Be("Workflow.NotEligible");

        // An ad hoc approver at stage 1 may act too; the deputy approves for the manager and the stage moves on.
        // Only an approver of the stage adds one, never the requester nor themselves.
        (await workflow.AddApproverAsync(instanceId, stranger, addedByUserId: stranger + 1)).Error.Code.Should().Be("Workflow.NotEligible");
        (await workflow.AddApproverAsync(instanceId, 1, addedByUserId: manager)).Error.Code.Should().Be("Workflow.OwnDocument");
        (await workflow.AddApproverAsync(instanceId, manager, addedByUserId: manager)).Error.Code.Should().Be("Workflow.AddSelf");
        (await workflow.AddApproverAsync(instanceId, auditor, addedByUserId: manager)).IsSuccess.Should().BeTrue();
        (await workflow.PendingForUserAsync(companyId, auditor)).Should().ContainSingle(p => p.InstanceId == instanceId && p.OnBehalfOfUserId == null);
        (await workflow.ActAsync(instanceId, deputy, WorkflowActionType.Approve, "for the manager")).IsSuccess.Should().BeTrue();
        WorkflowInstance instance = db.WorkflowInstances.Single(i => i.Id == instanceId);
        instance.CurrentStageSequence.Should().Be(2);
        instance.Approvals.Single().OnBehalfOfUserId.Should().Be(manager);

        // The board stage escalates to the director once, who can then approve.
        (await workflow.PendingForUserAsync(companyId, director)).Should().NotContain(p => p.InstanceId == instanceId);
        (await workflow.EscalateOverdueAsync(companyId)).Should().BeGreaterThanOrEqualTo(1);
        (await workflow.EscalateOverdueAsync(companyId)).Should().Be(0);
        (await workflow.PendingForUserAsync(companyId, director)).Should().ContainSingle(p => p.InstanceId == instanceId);
        (await workflow.ActAsync(instanceId, director, WorkflowActionType.Approve, null)).Value.Should().Be(WorkflowStatus.Approved);
    }

    [SkippableFact]
    public async Task The_requester_cannot_approve_their_own_document_even_at_an_open_stage()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        const long requester = 41, colleague = 42;

        // A stage naming nobody is open to everyone - except whoever raised the document.
        WorkflowDefinition definition = new(companyId, "WfOwnDocTest", "Open stage");
        definition.AddStage(1, "Anyone");
        db.WorkflowDefinitions.Add(definition);
        await db.SaveChangesAsync();

        IWorkflowService workflow = scope.ServiceProvider.GetRequiredService<IWorkflowService>();
        long instanceId = (await workflow.StartAsync(companyId, "WfOwnDocTest", "OWN-1", 500m, requester)).Value.InstanceId;
        (await workflow.PendingForUserAsync(companyId, requester)).Should().NotContain(p => p.InstanceId == instanceId);
        (await workflow.ActAsync(instanceId, requester, WorkflowActionType.Approve, null)).Error.Code.Should().Be("Workflow.OwnDocument");
        (await workflow.ActManyAsync([instanceId], requester, WorkflowActionType.Approve, null)).Should().ContainSingle(r => !r.Succeeded);
        (await workflow.GetStatusAsync(instanceId)).Value.Should().Be(WorkflowStatus.InProgress);

        (await workflow.ActAsync(instanceId, colleague, WorkflowActionType.Approve, null)).Value.Should().Be(WorkflowStatus.Approved);

        // The requester may still withdraw their own document by rejecting it.
        long second = (await workflow.StartAsync(companyId, "WfOwnDocTest", "OWN-2", 500m, requester)).Value.InstanceId;
        (await workflow.ActAsync(second, requester, WorkflowActionType.Reject, "Raised in error")).Value.Should().Be(WorkflowStatus.Rejected);
    }
}
