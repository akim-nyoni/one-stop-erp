using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Vouchers;
using One4All.Application.Abstractions.Workflow;
using One4All.Application.Vouchers;
using One4All.Core.Security;
using One4All.Core.Vouchers;
using One4All.Core.Workflow;
using One4All.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Thirteenth audit #24: the requester and approvers discuss a document in its approval thread, which reads with the decisions in time order.</summary>
[Collection(AccountingCollection.Name)]
public sealed class WorkflowCommentTests
{
    private const long Clerk = 91;

    private readonly AccountingFixture _fixture;

    public WorkflowCommentTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Participants_comment_and_the_thread_reads_in_order()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        string tag = Guid.NewGuid().ToString("N")[..8];
        User finance = new($"wfc-{tag}", "Finance manager"), outsider = new($"wfo-{tag}", "Someone else");
        db.Users.AddRange(finance, outsider);
        await db.SaveChangesAsync();
        WorkflowDefinition definition = new(companyId, WorkflowDocumentTypes.AccountingVoucher, "Vouchers");
        definition.AddStage(1, "Finance").AddEligibleUser(finance.Id);
        db.WorkflowDefinitions.Add(definition);
        await db.SaveChangesAsync();

        PreparedVoucher voucher = (await scope.ServiceProvider.GetRequiredService<IAccountingVoucherService>().PrepareAsync(companyId,
            new VoucherRequest(AccountingVoucherType.Payment, new DateOnly(2026, 3, 12), "Office Mart", "1100", null, [new VoucherLineRequest("6800", 80m, "Toner")], Clerk))).Value;
        long instance = await db.WorkflowInstances.Where(i => i.CompanyId == companyId && i.DocumentReference == voucher.Number).Select(i => i.Id).SingleAsync();
        IWorkflowService workflow = scope.ServiceProvider.GetRequiredService<IWorkflowService>();

        (await workflow.CommentAsync(instance, finance.Id, "Which order is this toner for?")).IsSuccess.Should().BeTrue("an approver of the current stage takes part");
        (await workflow.CommentAsync(instance, Clerk, "PO-1182, delivered last week.")).IsSuccess.Should().BeTrue("the requester takes part");
        (await workflow.CommentAsync(instance, outsider.Id, "Me too")).Error.Code.Should().Be("Workflow.NotParticipant");
        (await workflow.CommentAsync(instance, Clerk, "  ")).Error.Code.Should().Be("Workflow.CommentText");
        (await workflow.ActAsync(instance, finance.Id, WorkflowActionType.Approve, "Thanks")).IsSuccess.Should().BeTrue();

        IReadOnlyList<WorkflowThreadEntry> thread = (await workflow.ThreadAsync(instance)).Value;
        thread.Select(e => (e.UserId, e.Kind, e.Text)).Should().Equal(
            (finance.Id, "Comment", "Which order is this toner for?"), (Clerk, "Comment", "PO-1182, delivered last week."), (finance.Id, "Approve", "Thanks"));
        thread[0].UserName.Should().Be(finance.Username);
    }
}
