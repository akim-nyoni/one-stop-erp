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
/// Covers hard stock reservations for sales orders: orders reserve free stock first come first served, reserved stock is refused to any
/// other issue, the reserving order despatches it, and cancelling an order releases what it held.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class StockReservationTests
{
    private readonly AccountingFixture _fixture;

    public StockReservationTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Reserved_stock_is_held_for_its_order_until_despatched_or_released()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Item fan = new(companyId, "SR-FAN", "Fan") { CostOfSalesAccountCode = "5000" };
        Warehouse shop = new(companyId, "SR-WH", "Shop"), depot = new(companyId, "SR-DEP", "Depot");
        Customer customer = new(companyId, "SR-C", "Reserving Customer", Scenario.Usd);
        db.AddRange(fan, shop, depot, customer);
        await db.SaveChangesAsync();
        IInventoryService inventory = scope.ServiceProvider.GetRequiredService<IInventoryService>();
        (await inventory.ReceiveAsync(new ReceiveStockRequest
        {
            CompanyId = companyId, ItemId = fan.Id, WarehouseId = shop.Id, Date = new DateOnly(2026, 3, 1), Quantity = 10m, UnitCost = 20m, CreditAccountCode = "1100",
        })).IsSuccess.Should().BeTrue();

        ISalesOrderService orders = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();
        async Task<long> Order(string number) => (await orders.CreateOrderAsync(new CreateSalesOrderRequest
        {
            CompanyId = companyId, CustomerId = customer.Id, OrderNumber = number, OrderDate = new DateOnly(2026, 3, 2), Currency = Scenario.Usd,
            Lines = [new SalesOrderLineRequest(fan.Id, "Fan", 6m, 50m, null, "4000")],
        })).Value.SalesOrderId;
        long first = await Order("SR-1"), second = await Order("SR-2");
        (await orders.ReserveAsync(companyId, first, shop.Id)).Value.Should().Be(new StockReservationResult(6m, 0m));
        (await orders.ReserveAsync(companyId, second, shop.Id)).Value.Should().Be(new StockReservationResult(4m, 2m), "only 4 are left free");
        (await orders.ReserveAsync(companyId, second, depot.Id)).Value.Should().Be(new StockReservationResult(0m, 2m), "a line reserves in one warehouse");

        IssueStockRequest TillSale() => new() { CompanyId = companyId, ItemId = fan.Id, WarehouseId = shop.Id, Date = new DateOnly(2026, 3, 3), Quantity = 1m, Reference = "TILL-1" };
        (await inventory.IssueAsync(TillSale())).Error.Code.Should().Be("Inv.Reserved");

        long firstLine = await db.Set<SalesOrderLine>().Where(l => l.SalesOrderId == first).Select(l => l.Id).SingleAsync();
        (await orders.DespatchAsync(new DespatchRequest
        {
            CompanyId = companyId, SalesOrderId = first, WarehouseId = shop.Id, Date = new DateOnly(2026, 3, 3), Reference = "SR-D1", Lines = [new DespatchLine(firstLine, 6m)],
        })).IsSuccess.Should().BeTrue("the order takes its own reservation");
        (await db.Set<SalesOrderLine>().AsNoTracking().SingleAsync(l => l.Id == firstLine)).ReservedQuantity.Should().Be(0m);
        (await inventory.IssueAsync(TillSale())).Error.Code.Should().Be("Inv.Reserved", "the 4 left are the second order's");

        (await orders.CancelOrderAsync(companyId, second, "Customer changed their mind")).IsSuccess.Should().BeTrue();
        (await inventory.IssueAsync(TillSale())).IsSuccess.Should().BeTrue("cancelling released the reservation");
    }
}
