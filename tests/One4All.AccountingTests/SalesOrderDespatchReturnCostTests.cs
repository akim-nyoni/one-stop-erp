using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.SalesOrders;
using One4All.Core.Inventory;
using One4All.Core.SalesOrders;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Audit #21: goods returned from a despatch come back at what they cost when the order line was despatched, not at today's average; and the
/// return is one unit of work, so a kit whose later component cannot be received back leaves none of its components received and nothing posted.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class SalesOrderDespatchReturnCostTests
{
    private readonly AccountingFixture _fixture;

    public SalesOrderDespatchReturnCostTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_despatch_return_comes_back_at_the_despatch_cost_and_all_or_nothing()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer customer = new(companyId, "DRC-C", "Buyer", Scenario.Usd);
        Item gadget = new(companyId, "DRC-GADGET", "Gadget") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
        Item jar = new(companyId, "DRC-JAR", "Jar") { CostOfSalesAccountCode = "5000" };
        Item lid = new(companyId, "DRC-LID", "Lid") { CostOfSalesAccountCode = "5000" };
        Item pack = new(companyId, "DRC-PACK", "Jar pack", ItemType.Kit);
        Warehouse store = new(companyId, "DRC-WH", "Store");
        db.AddRange(customer, gadget, jar, lid, pack, store);
        await db.SaveChangesAsync();
        BillOfMaterials bom = new(companyId, "DRC-PACK", pack.Id, 1m);
        bom.AddLine(jar.Id, 1m);
        bom.AddLine(lid.Id, 1m);
        db.BillsOfMaterials.Add(bom);
        await db.SaveChangesAsync();

        IInventoryService inventory = scope.ServiceProvider.GetRequiredService<IInventoryService>();
        async Task Receive(Item item, decimal quantity, decimal cost) => (await inventory.ReceiveAsync(new ReceiveStockRequest
        {
            CompanyId = companyId, ItemId = item.Id, WarehouseId = store.Id, Date = new DateOnly(2026, 5, 1), Quantity = quantity, UnitCost = cost, CreditAccountCode = "1100",
        })).IsSuccess.Should().BeTrue();
        await Receive(gadget, 10m, 2m);
        await Receive(jar, 10m, 3m);
        await Receive(lid, 10m, 1m);

        ISalesOrderService orders = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();
        long orderId = (await orders.CreateOrderAsync(new CreateSalesOrderRequest
        {
            CompanyId = companyId, CustomerId = customer.Id, OrderNumber = "DRC-SO1", OrderDate = new DateOnly(2026, 5, 2), Currency = Scenario.Usd,
            Lines = [new SalesOrderLineRequest(gadget.Id, "Gadget", 5m, 9m, null, "4000"), new SalesOrderLineRequest(pack.Id, "Jar pack", 2m, 10m, null, "4000")],
        })).Value.SalesOrderId;
        List<SalesOrderLine> lines = (await db.SalesOrders.AsNoTracking().Include(o => o.Lines).SingleAsync(o => o.Id == orderId)).Lines.ToList();
        long gadgetLine = lines.Single(l => l.ItemId == gadget.Id).Id, packLine = lines.Single(l => l.ItemId == pack.Id).Id;
        (await orders.DespatchAsync(new DespatchRequest
        {
            CompanyId = companyId, SalesOrderId = orderId, WarehouseId = store.Id, Date = new DateOnly(2026, 5, 3), Reference = "DRC-D1",
            Lines = [new DespatchLine(gadgetLine, 5m), new DespatchLine(packLine, 2m)],
        })).IsSuccess.Should().BeTrue();

        // Ten more gadgets at 8 lift the average to (5 x 2 + 10 x 8) / 15 = 6; two returned come back at the 2 they were despatched at.
        await Receive(gadget, 10m, 8m);
        decimal back = (await orders.ReturnDespatchAsync(companyId, orderId, gadgetLine, store.Id, new DateOnly(2026, 5, 4), 2m, "Unwanted")).Value;
        back.Should().Be(4m, "2 at the despatch cost of 2, not at today's average of 6");

        // The lid's cost of sales account is gone by the time the pack comes back: the lid cannot be received, so the jar is not received either.
        await db.Items.Where(i => i.Id == lid.Id).ExecuteUpdateAsync(u => u.SetProperty(i => i.CostOfSalesAccountCode, "NOPE-9"));
        db.ChangeTracker.Clear();
        (await orders.ReturnDespatchAsync(companyId, orderId, packLine, store.Id, new DateOnly(2026, 5, 4), 1m, "Broken")).IsFailure.Should().BeTrue();

        await using AsyncServiceScope check = provider.CreateAsyncScope();
        One4AllDbContext fresh = check.ServiceProvider.GetRequiredService<One4AllDbContext>();
        (await fresh.StockBalances.AsNoTracking().SingleAsync(b => b.ItemId == jar.Id && b.WarehouseId == store.Id)).QuantityOnHand.Should().Be(8m, "the jar's receipt was undone");
        (await fresh.Set<StockMovement>().AsNoTracking().AnyAsync(m => m.CompanyId == companyId && m.ItemId == jar.Id && m.Type == StockMovementType.Receipt && m.Reference == "RET:DRC-SO1"))
            .Should().BeFalse();
        (await fresh.Set<SalesOrderLine>().AsNoTracking().SingleAsync(l => l.Id == packLine)).DespatchedQuantity.Should().Be(2m);
    }
}
