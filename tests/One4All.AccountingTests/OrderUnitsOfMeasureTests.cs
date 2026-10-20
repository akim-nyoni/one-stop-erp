using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Procurement;
using One4All.Application.Abstractions.SalesOrders;
using One4All.Core.Inventory;
using One4All.Core.Procurement;
using One4All.Core.SalesOrders;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// An item kept in eaches is bought in boxes of 12 and sold in boxes too: 2 boxes at 120 become 24 eaches at 10 on the purchase order, 1 box at 180
/// becomes 12 at 15 on the sales order, both remembering the box; a unit the item does not have is refused.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class OrderUnitsOfMeasureTests
{
    private readonly AccountingFixture _fixture;

    public OrderUnitsOfMeasureTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Orders_are_keyed_in_packs_and_kept_in_the_items_own_unit()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Item item = new(companyId, "UOM-PEN", "Pen") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
        Supplier supplier = new(companyId, "UOM-S", "Stationer", Scenario.Usd);
        Customer customer = new(companyId, "UOM-C", "Office", Scenario.Usd);
        db.AddRange(item, supplier, customer);
        await db.SaveChangesAsync();
        db.Add(new ItemUnit(companyId, item.Id, "BOX", 12m, null));
        await db.SaveChangesAsync();

        IProcurementService procurement = scope.ServiceProvider.GetRequiredService<IProcurementService>();
        CreatePurchaseOrderRequest Po(string number, string unit) => new()
        {
            CompanyId = companyId, SupplierId = supplier.Id, OrderNumber = number, OrderDate = new DateOnly(2026, 5, 1), Currency = Scenario.Usd,
            Lines = [new PurchaseOrderLineRequest(item.Id, "Pens", 2m, 120m, UnitCode: unit)],
        };
        (await procurement.CreatePurchaseOrderAsync(Po("UOM-PO0", "CASE"))).Error.Code.Should().Be("Inv.UnitUnknown");
        (await procurement.CreatePurchaseOrderAsync(Po("UOM-PO1", "box"))).IsSuccess.Should().BeTrue();
        PurchaseOrderLine bought = await db.Set<PurchaseOrderLine>().AsNoTracking()
            .SingleAsync(l => db.Set<PurchaseOrder>().Any(o => o.Id == l.PurchaseOrderId && o.CompanyId == companyId && o.OrderNumber == "UOM-PO1"));
        (bought.OrderedQuantity, bought.UnitPrice, bought.UnitCode, bought.UnitQuantity, bought.PricePerUnit).Should().Be((24m, 10m, "BOX", 2m, 120m));

        ISalesOrderService orders = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();
        long orderId = (await orders.CreateOrderAsync(new CreateSalesOrderRequest
        {
            CompanyId = companyId, CustomerId = customer.Id, OrderNumber = "UOM-SO1", OrderDate = new DateOnly(2026, 5, 2), Currency = Scenario.Usd,
            Lines = [new SalesOrderLineRequest(item.Id, "Pens", 1m, 180m, null, "4000", "BOX")],
        })).Value.SalesOrderId;
        SalesOrderLine sold = await db.Set<SalesOrderLine>().AsNoTracking().SingleAsync(l => l.SalesOrderId == orderId);
        (sold.OrderedQuantity, sold.UnitPrice, sold.UnitCode, sold.UnitFactor).Should().Be((12m, 15m, "BOX", 12m));
    }
}
