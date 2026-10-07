using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Procurement;
using One4All.Application.Abstractions.Scheduling;
using One4All.Application.Abstractions.Workflow;
using One4All.Core.Inventory;
using One4All.Core.Procurement;
using One4All.Core.Security;
using One4All.Core.Trading;
using One4All.Core.Workflow;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers purchase orders under the approval workflow and scheduled escalation: with a purchase-order workflow defined, a new order waits for
/// approval (no goods can be received); approval releases it; rejection cancels it; the escalation runs as a scheduled task.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PurchaseOrderWorkflowTests
{
    private readonly AccountingFixture _fixture;

    public PurchaseOrderWorkflowTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Orders_wait_for_their_workflow_and_escalation_is_scheduled()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        string tag = Guid.NewGuid().ToString("N")[..8];
        User buyer = new($"pow-b{tag}", "Buyer"), manager = new($"pow-m{tag}", "Manager");
        db.AddRange(buyer, manager);
        Supplier supplier = new(companyId, "POW-SUP", "Supplier", Scenario.Usd);
        Item paper = new(companyId, "POW-PAPER", "Paper") { CostOfSalesAccountCode = "5000" };
        Warehouse store = new(companyId, "POW-WH", "Store");
        db.AddRange(supplier, paper, store);
        await db.SaveChangesAsync();
        WorkflowDefinition orders = new(companyId, WorkflowDocumentTypes.PurchaseOrder, "Orders");
        orders.AddStage(1, "Manager").AddEligibleUser(manager.Id);
        db.WorkflowDefinitions.Add(orders);
        await db.SaveChangesAsync();

        IProcurementService procurement = scope.ServiceProvider.GetRequiredService<IProcurementService>();
        IWorkflowService workflow = scope.ServiceProvider.GetRequiredService<IWorkflowService>();
        async Task<long> Raise(string number) => (await procurement.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest
        {
            CompanyId = companyId, SupplierId = supplier.Id, OrderNumber = number, OrderDate = new DateOnly(2026, 4, 1), Currency = Scenario.Usd, Lines = [new PurchaseOrderLineRequest(paper.Id, "Paper", 10m, 2m)],
            RaisedByUserId = buyer.Id,
        })).Value.PurchaseOrderId;
        async Task<long> InstanceFor(string number) => await db.WorkflowInstances.AsNoTracking().Where(i => i.CompanyId == companyId && i.DocumentType == WorkflowDocumentTypes.PurchaseOrder && i.DocumentReference == number)
            .Select(i => i.Id).SingleAsync();
        async Task<Core.Common.Result<GrnResult>> Receive(long order, string grn) => await procurement.ReceiveGoodsAsync(new ReceiveGoodsRequest
        {
            CompanyId = companyId, PurchaseOrderId = order, WarehouseId = store.Id, GrnNumber = grn, Date = new DateOnly(2026, 4, 2),
            Lines = [new ReceiveLine(await db.Set<PurchaseOrderLine>().Where(l => l.PurchaseOrderId == order).Select(l => l.Id).SingleAsync(), 10m)],
        });

        long approved = await Raise("POW-1");
        (await Receive(approved, "POW-G1")).Error.Code.Should().Be("Pop.AwaitingApproval");
        (await workflow.ActAsync(await InstanceFor("POW-1"), manager.Id, WorkflowActionType.Approve, null)).Value.Should().Be(WorkflowStatus.Approved);
        (await Receive(approved, "POW-G1")).IsSuccess.Should().BeTrue();

        long rejected = await Raise("POW-2");
        (await workflow.ActAsync(await InstanceFor("POW-2"), manager.Id, WorkflowActionType.Reject, "Not needed")).IsSuccess.Should().BeTrue();
        (await db.PurchaseOrders.AsNoTracking().SingleAsync(o => o.Id == rejected)).Status.Should().Be(PurchaseOrderStatus.Cancelled);

        IScheduledTask escalation = scope.ServiceProvider.GetServices<IScheduledTask>().Single(t => t.Key == "workflow.escalate");
        (await escalation.RunAsync(companyId, DateTimeOffset.UtcNow)).Value.Should().Contain("escalated");
    }
}
