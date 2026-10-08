using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.System;
using One4All.Application.Abstractions.Workflow;
using One4All.Core.Communications;
using One4All.Core.Security;
using One4All.Core.System;
using One4All.Core.Trading;
using One4All.Core.Workflow;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers approvals for master data (brief §7): a change waits for the "MasterDataChange" workflow and is applied only when approved; approvers see
/// the files attached to the document; and an emailed single-use link approves once, never twice.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class MasterDataApprovalTests
{
    private readonly AccountingFixture _fixture;

    public MasterDataApprovalTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_change_waits_for_approval_by_emailed_link_and_is_then_applied()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        User approver = new($"md-approver-{companyId}", "Approver") { Email = "approver@example.com" };
        User requester = new($"md-requester-{companyId}", "Requester");
        db.Users.AddRange(approver, requester);
        Customer customer = new(companyId, "MD-C", "Steady Buyer", Scenario.Usd) { PaymentTermsDays = 30 };
        db.Customers.Add(customer);
        Company company = await db.Companies.SingleAsync(c => c.Id == companyId);
        company.ApprovalLinkBaseUrl = "https://erp.example.com/";
        await db.SaveChangesAsync();

        IMasterDataChangeService changes = scope.ServiceProvider.GetRequiredService<IMasterDataChangeService>();

        // No workflow yet: applied at once, with the value before.
        MasterDataChangeRow direct = (await changes.RequestAsync(companyId, MasterDataTarget.Customer, "md-c", "paymenttermsdays", "45", requester.Id)).Value;
        (direct.Status, direct.OldValue, direct.NewValue).Should().Be((MasterDataChangeStatus.Applied, "30", "45"));
        (await changes.RequestAsync(companyId, MasterDataTarget.Customer, "MD-C", "Code", "X", requester.Id)).Error.Code.Should().Be("Mdc.Field");
        (await changes.RequestAsync(companyId, MasterDataTarget.Customer, "MD-C", "PaymentTermsDays", "soon", requester.Id)).Error.Code.Should().Be("Mdc.Value");

        WorkflowDefinition definition = new(companyId, "MasterDataChange", "Master data");
        definition.AddStage(1, "Controller").AddEligibleUser(approver.Id);
        db.WorkflowDefinitions.Add(definition);
        await db.SaveChangesAsync();

        MasterDataChangeRow held = (await changes.RequestAsync(companyId, MasterDataTarget.Customer, "MD-C", "PaymentTermsDays", "60", requester.Id)).Value;
        held.Status.Should().Be(MasterDataChangeStatus.Pending);
        (await changes.RequestAsync(companyId, MasterDataTarget.Customer, "MD-C", "PaymentTermsDays", "90", requester.Id)).Error.Code.Should().Be("Mdc.Pending");
        (await db.Customers.AsNoTracking().SingleAsync(c => c.Id == customer.Id)).PaymentTermsDays.Should().Be(45, "nothing changes until it is approved");

        // The approver sees the supporting file and gets a link.
        (await scope.ServiceProvider.GetRequiredService<IAttachmentService>().AttachAsync(companyId, AttachmentTarget.WorkflowDocument, $"MasterDataChange/{held.Reference}", "terms-letter.pdf",
            global::System.Text.Encoding.ASCII.GetBytes("%PDF-1.4 signed terms"), requester.Id, "Signed terms")).IsSuccess.Should().BeTrue();
        IWorkflowService workflow = scope.ServiceProvider.GetRequiredService<IWorkflowService>();
        (await workflow.PendingForUserAsync(companyId, approver.Id)).Single().Attachments.Should().Be(1);
        string body = (await db.Set<OutboundMessage>().AsNoTracking().Where(m => m.CompanyId == companyId && m.Recipient == "approver@example.com").ToListAsync()).Single().Body;
        string token = body[(body.IndexOf("/api/workflow/email/", StringComparison.Ordinal) + "/api/workflow/email/".Length)..].Trim();
        body.Should().Contain("https://erp.example.com/api/workflow/email/");

        (await workflow.DescribeTokenAsync(token)).Value.Reference.Should().Be(held.Reference);
        (await workflow.ActByTokenAsync("not-a-token", WorkflowActionType.Approve, null)).Error.Code.Should().Be("Workflow.LinkInvalid");
        (await workflow.ActByTokenAsync(token, WorkflowActionType.Approve, null)).Value.Should().Be(WorkflowStatus.Approved);
        (await workflow.ActByTokenAsync(token, WorkflowActionType.Approve, null)).Error.Code.Should().Be("Workflow.LinkInvalid", "a link works once");

        await using AsyncServiceScope check = provider.CreateAsyncScope();
        One4AllDbContext fresh = check.ServiceProvider.GetRequiredService<One4AllDbContext>();
        (await fresh.Customers.AsNoTracking().SingleAsync(c => c.Id == customer.Id)).PaymentTermsDays.Should().Be(60);
        MasterDataChange applied = await fresh.MasterDataChanges.AsNoTracking().SingleAsync(c => c.Reference == held.Reference);
        (applied.Status, applied.OldValue, applied.DecidedByUserId).Should().Be((MasterDataChangeStatus.Applied, "45", approver.Id));
    }
}
