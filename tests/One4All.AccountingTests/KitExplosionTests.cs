using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.SalesOrders;
using One4All.Core.Inventory;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers kits sold (brief §4.2: phantom kits): despatching a kit issues the components of its bill of materials, scaled from the batch and with
/// kits inside kits exploded, at their cost; a return puts the components back.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class KitExplosionTests
{
    private readonly AccountingFixture _fixture;

    public KitExplosionTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Despatching_a_kit_issues_its_components_and_a_return_restocks_them()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer customer = new(companyId, "KX-C", "Gift Buyer", Scenario.Usd);
        Item jar = new(companyId, "KX-JAR", "Jam jar") { CostOfSalesAccountCode = "5000" };
        Item basket = new(companyId, "KX-BASKET", "Basket") { CostOfSalesAccountCode = "5000" };
        Item card = new(companyId, "KX-CARD", "Greeting card") { CostOfSalesAccountCode = "5000" };
        Item hamper = new(companyId, "KX-HAMPER", "Hamper", ItemType.Kit);
        Item giftSet = new(companyId, "KX-GIFT", "Gift set", ItemType.Kit);
        Warehouse store = new(companyId, "KX-WH", "Store");
        db.AddRange(customer, jar, basket, card, hamper, giftSet, store);
        await db.SaveChangesAsync();

        // Two hampers per batch need four jars and two baskets; a gift set is a hamper and a card.
        BillOfMaterials hamperBom = new(companyId, "KX-HAMPER", hamper.Id, 2m);
        hamperBom.AddLine(jar.Id, 4m);
        hamperBom.AddLine(basket.Id, 2m);
        BillOfMaterials giftBom = new(companyId, "KX-GIFT", giftSet.Id, 1m);
        giftBom.AddLine(hamper.Id, 1m);
        giftBom.AddLine(card.Id, 1m);
        db.BillsOfMaterials.AddRange(hamperBom, giftBom);
        await db.SaveChangesAsync();

        IInventoryService inventory = scope.ServiceProvider.GetRequiredService<IInventoryService>();
        foreach ((Item item, decimal cost) in new[] { (jar, 3m), (basket, 10m), (card, 1m) })
        {
            (await inventory.ReceiveAsync(new ReceiveStockRequest
            {
                CompanyId = companyId, ItemId = item.Id, WarehouseId = store.Id, Date = new DateOnly(2026, 3, 1), Quantity = 100m, UnitCost = cost, CreditAccountCode = "1100",
            })).IsSuccess.Should().BeTrue();
        }

        ISalesOrderService orders = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();
        long orderId = (await orders.CreateOrderAsync(new CreateSalesOrderRequest
        {
            CompanyId = companyId, CustomerId = customer.Id, OrderNumber = "KX-O1", OrderDate = new DateOnly(2026, 3, 2), Currency = Scenario.Usd,
            Lines = [new SalesOrderLineRequest(giftSet.Id, "Gift set", 3m, 50m, null, "4000")],
        })).Value.SalesOrderId;
        long lineId = (await db.SalesOrders.AsNoTracking().Include(o => o.Lines).SingleAsync(o => o.Id == orderId)).Lines[0].Id;
        (await orders.DespatchAsync(new DespatchRequest
        {
            CompanyId = companyId, SalesOrderId = orderId, WarehouseId = store.Id, Date = new DateOnly(2026, 3, 5), Reference = "KX-D1", Lines = [new DespatchLine(lineId, 3m)],
        })).IsSuccess.Should().BeTrue();

        async Task<Dictionary<long, decimal>> OnHand()
        {
            await using AsyncServiceScope check = provider.CreateAsyncScope();
            return await check.ServiceProvider.GetRequiredService<One4AllDbContext>().StockBalances.AsNoTracking().Where(b => b.CompanyId == companyId && b.WarehouseId == store.Id)
                .ToDictionaryAsync(b => b.ItemId, b => b.QuantityOnHand);
        }

        Dictionary<long, decimal> after = await OnHand();
        after[jar.Id].Should().Be(94m, "three gift sets hold three hampers, each two jars");
        after[basket.Id].Should().Be(97m);
        after[card.Id].Should().Be(97m);
        after.Should().NotContainKeys([hamper.Id, giftSet.Id], "a kit is not stocked itself");
        (await db.Set<StockMovement>().AsNoTracking().Where(m => m.CompanyId == companyId && m.Reference == "KX-D1").SumAsync(m => -m.Value))
            .Should().Be(6m * 3m + 3m * 10m + 3m * 1m);

        decimal returned = (await orders.ReturnDespatchAsync(companyId, orderId, lineId, store.Id, new DateOnly(2026, 3, 6), 1m, "Damaged box")).Value;
        returned.Should().Be(2m * 3m + 10m + 1m);
        Dictionary<long, decimal> restocked = await OnHand();
        restocked[jar.Id].Should().Be(96m);
        restocked[basket.Id].Should().Be(98m);
        restocked[card.Id].Should().Be(98m);
    }
}
