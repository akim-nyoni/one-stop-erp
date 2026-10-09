using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Security;
using One4All.Application.Abstractions.Workflow;
using One4All.Core.Accounting;
using One4All.Core.Audit;
using One4All.Core.Workflow;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the business events in the audit trail (brief §6): a posting writes Post, a reversal Void with its reason, a workflow decision Approve or
/// Reject by the approver — each with the terminal it came from, which the journal keeps too — and the chain still verifies.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class BusinessEventAuditTests
{
    private readonly AccountingFixture _fixture;

    public BusinessEventAuditTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Postings_reversals_and_decisions_are_named_in_the_audit_trail()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        scope.ServiceProvider.GetRequiredService<One4All.Infrastructure.Security.CurrentUserContext>().SetClientAddress("10.9.8.7");

        IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
        PostingReceipt posted = (await engine.PostAsync(new JournalRequest
        {
            CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 3, 10), TransactionCurrency = Scenario.Usd, DocumentType = "AUDIT", DocumentNumber = "BE-1",
            Lines =
            [
                new JournalLineRequest { AccountCode = "6800", Side = JournalSide.Debit, TransactionAmount = 75m },
                new JournalLineRequest { AccountCode = "1100", Side = JournalSide.Credit, TransactionAmount = 75m },
            ],
        })).Value;
        (await db.Journals.AsNoTracking().SingleAsync(j => j.Id == posted.JournalId)).Terminal.Should().Be("10.9.8.7");
        PostingReceipt reversed = (await engine.ReverseAsync(companyId, posted.JournalId, new DateOnly(2026, 3, 11), "Keyed twice")).Value;

        WorkflowDefinition definition = new(companyId, "BeDocument", "One step");
        definition.AddStage(1, "Manager").AddEligibleUser(55);
        db.WorkflowDefinitions.Add(definition);
        await db.SaveChangesAsync();
        IWorkflowService workflow = scope.ServiceProvider.GetRequiredService<IWorkflowService>();
        long approved = (await workflow.StartAsync(companyId, "BeDocument", "BE-DOC-1", 10m, 3)).Value.InstanceId;
        long rejected = (await workflow.StartAsync(companyId, "BeDocument", "BE-DOC-2", 10m, 3)).Value.InstanceId;
        (await workflow.ActAsync(approved, 55, WorkflowActionType.Approve, "fine")).IsSuccess.Should().BeTrue();
        (await workflow.ActAsync(rejected, 55, WorkflowActionType.Reject, "no budget")).IsSuccess.Should().BeTrue();

        List<ChangeLogEntry> events = await db.ChangeLog.AsNoTracking()
            .Where(e => e.CompanyId == companyId && (e.Action == AuditAction.Post || e.Action == AuditAction.Void || e.Action == AuditAction.Approve || e.Action == AuditAction.Reject))
            .OrderBy(e => e.Id).ToListAsync();
        events.Should().Contain(e => e.Action == AuditAction.Post && e.EntityId == posted.JournalNumber && e.Terminal == "10.9.8.7");
        events.Should().Contain(e => e.Action == AuditAction.Post && e.EntityId == reversed.JournalNumber);
        events.Should().Contain(e => e.Action == AuditAction.Void && e.EntityId == posted.JournalNumber && e.Reason == "Keyed twice");
        events.Should().Contain(e => e.Action == AuditAction.Approve && e.EntityId == "BE-DOC-1" && e.UserId == 55 && e.Reason == "fine");
        events.Should().Contain(e => e.Action == AuditAction.Reject && e.EntityId == "BE-DOC-2" && e.Reason == "no budget");

        (await scope.ServiceProvider.GetRequiredService<IAuditService>().VerifyChainAsync(companyId)).IsIntact.Should().BeTrue();
    }
}
