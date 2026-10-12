using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.System;
using One4All.Application.Abstractions.Vouchers;
using One4All.Application.Abstractions.Workflow;
using One4All.Application.Vouchers;
using One4All.Core.Security;
using One4All.Core.System;
using One4All.Core.Vouchers;
using One4All.Core.Workflow;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers the voucher's checked-by signature, supporting documents attached to it, and approval through the workflow inbox.</summary>
[Collection(AccountingCollection.Name)]
public sealed class VoucherCheckAndWorkflowTests
{
    private const long Clerk = 61;
    private const long Checker = 62;
    private const long Accountant = 63;

    private readonly AccountingFixture _fixture;

    public VoucherCheckAndWorkflowTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Vouchers_are_checked_carry_their_support_and_go_through_the_workflow()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        IAccountingVoucherService vouchers = scope.ServiceProvider.GetRequiredService<IAccountingVoucherService>();
        DateOnly date = new(2026, 3, 12);
        VoucherRequest Payment(string party) => new(AccountingVoucherType.Payment, date, party, "1100", null, [new VoucherLineRequest("6800", 80m, "Toner")], Clerk);

        // Checked by a second person; the checker cannot also approve; the print carries all three signatures and the support.
        PreparedVoucher pv = (await vouchers.PrepareAsync(companyId, Payment("Office Mart"))).Value;
        (await vouchers.CheckAsync(companyId, pv.VoucherId, Clerk)).Error.Code.Should().Be("Vou.SelfCheck");
        (await vouchers.CheckAsync(companyId, pv.VoucherId, Checker)).IsSuccess.Should().BeTrue();
        (await vouchers.CheckAsync(companyId, pv.VoucherId, Accountant)).Error.Code.Should().Be("Vou.AlreadyChecked");
        IAttachmentService attachments = scope.ServiceProvider.GetRequiredService<IAttachmentService>();
        byte[] pdf = [.. "%PDF-1.4\n%invoice\n"u8.ToArray()];
        long invoice = (await attachments.AttachAsync(companyId, AttachmentTarget.AccountingVoucher, pv.Number, "invoice.pdf", pdf, Clerk, "Supplier invoice")).Value;
        (await attachments.AttachAsync(companyId, AttachmentTarget.AccountingVoucher, "PV-NOPE", "invoice.pdf", pdf, Clerk, null)).IsFailure.Should().BeTrue();
        (await vouchers.ApproveAsync(companyId, pv.VoucherId, Checker)).Error.Code.Should().Be("Vou.CheckerApproval");
        (await vouchers.ApproveAsync(companyId, pv.VoucherId, Accountant)).IsSuccess.Should().BeTrue();
        string printed = (await vouchers.RenderAsync(companyId, pv.VoucherId)).Value;
        printed.Should().Contain("Checked by: User 62").And.Contain("Approved by: User 63").And.Contain("Supporting documents: 1 attached");
        (await attachments.RemoveAsync(companyId, invoice, Clerk, "wrong file")).Error.Code.Should().Be("Att.Posted");

        // With a workflow defined, a prepared voucher waits in the inbox: no direct approval; the inbox approval posts it, a rejection cancels it.
        string tag = Guid.NewGuid().ToString("N")[..8];
        User finance = new($"vcw-{tag}", "Finance manager");
        db.Users.Add(finance);
        await db.SaveChangesAsync();
        WorkflowDefinition definition = new(companyId, WorkflowDocumentTypes.AccountingVoucher, "Vouchers");
        definition.AddStage(1, "Finance").AddEligibleUser(finance.Id);
        db.WorkflowDefinitions.Add(definition);
        await db.SaveChangesAsync();
        IWorkflowService workflow = scope.ServiceProvider.GetRequiredService<IWorkflowService>();
        async Task<long> Pending(string number) => await db.WorkflowInstances.AsNoTracking()
            .Where(i => i.CompanyId == companyId && i.DocumentType == WorkflowDocumentTypes.AccountingVoucher && i.DocumentReference == number).Select(i => i.Id).SingleAsync();

        PreparedVoucher governed = (await vouchers.PrepareAsync(companyId, Payment("Courier Co"))).Value;
        (await vouchers.ApproveAsync(companyId, governed.VoucherId, Accountant)).Error.Code.Should().Be("Wf.Governed");
        (await workflow.ActAsync(await Pending(governed.Number), finance.Id, WorkflowActionType.Approve, null)).Value.Should().Be(WorkflowStatus.Approved);
        AccountingVoucher posted = await db.Set<AccountingVoucher>().AsNoTracking().SingleAsync(v => v.Id == governed.VoucherId);
        (posted.Status, posted.ApprovedByUserId).Should().Be((AccountingVoucherStatus.Posted, (long?)finance.Id));
        posted.JournalNumber.Should().NotBeNull();

        PreparedVoucher turnedDown = (await vouchers.PrepareAsync(companyId, Payment("Dubious Ltd"))).Value;
        (await workflow.ActAsync(await Pending(turnedDown.Number), finance.Id, WorkflowActionType.Reject, "No order")).IsSuccess.Should().BeTrue();
        (await db.Set<AccountingVoucher>().AsNoTracking().SingleAsync(v => v.Id == turnedDown.VoucherId)).Status.Should().Be(AccountingVoucherStatus.Cancelled);
    }
}
