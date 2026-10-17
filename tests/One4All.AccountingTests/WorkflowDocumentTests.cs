using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Communications;
using One4All.Application.Abstractions.Procurement;
using One4All.Application.Abstractions.Workflow;
using One4All.Core.Accounting;
using One4All.Core.Inventory;
using One4All.Core.Procurement;
using One4All.Core.Security;
using One4All.Core.Trading;
using One4All.Core.Workflow;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the approval workflow driving documents: a company's requisition workflow (two stages, the second only for larger amounts)
/// governs the requisition - direct approval is refused, approvers are notified stage by stage, the last approval approves the
/// requisition and the requester is told; a journal workflow posts the journal on approval and rejects it on rejection; and when the
/// document cannot follow a decision (the preparer approving their own journal), the decision is not recorded.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class WorkflowDocumentTests
{
    private readonly AccountingFixture _fixture;

    public WorkflowDocumentTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Workflows_govern_requisitions_and_journals()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        string tag = Guid.NewGuid().ToString("N")[..8];
        User requester = new($"req{tag}", "Requester"), manager = new($"mgr{tag}", "Manager"), finance = new($"fin{tag}", "Finance"), clerk = new($"clk{tag}", "Clerk"),
            controller = new($"ctl{tag}", "Controller");
        db.AddRange(requester, manager, finance, clerk, controller);
        Supplier supplier = new(companyId, "WF-SUPP", "Stationers", Scenario.Usd);
        Item paper = new(companyId, "WF-PAPER", "Paper");
        db.AddRange(supplier, paper);
        await db.SaveChangesAsync();

        WorkflowDefinition requisitions = new(companyId, WorkflowDocumentTypes.PurchaseRequisition, "Requisitions");
        requisitions.AddStage(1, "Manager").AddEligibleUser(manager.Id);
        requisitions.AddStage(2, "Finance", minValue: 1_000m).AddEligibleUser(finance.Id);
        WorkflowDefinition journals = new(companyId, WorkflowDocumentTypes.Journal, "Journals");
        journals.AddStage(1, "Controller").AddEligibleUser(controller.Id);
        db.WorkflowDefinitions.AddRange(requisitions, journals);
        await db.SaveChangesAsync();

        IWorkflowService workflow = scope.ServiceProvider.GetRequiredService<IWorkflowService>();
        INotificationService notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
        async Task<long> InstanceFor(string type, long id) => await db.WorkflowInstances.AsNoTracking()
            .Where(i => i.CompanyId == companyId && i.DocumentType == type && i.DocumentReference == id.ToString()).Select(i => i.Id).SingleAsync();
        async Task<IReadOnlyList<string>> Inbox(User user) => [.. (await notifications.InboxAsync(companyId, user.Id, unreadOnly: false)).Select(n => n.Title)];

        // A 1,500 requisition: manager then finance.
        IPurchaseRequisitionService service = scope.ServiceProvider.GetRequiredService<IPurchaseRequisitionService>();
        long requisition = (await service.CreateAsync(companyId, supplier.Id, $"WF-REQ-{tag}", new DateOnly(2026, 4, 1), Scenario.Usd, [new PurchaseRequisitionLineRequest(paper.Id, "Paper", 100m, 15m)])).Value;
        (await service.SubmitAsync(companyId, requisition, requester.Id)).IsSuccess.Should().BeTrue();
        long reqFlow = await InstanceFor(WorkflowDocumentTypes.PurchaseRequisition, requisition);
        (await Inbox(manager)).Should().ContainSingle(t => t.StartsWith("Approval needed: PurchaseRequisition", StringComparison.Ordinal));
        (await Inbox(finance)).Should().BeEmpty();
        (await service.ApproveAsync(companyId, requisition, manager.Id)).Error.Code.Should().Be("Workflow.Governs");

        (await workflow.ActAsync(reqFlow, manager.Id, WorkflowActionType.Approve, null)).Value.Should().Be(WorkflowStatus.InProgress);
        (await db.Set<PurchaseRequisition>().AsNoTracking().SingleAsync(r => r.Id == requisition)).Status.Should().Be(PurchaseRequisitionStatus.Submitted);
        (await Inbox(finance)).Should().ContainSingle(t => t.StartsWith("Approval needed", StringComparison.Ordinal));
        (await workflow.ActAsync(reqFlow, finance.Id, WorkflowActionType.Approve, "Within budget")).Value.Should().Be(WorkflowStatus.Approved);
        PurchaseRequisition approved = await db.Set<PurchaseRequisition>().AsNoTracking().SingleAsync(r => r.Id == requisition);
        (approved.Status, approved.ApprovedByUserId).Should().Be((PurchaseRequisitionStatus.Approved, finance.Id));
        (await Inbox(requester)).Should().Contain(t => t.EndsWith("approved", StringComparison.Ordinal));

        // A journal: the controller's approval posts it; a second one is rejected.
        IJournalApprovalService journalApproval = scope.ServiceProvider.GetRequiredService<IJournalApprovalService>();
        JournalRequest Journal(decimal amount) => new()
        {
            CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 4, 2), TransactionCurrency = Scenario.Usd, Narrative = "Accrual",
            Lines =
            [
                new JournalLineRequest { AccountCode = "6100", Side = JournalSide.Debit, TransactionAmount = amount },
                new JournalLineRequest { AccountCode = "1100", Side = JournalSide.Credit, TransactionAmount = amount },
            ],
        };
        var submittedJournal = await journalApproval.SubmitAsync(Journal(300m), clerk.Id);
        submittedJournal.IsSuccess.Should().BeTrue(submittedJournal.IsFailure ? submittedJournal.Error.ToString() : null);
        long posted = submittedJournal.Value.JournalId;
        long journalFlow = await InstanceFor(WorkflowDocumentTypes.Journal, posted);
        (await journalApproval.ApproveAsync(companyId, posted, controller.Id)).Error.Code.Should().Be("Workflow.Governs");

        // The preparer cannot approve their own journal even when added to the stage: the decision is not kept.
        (await workflow.AddApproverAsync(journalFlow, clerk.Id, controller.Id)).IsSuccess.Should().BeTrue();
        (await workflow.ActAsync(journalFlow, clerk.Id, WorkflowActionType.Approve, null)).Error.Code.Should().Be("Gl.SelfApproval");
        (await workflow.GetStatusAsync(journalFlow)).Value.Should().Be(WorkflowStatus.InProgress);

        (await workflow.ActAsync(journalFlow, controller.Id, WorkflowActionType.Approve, null)).Value.Should().Be(WorkflowStatus.Approved);
        JournalEntry released = await db.Journals.AsNoTracking().SingleAsync(j => j.Id == posted);
        (released.Status, released.ApprovedByUserId).Should().Be((JournalStatus.Posted, controller.Id));

        long refused = (await journalApproval.SubmitAsync(Journal(80m), clerk.Id)).Value.JournalId;
        (await workflow.ActAsync(await InstanceFor(WorkflowDocumentTypes.Journal, refused), controller.Id, WorkflowActionType.Reject, "No supporting invoice")).Value.Should().Be(WorkflowStatus.Rejected);
        JournalEntry rejected = await db.Journals.AsNoTracking().SingleAsync(j => j.Id == refused);
        (rejected.Status, rejected.RejectionReason).Should().Be((JournalStatus.Cancelled, "No supporting invoice"));
        (await Inbox(clerk)).Should().Contain(t => t.EndsWith("rejected", StringComparison.Ordinal));
    }
}
