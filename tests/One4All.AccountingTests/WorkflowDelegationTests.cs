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
        (await workflow.DelegateAsync(companyId, manager, manager, today, today, "Leave")).Error.Code.Should().Be("Workflow.DelegationInvalid");
        (await workflow.DelegateAsync(companyId, manager, deputy, today.AddDays(-1), today.AddDays(5), "Leave")).IsSuccess.Should().BeTrue();
        (await workflow.DelegateAsync(companyId, manager, stranger, today, today, "Leave")).Error.Code.Should().Be("Workflow.DelegationOverlap");
        (await workflow.PendingForUserAsync(companyId, deputy)).Should().ContainSingle(p => p.InstanceId == instanceId && p.OnBehalfOfUserId == manager);
        (await workflow.ActAsync(instanceId, stranger, WorkflowActionType.Approve, null)).Error.Code.Should().Be("Workflow.NotEligible");

        // An ad hoc approver at stage 1 may act too; the deputy approves for the manager and the stage moves on.
        (await workflow.AddApproverAsync(instanceId, auditor, addedByUserId: 1)).IsSuccess.Should().BeTrue();
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
}
