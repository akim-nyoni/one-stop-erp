using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Workflow;
using One4All.Core.Security;
using One4All.Core.Workflow;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers defining a workflow (brief §7) through the service: stages with quorum, value band, condition, named approvers, a role and escalation; a new
/// definition replaces the active one for its document type; documents then route through it; and what is refused.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class WorkflowDefinitionTests
{
    private readonly AccountingFixture _fixture;

    public WorkflowDefinitionTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_workflow_is_defined_and_routes_documents()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        string suffix = companyId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        User buyer = new($"wf-buyer-{suffix}", "Buyer"), cfo = new($"wf-cfo-{suffix}", "CFO");
        db.Users.AddRange(buyer, cfo);
        await db.SaveChangesAsync();
        IWorkflowService workflow = scope.ServiceProvider.GetRequiredService<IWorkflowService>();

        (await workflow.DefineAsync(companyId, new WorkflowDefinitionInput(WorkflowDocumentTypes.PurchaseRequisition, "Requisitions", []))).Error.Code.Should().Be("Workflow.DefinitionInvalid");
        (await workflow.DefineAsync(companyId, new WorkflowDefinitionInput(WorkflowDocumentTypes.PurchaseRequisition, "Requisitions", [new WorkflowStageInput(1, "Buyer")]))).Error.Code
            .Should().Be("Workflow.DefinitionInvalid", "a stage needs an approver");
        (await workflow.DefineAsync(companyId, new WorkflowDefinitionInput(WorkflowDocumentTypes.PurchaseRequisition, "Requisitions", [new WorkflowStageInput(1, "Buyer", Approvers: ["nobody-here"])]))).Error.Code
            .Should().Be("Workflow.UserNotFound");
        (await workflow.DefineAsync(companyId, new WorkflowDefinitionInput(WorkflowDocumentTypes.PurchaseRequisition, "Requisitions", [new WorkflowStageInput(1, "Buyer", RoleName: "No Such Role")]))).Error.Code
            .Should().Be("Workflow.RoleNotFound");
        (await workflow.DefineAsync(companyId, new WorkflowDefinitionInput(WorkflowDocumentTypes.PurchaseRequisition, "Requisitions",
            [new WorkflowStageInput(1, "Buyer", Approvers: [buyer.Username], Condition: "Value 5")]))).Error.Code.Should().Be("Workflow.ConditionInvalid");

        var first = await workflow.DefineAsync(companyId, new WorkflowDefinitionInput(WorkflowDocumentTypes.PurchaseRequisition, "Requisitions v1", [new WorkflowStageInput(1, "Buyer", Approvers: [buyer.Username])]));
        first.IsSuccess.Should().BeTrue(first.IsFailure ? first.Error.Message : null);
        var second = await workflow.DefineAsync(companyId, new WorkflowDefinitionInput(WorkflowDocumentTypes.PurchaseRequisition, "Requisitions v2",
        [
            new WorkflowStageInput(1, "Buyer", Approvers: [buyer.Username.ToUpperInvariant()], EscalateAfterHours: 24, EscalateTo: cfo.Username),
            new WorkflowStageInput(2, "CFO", Quorum: 1, MinValue: 10_000m, Approvers: [cfo.Username], DynamicApprover: DynamicApprover.RequesterManager),
        ]));
        second.IsSuccess.Should().BeTrue(second.IsFailure ? second.Error.Message : null);
        List<WorkflowDefinition> defined = await db.WorkflowDefinitions.AsNoTracking().Include(d => d.Stages).Where(d => d.CompanyId == companyId && d.DocumentType == WorkflowDocumentTypes.PurchaseRequisition)
            .OrderBy(d => d.Id).ToListAsync();
        defined.Select(d => (d.Name, d.IsActive)).Should().Equal(("Requisitions v1", false), ("Requisitions v2", true));
        WorkflowStage escalating = defined[1].Stages.Single(s => s.Sequence == 1);
        (escalating.EligibleUserIds.Single(), escalating.EscalateAfterHours, escalating.EscalateToUserId).Should().Be((buyer.Id, 24, cfo.Id));
        (defined[1].Stages.Single(s => s.Sequence == 2).MinValue, defined[1].Stages.Single(s => s.Sequence == 2).DynamicApprover).Should().Be((10_000m, DynamicApprover.RequesterManager));

        // A small requisition stops at the buyer; a large one goes on to the CFO.
        var small = await workflow.StartAsync(companyId, WorkflowDocumentTypes.PurchaseRequisition, "REQ-S", 500m, initiatedByUserId: 1);
        small.IsSuccess.Should().BeTrue(small.IsFailure ? small.Error.Message : null);
        (await workflow.PendingForUserAsync(companyId, buyer.Id)).Should().ContainSingle(p => p.DocumentReference == "REQ-S");
        (await workflow.ActAsync(small.Value.InstanceId, buyer.Id, WorkflowActionType.Approve, "ok")).Value.Should().Be(WorkflowStatus.Approved);
        var large = await workflow.StartAsync(companyId, WorkflowDocumentTypes.PurchaseRequisition, "REQ-L", 25_000m, initiatedByUserId: 1);
        (await workflow.ActAsync(large.Value.InstanceId, buyer.Id, WorkflowActionType.Approve, "ok")).Value.Should().Be(WorkflowStatus.InProgress);
        (await workflow.PendingForUserAsync(companyId, cfo.Id)).Should().ContainSingle(p => p.DocumentReference == "REQ-L");

        // Approvers through a group: a group member approves a stage that names the group, and holds a role the group carries.
        Group buyers = new($"wf-buyers-{suffix}", "Buyers");
        db.Groups.Add(buyers);
        User member = new($"wf-member-{suffix}", "Member");
        db.Users.Add(member);
        await db.SaveChangesAsync();
        member.JoinGroup(buyers.Id);
        await db.SaveChangesAsync();
        (await workflow.DefineAsync(companyId, new WorkflowDefinitionInput(WorkflowDocumentTypes.Stocktake, "Counts", [new WorkflowStageInput(1, "Buyers", GroupName: "no-such-group")]))).Error.Code
            .Should().Be("Workflow.GroupNotFound");
        var grouped = await workflow.DefineAsync(companyId, new WorkflowDefinitionInput(WorkflowDocumentTypes.Stocktake, "Counts", [new WorkflowStageInput(1, "Buyers", GroupName: buyers.Name)]));
        grouped.IsSuccess.Should().BeTrue(grouped.IsFailure ? grouped.Error.Message : null);
        var count = await workflow.StartAsync(companyId, WorkflowDocumentTypes.Stocktake, "STK-1", 0m, initiatedByUserId: 1);
        (await workflow.PendingForUserAsync(companyId, member.Id)).Should().ContainSingle(p => p.DocumentReference == "STK-1");
        (await workflow.ActAsync(count.Value.InstanceId, member.Id, WorkflowActionType.Approve, "counted")).Value.Should().Be(WorkflowStatus.Approved);

        (await workflow.SetDefinitionActiveAsync(companyId, second.Value, false)).IsSuccess.Should().BeTrue();
        (await workflow.StartAsync(companyId, WorkflowDocumentTypes.PurchaseRequisition, "REQ-X", 5m, 1)).Error.Code.Should().Be("Workflow.NoDefinition");
        (await workflow.SetDefinitionActiveAsync(companyId, first.Value, true)).IsSuccess.Should().BeTrue();
        (await db.WorkflowDefinitions.AsNoTracking().Where(d => d.CompanyId == companyId && d.DocumentType == WorkflowDocumentTypes.PurchaseRequisition && d.IsActive).Select(d => d.Name).ToListAsync()).Should().Equal("Requisitions v1");
    }
}
