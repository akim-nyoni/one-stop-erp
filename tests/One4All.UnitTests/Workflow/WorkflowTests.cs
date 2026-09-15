using FluentAssertions;
using One4All.Core.Common;
using One4All.Core.Workflow;
using Xunit;

namespace One4All.UnitTests.Workflow;

public class WorkflowTests
{
    private static (WorkflowInstance instance, IReadOnlyList<WorkflowStage> applicable) Start(WorkflowDefinition def, decimal value)
    {
        List<WorkflowStage> applicable = def.Stages.Where(s => s.AppliesTo(value)).OrderBy(s => s.Sequence).ToList();
        int firstSeq = applicable.Count > 0 ? applicable[0].Sequence : 0;
        WorkflowInstance instance = new(1, def.Id, def.DocumentType, "DOC-1", value, initiatedByUserId: 1, firstSeq);
        return (instance, applicable);
    }

    private static WorkflowStatus Act(WorkflowInstance instance, IReadOnlyList<WorkflowStage> applicable, long userId, WorkflowActionType action, string? comment = null) =>
        instance.Act(applicable, userId, action, comment, DateTimeOffset.UtcNow).Value;

    [Fact]
    public void Single_stage_single_approval_approves()
    {
        WorkflowDefinition def = new(1, "Requisition", "Standard");
        def.AddStage(1, "Manager");

        (WorkflowInstance instance, IReadOnlyList<WorkflowStage> applicable) = Start(def, 500m);
        Act(instance, applicable, 10, WorkflowActionType.Approve).Should().Be(WorkflowStatus.Approved);
    }

    [Fact]
    public void Quorum_requires_two_approvals()
    {
        WorkflowDefinition def = new(1, "Requisition", "Committee");
        def.AddStage(1, "Any 2 of 3", quorum: 2);

        (WorkflowInstance instance, IReadOnlyList<WorkflowStage> applicable) = Start(def, 500m);
        Act(instance, applicable, 10, WorkflowActionType.Approve).Should().Be(WorkflowStatus.InProgress);
        Act(instance, applicable, 11, WorkflowActionType.Approve).Should().Be(WorkflowStatus.Approved);
    }

    [Fact]
    public void Two_stages_advance_in_order()
    {
        WorkflowDefinition def = new(1, "PurchaseOrder", "Two-tier");
        def.AddStage(1, "Buyer");
        def.AddStage(2, "Finance");

        (WorkflowInstance instance, IReadOnlyList<WorkflowStage> applicable) = Start(def, 500m);
        Act(instance, applicable, 10, WorkflowActionType.Approve).Should().Be(WorkflowStatus.InProgress);
        instance.CurrentStageSequence.Should().Be(2);
        Act(instance, applicable, 11, WorkflowActionType.Approve).Should().Be(WorkflowStatus.Approved);
    }

    [Fact]
    public void Value_band_skips_a_high_value_stage_for_a_small_document()
    {
        WorkflowDefinition def = new(1, "Requisition", "Banded");
        def.AddStage(1, "Manager");
        def.AddStage(2, "Director", minValue: 10000m);

        (WorkflowInstance small, IReadOnlyList<WorkflowStage> smallStages) = Start(def, 500m);
        smallStages.Should().HaveCount(1);
        Act(small, smallStages, 10, WorkflowActionType.Approve).Should().Be(WorkflowStatus.Approved);

        (WorkflowInstance big, IReadOnlyList<WorkflowStage> bigStages) = Start(def, 20000m);
        bigStages.Should().HaveCount(2);
        Act(big, bigStages, 10, WorkflowActionType.Approve).Should().Be(WorkflowStatus.InProgress);
        Act(big, bigStages, 11, WorkflowActionType.Approve).Should().Be(WorkflowStatus.Approved);
    }

    [Fact]
    public void Reject_requires_comment_and_rejects()
    {
        WorkflowDefinition def = new(1, "Requisition", "Standard");
        def.AddStage(1, "Manager");
        (WorkflowInstance instance, IReadOnlyList<WorkflowStage> applicable) = Start(def, 500m);

        Result<WorkflowStatus> noComment = instance.Act(applicable, 10, WorkflowActionType.Reject, null, DateTimeOffset.UtcNow);
        noComment.IsFailure.Should().BeTrue();
        noComment.Error.Code.Should().Be("Workflow.CommentRequired");

        Act(instance, applicable, 10, WorkflowActionType.Reject, "Not justified").Should().Be(WorkflowStatus.Rejected);
    }

    [Fact]
    public void Return_sends_back_and_can_resubmit()
    {
        WorkflowDefinition def = new(1, "Requisition", "Standard");
        def.AddStage(1, "Manager");
        (WorkflowInstance instance, IReadOnlyList<WorkflowStage> applicable) = Start(def, 500m);

        Act(instance, applicable, 10, WorkflowActionType.Return, "Add a quote").Should().Be(WorkflowStatus.Returned);
        instance.Resubmit(1);
        instance.Status.Should().Be(WorkflowStatus.InProgress);
        Act(instance, applicable, 10, WorkflowActionType.Approve).Should().Be(WorkflowStatus.Approved);
    }

    [Fact]
    public void Cannot_act_on_a_finished_workflow()
    {
        WorkflowDefinition def = new(1, "Requisition", "Standard");
        def.AddStage(1, "Manager");
        (WorkflowInstance instance, IReadOnlyList<WorkflowStage> applicable) = Start(def, 500m);
        Act(instance, applicable, 10, WorkflowActionType.Approve);

        instance.Act(applicable, 11, WorkflowActionType.Approve, null, DateTimeOffset.UtcNow).Error.Code.Should().Be("Workflow.NotInProgress");
    }
}
