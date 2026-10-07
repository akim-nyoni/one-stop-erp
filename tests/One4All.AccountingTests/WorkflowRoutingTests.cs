using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Municipal;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Abstractions.Workflow;
using One4All.Core.Municipal;
using One4All.Core.Security;
using One4All.Core.Trading;
using One4All.Core.Workflow;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers approval workflows for payment runs, billing runs, credit notes and write-offs: each starts its workflow when the company defines
/// one and cannot then be approved directly; approval through the workflow pays the run, posts the credit note or writes the debt off;
/// rejection cancels the run, leaves the ledger untouched or withdraws the request; a billing run approved through its workflow is
/// authorised and posts through the resumable posting.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class WorkflowRoutingTests
{
    private readonly AccountingFixture _fixture;

    public WorkflowRoutingTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Runs_credit_notes_and_write_offs_are_approved_through_their_workflows()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        string tag = Guid.NewGuid().ToString("N")[..8];
        User clerk = new($"wrc{tag}", "Clerk"), boss = new($"wrb{tag}", "Boss");
        db.AddRange(clerk, boss);
        Supplier supplier = new(companyId, "WR-SUP", "Supplier", Scenario.Usd);
        Customer customer = new(companyId, "WR-CUS", "Customer", Scenario.Usd);
        db.AddRange(supplier, customer);
        await db.SaveChangesAsync();
        foreach (string type in new[] { WorkflowDocumentTypes.PaymentRun, WorkflowDocumentTypes.BillingRun, WorkflowDocumentTypes.CreditNote, WorkflowDocumentTypes.WriteOff })
        {
            WorkflowDefinition definition = new(companyId, type, type);
            definition.AddStage(1, "Boss").AddEligibleUser(boss.Id);
            db.WorkflowDefinitions.Add(definition);
        }

        await db.SaveChangesAsync();
        IWorkflowService workflow = scope.ServiceProvider.GetRequiredService<IWorkflowService>();
        async Task<long> InstanceFor(string type, string reference) => await db.WorkflowInstances.AsNoTracking()
            .Where(i => i.CompanyId == companyId && i.DocumentType == type && i.DocumentReference == reference).OrderByDescending(i => i.Id).Select(i => i.Id).FirstAsync();

        // Payment run: governed, so not approvable directly; the boss's approval pays it.
        (await scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>().PostInvoiceAsync(new PurchaseInvoiceRequest
        {
            CompanyId = companyId, SupplierId = supplier.Id, DocumentNumber = "WR-P1", DocumentDate = new DateOnly(2026, 3, 1), Currency = Scenario.Usd, NetAmount = 80m, ExpenseAccountCode = "5000",
        })).IsSuccess.Should().BeTrue();
        IPaymentRunService runs = scope.ServiceProvider.GetRequiredService<IPaymentRunService>();
        SavedPaymentRun run = (await runs.SaveProposalAsync(new PaymentRunRequest
        {
            CompanyId = companyId, PaymentDate = new DateOnly(2026, 4, 1), DueOnOrBefore = new DateOnly(2026, 4, 30), BankAccountCode = "1100",
        }, clerk.Id)).Value;
        (await runs.ApproveAsync(companyId, run.Number, boss.Id)).Error.Code.Should().Be("Wf.Governed");
        (await workflow.ActAsync(await InstanceFor(WorkflowDocumentTypes.PaymentRun, run.Number), boss.Id, WorkflowActionType.Approve, null)).Value.Should().Be(WorkflowStatus.Approved);
        (await runs.GetAsync(companyId, run.Number)).Value.Status.Should().Be(PaymentRunStatus.Paid);

        // Credit notes: held until approved; a rejected one never reaches the ledger.
        ICreditNoteApprovalService credits = scope.ServiceProvider.GetRequiredService<ICreditNoteApprovalService>();
        SalesCreditNoteRequest Credit(string number) => new()
        {
            CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = number, DocumentDate = new DateOnly(2026, 4, 2), Currency = Scenario.Usd, NetAmount = 25m, SalesAccountCode = "4000",
        };
        (await credits.SubmitAsync(Credit("WR-CN1"), clerk.Id)).Value.Posted.Should().BeFalse();
        (await credits.SubmitAsync(Credit("WR-CN2"), clerk.Id)).Value.Posted.Should().BeFalse();
        (await db.OpenItems.AnyAsync(i => i.CompanyId == companyId && i.DocumentNumber.StartsWith("WR-CN"))).Should().BeFalse();
        (await workflow.ActAsync(await InstanceFor(WorkflowDocumentTypes.CreditNote, "WR-CN1"), boss.Id, WorkflowActionType.Approve, null)).IsSuccess.Should().BeTrue();
        (await workflow.ActAsync(await InstanceFor(WorkflowDocumentTypes.CreditNote, "WR-CN2"), boss.Id, WorkflowActionType.Reject, "Not justified")).IsSuccess.Should().BeTrue();
        (await db.OpenItems.AsNoTracking().Where(i => i.CompanyId == companyId && i.DocumentNumber.StartsWith("WR-CN")).Select(i => i.DocumentNumber).ToListAsync()).Should().Equal("WR-CN1");
        (await db.Set<PendingCreditNote>().AsNoTracking().Where(p => p.CompanyId == companyId).OrderBy(p => p.DocumentNumber).Select(p => p.Status).ToListAsync())
            .Should().Equal(PendingCreditNoteStatus.Posted, PendingCreditNoteStatus.Rejected);

        // Write-off: rejected withdraws the request; asked again and approved, the debt is written off.
        (await scope.ServiceProvider.GetRequiredService<ISalesLedgerService>().PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = "WR-INV", DocumentDate = new DateOnly(2026, 1, 2), Currency = Scenario.Usd, NetAmount = 90m, SalesAccountCode = "4000",
        })).IsSuccess.Should().BeTrue();
        IDebtCollectionService debts = scope.ServiceProvider.GetRequiredService<IDebtCollectionService>();
        string caseNumber = (await debts.HandOverAsync(companyId, customer.Id, "Collectors Ltd", new DateOnly(2026, 3, 15))).Value;
        (await debts.RequestWriteOffAsync(companyId, caseNumber, clerk.Id, "Gone away", "6100")).IsSuccess.Should().BeTrue();
        (await debts.ApproveWriteOffAsync(companyId, caseNumber, boss.Id, new DateOnly(2026, 4, 3), "6100")).Error.Code.Should().Be("Wf.Governed");
        (await workflow.ActAsync(await InstanceFor(WorkflowDocumentTypes.WriteOff, caseNumber), boss.Id, WorkflowActionType.Reject, "Keep chasing")).IsSuccess.Should().BeTrue();
        (await db.Set<DebtCase>().AsNoTracking().SingleAsync(c => c.CompanyId == companyId && c.CaseNumber == caseNumber)).WriteOffRequestedByUserId.Should().BeNull();
        (await debts.RequestWriteOffAsync(companyId, caseNumber, clerk.Id, "Liquidated", "6100")).IsSuccess.Should().BeTrue();
        (await workflow.ActAsync(await InstanceFor(WorkflowDocumentTypes.WriteOff, caseNumber), boss.Id, WorkflowActionType.Approve, null)).IsSuccess.Should().BeTrue();
        (await db.Set<DebtCase>().AsNoTracking().SingleAsync(c => c.CompanyId == companyId && c.CaseNumber == caseNumber)).Stage.Should().Be(DebtCaseStage.WrittenOff);

        // Billing run: approved through its workflow it is authorised, and posts through the resumable posting.
        Tariff refuse = new(companyId, "WR-REF", "Refuse", ServiceType.Refuse, TariffType.Flat, new DateOnly(2026, 1, 1), "4000") { FixedCharge = 15m };
        db.Tariffs.Add(refuse);
        await db.SaveChangesAsync();
        Property stand = new(companyId, "WR-S1", "Ward 1", customer.Id);
        stand.AddService(ServiceType.Refuse, refuse.Id, new DateOnly(2026, 1, 1));
        db.Properties.Add(stand);
        await db.SaveChangesAsync();
        IBillingRunService billing = scope.ServiceProvider.GetRequiredService<IBillingRunService>();
        BillingRunSummary proposed = (await billing.ProposeAsync(new BillingRunProposal
        {
            CompanyId = companyId, ServiceType = ServiceType.Refuse, BillDate = new DateOnly(2026, 4, 30), InvoicePrefix = "WRB", UserId = clerk.Id,
        })).Value;
        (await billing.ApproveAsync(companyId, proposed.RunNumber, boss.Id)).Error.Code.Should().Be("Wf.Governed");
        (await workflow.ActAsync(await InstanceFor(WorkflowDocumentTypes.BillingRun, proposed.RunNumber), boss.Id, WorkflowActionType.Approve, null)).IsSuccess.Should().BeTrue();
        BillingRunSummary posted = (await billing.ResumeAsync(companyId, proposed.RunNumber, new BillingPostOptions())).Value;
        (posted.Status, posted.Posted).Should().Be((BillingRunStatus.Posted, 1));
    }
}
