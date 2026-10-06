using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Security;
using One4All.Application.Abstractions.Workflow;
using One4All.Core.Workflow;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers workflow rules: a stage that applies only when its condition holds, approval by the requester's manager and by a role's
/// holders, a snapshot of the document at each decision, and bulk approval acting item by item.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class WorkflowRulesTests
{
    private readonly AccountingFixture _fixture;

    public WorkflowRulesTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Conditions_dynamic_approvers_snapshots_and_bulk_approval()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        IUserAdminService admin = scope.ServiceProvider.GetRequiredService<IUserAdminService>();
        string tag = Guid.NewGuid().ToString("N")[..8];
        long requester = (await admin.CreateUserAsync($"wr.req.{tag}", "Requester", null, "Request2026a")).Value;
        long manager = (await admin.CreateUserAsync($"wr.mgr.{tag}", "Manager", null, "Manager2026a")).Value;
        long auditor = (await admin.CreateUserAsync($"wr.it.{tag}", "IT head", null, "ItHead2026a")).Value;
        long itRole = (await admin.CreateRoleAsync($"IT approvers {tag}", null)).Value;
        (await admin.AssignRoleAsync(auditor, itRole)).IsSuccess.Should().BeTrue();
        (await admin.SetManagerAsync(requester, requester)).Error.Code.Should().Be("Security.ManagerInvalid");
        (await admin.SetManagerAsync(requester, manager)).IsSuccess.Should().BeTrue();

        WorkflowDefinition definition = new(companyId, $"WR-{tag}", "Purchases");
        definition.AddStage(1, "Line manager");
        definition.AddStage(2, "IT sign-off");
        db.WorkflowDefinitions.Add(definition);
        await db.SaveChangesAsync();
        IWorkflowService workflow = scope.ServiceProvider.GetRequiredService<IWorkflowService>();
        (await workflow.SetStageRulesAsync(companyId, definition.Id, 1, null, null, DynamicApprover.RequesterManager)).IsSuccess.Should().BeTrue();
        (await workflow.SetStageRulesAsync(companyId, definition.Id, 2, "Department IT", null, DynamicApprover.None)).Error.Code.Should().Be("Workflow.ConditionInvalid");
        (await workflow.SetStageRulesAsync(companyId, definition.Id, 2, "Department = IT and Value > 1000", $"IT approvers {tag}", DynamicApprover.None)).IsSuccess.Should().BeTrue();

        // An IT purchase over 1,000 needs the manager then IT; a finance one only the manager.
        long laptops = (await workflow.StartAsync(companyId, $"WR-{tag}", "PR-1", 5_000m, requester, new Dictionary<string, string> { ["Department"] = "IT" })).Value.InstanceId;
        long paper = (await workflow.StartAsync(companyId, $"WR-{tag}", "PR-2", 5_000m, requester, new Dictionary<string, string> { ["Department"] = "Finance" })).Value.InstanceId;
        (await workflow.ActAsync(laptops, auditor, WorkflowActionType.Approve, null)).Error.Code.Should().Be("Workflow.NotEligible");
        (await workflow.PendingForUserAsync(companyId, manager)).Select(p => p.InstanceId).Should().Contain([laptops, paper]);

        IReadOnlyList<BulkActionResult> bulk = await workflow.ActManyAsync([laptops, paper, -1], manager, WorkflowActionType.Approve, "Fine");
        bulk.Select(b => (b.InstanceId, b.Succeeded, b.Status)).Should().Equal((laptops, true, WorkflowStatus.InProgress), (paper, true, WorkflowStatus.Approved), (-1L, false, null));
        (await workflow.ActAsync(laptops, auditor, WorkflowActionType.Approve, "Specs ok")).Value.Should().Be(WorkflowStatus.Approved);

        WorkflowInstance instance = await db.WorkflowInstances.AsNoTracking().Include(i => i.Approvals).SingleAsync(i => i.Id == laptops);
        instance.Approvals.Should().HaveCount(2).And.OnlyContain(a => a.Snapshot != null && a.Snapshot.Contains("\"Department\":\"IT\"") && a.Snapshot.Contains("PR-1"));
        instance.Approvals.Last().Snapshot.Should().Contain("IT sign-off");
    }
}
