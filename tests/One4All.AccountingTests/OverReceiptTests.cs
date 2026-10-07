using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Procurement;
using One4All.Core.Inventory;
using One4All.Core.Procurement;
using One4All.Core.Security;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the over-receipt tolerance: a receipt within the company's 10% goes through; beyond it is refused unless a supervisor with the GRN
/// authorise permission (not the receiver, not a user without it) accepts it, who is recorded on the GRN; a supplier's own tolerance
/// replaces the company's.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class OverReceiptTests
{
    private readonly AccountingFixture _fixture;

    public OverReceiptTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Receipts_beyond_the_tolerance_need_an_authorised_supervisor()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        (await db.Companies.SingleAsync(c => c.Id == companyId)).OverReceiptTolerancePercent = 10m;
        Role storesSupervisor = new($"Stores supervisor {companyId}");
        storesSupervisor.Grant("Procurement.Grn", OperationFlags.View | OperationFlags.Create | OperationFlags.AuthoriseLevel1);
        db.Roles.Add(storesSupervisor);
        User receiver = new($"or-recv-{companyId}", "Receiver"), supervisor = new($"or-sup-{companyId}", "Supervisor"), clerk = new($"or-clerk-{companyId}", "Clerk");
        db.Users.AddRange(receiver, supervisor, clerk);
        Supplier supplier = new(companyId, "OR-SUP", "Mill", Scenario.Usd), generous = new(companyId, "OR-GEN", "Generous Mill", Scenario.Usd) { OverReceiptTolerancePercent = 50m };
        Item flour = new(companyId, "OR-FLOUR", "Flour") { CostOfSalesAccountCode = "5000" };
        Warehouse store = new(companyId, "OR-WH", "Store");
        db.AddRange(supplier, generous, flour, store);
        await db.SaveChangesAsync();
        supervisor.AssignRole(storesSupervisor.Id);
        await db.SaveChangesAsync();

        IProcurementService procurement = scope.ServiceProvider.GetRequiredService<IProcurementService>();
        async Task<(long Order, long Line)> Order(Supplier from, string number)
        {
            long order = (await procurement.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest
            {
                CompanyId = companyId, SupplierId = from.Id, OrderNumber = number, OrderDate = new DateOnly(2026, 4, 1), Currency = Scenario.Usd, Lines = [new PurchaseOrderLineRequest(flour.Id, "Flour", 100m, 2m)],
            })).Value.PurchaseOrderId;
            return (order, await db.Set<PurchaseOrderLine>().Where(l => l.PurchaseOrderId == order).Select(l => l.Id).SingleAsync());
        }

        ReceiveGoodsRequest Receive((long Order, long Line) po, string grn, decimal quantity, long? authorisedBy = null) => new()
        {
            CompanyId = companyId, PurchaseOrderId = po.Order, WarehouseId = store.Id, GrnNumber = grn, Date = new DateOnly(2026, 4, 2), Lines = [new ReceiveLine(po.Line, quantity)],
            ReceivedByUserId = receiver.Id, OverReceiptAuthorisedByUserId = authorisedBy,
        };

        (long, long) first = await Order(supplier, "OR-PO1");
        (await procurement.ReceiveGoodsAsync(Receive(first, "OR-G1", 105m))).IsSuccess.Should().BeTrue("within 10%");
        (await procurement.ReceiveGoodsAsync(Receive(first, "OR-G2", 10m))).Error.Code.Should().Be("Pop.OverReceipt", "115 of 100 is over 10%");
        (await procurement.ReceiveGoodsAsync(Receive(first, "OR-G2", 10m, receiver.Id))).Error.Code.Should().Be("Pop.OverReceiptSelf");
        (await procurement.ReceiveGoodsAsync(Receive(first, "OR-G2", 10m, clerk.Id))).Error.Code.Should().Be("Pop.OverReceiptNotAuthorised");
        (await procurement.ReceiveGoodsAsync(Receive(first, "OR-G2", 10m, supervisor.Id))).IsSuccess.Should().BeTrue();
        (await db.Set<GoodsReceivedNote>().AsNoTracking().SingleAsync(g => g.CompanyId == companyId && g.GrnNumber == "OR-G2")).OverReceiptAuthorisedByUserId.Should().Be(supervisor.Id);
        (await db.Set<GoodsReceivedNote>().AsNoTracking().SingleAsync(g => g.CompanyId == companyId && g.GrnNumber == "OR-G1")).OverReceiptAuthorisedByUserId.Should().BeNull();

        (long, long) second = await Order(generous, "OR-PO2");
        (await procurement.ReceiveGoodsAsync(Receive(second, "OR-G3", 140m))).IsSuccess.Should().BeTrue("the supplier's own 50%");
    }
}
