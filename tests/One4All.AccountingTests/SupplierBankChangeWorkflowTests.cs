using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Abstractions.Workflow;
using One4All.Core.Security;
using One4All.Core.Trading;
using One4All.Core.Workflow;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Thirteenth audit #22: a supplier bank-detail change goes through the company's workflow when one is defined, and only its inbox decides it.</summary>
[Collection(AccountingCollection.Name)]
public sealed class SupplierBankChangeWorkflowTests
{
    private const long Clerk = 81;
    private const long Manager = 82;

    private readonly AccountingFixture _fixture;

    public SupplierBankChangeWorkflowTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Bank_detail_changes_follow_the_workflow_when_one_is_defined()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Supplier supplier = new(companyId, "SBW-1", "Mutare Timber", Scenario.Usd) { BankName = "CBZ", BankAccountNumber = "111" };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();
        ISupplierBankDetailsService bank = scope.ServiceProvider.GetRequiredService<ISupplierBankDetailsService>();

        // Without a workflow: the second user approves directly.
        long first = (await bank.RequestChangeAsync(companyId, supplier.Id, new SupplierBankDetails("Mutare Timber", "222", "CBZ"), Clerk)).Value;
        (await bank.ApproveAsync(companyId, first, Manager)).IsSuccess.Should().BeTrue();

        string tag = Guid.NewGuid().ToString("N")[..8];
        User treasurer = new($"sbw-{tag}", "Treasurer");
        db.Users.Add(treasurer);
        await db.SaveChangesAsync();
        WorkflowDefinition definition = new(companyId, WorkflowDocumentTypes.SupplierBankChange, "Bank details");
        definition.AddStage(1, "Treasury").AddEligibleUser(treasurer.Id);
        db.WorkflowDefinitions.Add(definition);
        await db.SaveChangesAsync();
        IWorkflowService workflow = scope.ServiceProvider.GetRequiredService<IWorkflowService>();
        async Task<long> Pending(long changeId) => await db.WorkflowInstances.AsNoTracking()
            .Where(i => i.CompanyId == companyId && i.DocumentType == WorkflowDocumentTypes.SupplierBankChange && i.DocumentReference == "SBC-" + changeId).Select(i => i.Id).SingleAsync();

        // With one: no direct approval or rejection; the inbox approval changes the supplier.
        long second = (await bank.RequestChangeAsync(companyId, supplier.Id, new SupplierBankDetails("Mutare Timber", "333", "Stanbic"), Clerk)).Value;
        (await bank.ApproveAsync(companyId, second, Manager)).Error.Code.Should().Be("Wf.Governed");
        (await bank.RejectAsync(companyId, second, Manager, "no")).Error.Code.Should().Be("Wf.Governed");
        (await workflow.ActAsync(await Pending(second), treasurer.Id, WorkflowActionType.Approve, null)).Value.Should().Be(WorkflowStatus.Approved);
        Supplier changed = await db.Suppliers.AsNoTracking().SingleAsync(s => s.Id == supplier.Id);
        (changed.BankAccountNumber, changed.BankName).Should().Be(("333", "Stanbic"));

        // A rejection in the inbox leaves the supplier as it was, with the reason on the change.
        long third = (await bank.RequestChangeAsync(companyId, supplier.Id, new SupplierBankDetails("Mutare Timber", "999", "Unknown"), Clerk)).Value;
        (await workflow.ActAsync(await Pending(third), treasurer.Id, WorkflowActionType.Reject, "Not on letterhead")).IsSuccess.Should().BeTrue();
        SupplierBankChange rejected = await db.Set<SupplierBankChange>().AsNoTracking().SingleAsync(c => c.Id == third);
        (rejected.Status, rejected.RejectionReason).Should().Be((BankChangeStatus.Rejected, "Not on letterhead"));
        (await db.Suppliers.AsNoTracking().SingleAsync(s => s.Id == supplier.Id)).BankAccountNumber.Should().Be("333");
    }
}
