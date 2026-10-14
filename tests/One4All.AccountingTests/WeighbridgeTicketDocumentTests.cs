using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.JobCosting;
using One4All.Application.Abstractions.SalesOrders;
using One4All.Core.Inventory;
using One4All.Core.JobCosting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers weighbridge loads against sales orders and the printed ticket: a load names the customer's open order for the product (another
/// customer's order, or one without the product, is refused); the ticket prints the vehicle, order, gross, tare and net with signature lines.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class WeighbridgeTicketDocumentTests
{
    private readonly AccountingFixture _fixture;

    public WeighbridgeTicketDocumentTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Loads_go_against_orders_and_print_a_ticket()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer builder = new(companyId, "WT-B", "Ticket Builder", Scenario.Usd), other = new(companyId, "WT-O", "Other Builder", Scenario.Usd);
        Item sand = new(companyId, "WT-SAND", "River sand") { RevenueAccountCode = "4000" }, stone = new(companyId, "WT-STONE", "Stone") { RevenueAccountCode = "4000" };
        db.AddRange(builder, other, sand, stone, new WeighbridgeVehicle(companyId, "WT-1") { StoredTareKg = 9_000m });
        await db.SaveChangesAsync();

        ISalesOrderService orders = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();
        async Task Order(string number, long customer, long item) => (await orders.CreateOrderAsync(new CreateSalesOrderRequest
        {
            CompanyId = companyId, CustomerId = customer, OrderNumber = number, OrderDate = new DateOnly(2026, 3, 1), Currency = Scenario.Usd,
            Lines = [new SalesOrderLineRequest(item, "Load", 30m, 12m, null, "4000")],
        })).IsSuccess.Should().BeTrue();
        await Order("WT-SO1", builder.Id, sand.Id);
        await Order("WT-SO2", other.Id, sand.Id);
        await Order("WT-SO3", builder.Id, stone.Id);

        IWeighbridgeService bridge = scope.ServiceProvider.GetRequiredService<IWeighbridgeService>();
        FirstWeighRequest Weigh(string order) => new()
        {
            CompanyId = companyId, Direction = WeighDirection.Outbound, Vehicle = "WT-1", ItemId = sand.Id, CubicMetresPerTonne = 0.6m, CustomerId = builder.Id,
            Date = new DateOnly(2026, 3, 10), OperatorUserId = 1, Driver = "T. Moyo", SalesOrderNumber = order,
        };
        (await bridge.FirstWeighAsync(Weigh("NOPE"))).Error.Code.Should().Be("Wb.OrderNotFound");
        (await bridge.FirstWeighAsync(Weigh("WT-SO2"))).Error.Code.Should().Be("Wb.OrderMismatch");
        (await bridge.FirstWeighAsync(Weigh("WT-SO3"))).Error.Code.Should().Be("Wb.OrderMismatch");
        (long ticket, string number) = (await bridge.FirstWeighAsync(Weigh("wt-so1"))).Value;
        (await db.WeighbridgeTickets.AsNoTracking().SingleAsync(t => t.Id == ticket)).SalesOrderId.Should().NotBeNull();

        (await bridge.RenderTicketAsync(companyId, number)).Value.Should().Contain("on the bridge");
        (await bridge.SecondWeighAsync(new SecondWeighRequest { CompanyId = companyId, TicketId = ticket, WeightKg = 21_500m, DeductionKg = 500m, OperatorUserId = 1 })).IsSuccess.Should().BeTrue();
        string printed = (await bridge.RenderTicketAsync(companyId, number)).Value;
        printed.Should().Contain("WEIGHBRIDGE TICKET").And.Contain("WT-1").And.Contain("T. Moyo").And.Contain("WT-SO1").And.Contain("WT-B Ticket Builder")
            .And.Contain("21,500 kg").And.Contain("9,000 kg").And.Contain("12,000 kg").And.Contain("12.000 t").And.Contain("Driver: ____");
        (await bridge.RenderTicketAsync(companyId, "NOPE")).Error.Code.Should().Be("Wb.TicketNotFound");

        // A load from a stockpile is despatched on its order, and invoiced at the order's price when no price is given.
        sand.CostOfSalesAccountCode = "5000";
        Warehouse pile = new(companyId, "WT-PILE", "Sand stockpile");
        db.Warehouses.Add(pile);
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<One4All.Application.Abstractions.Inventory.IInventoryService>().ReceiveAsync(new One4All.Application.Abstractions.Inventory.ReceiveStockRequest
        {
            CompanyId = companyId, ItemId = sand.Id, WarehouseId = pile.Id, Date = new DateOnly(2026, 3, 1), Quantity = 100m, UnitCost = 5m, CreditAccountCode = "1100",
        })).IsSuccess.Should().BeTrue();
        FirstWeighRequest fromPile = Weigh("WT-SO1");
        (long piled, _) = (await bridge.FirstWeighAsync(new FirstWeighRequest
        {
            CompanyId = fromPile.CompanyId, Direction = fromPile.Direction, Vehicle = fromPile.Vehicle, ItemId = fromPile.ItemId, CubicMetresPerTonne = fromPile.CubicMetresPerTonne,
            CustomerId = fromPile.CustomerId, Date = fromPile.Date, OperatorUserId = 1, SalesOrderNumber = "WT-SO1", WarehouseId = pile.Id,
        })).Value;
        (await bridge.SecondWeighAsync(new SecondWeighRequest { CompanyId = companyId, TicketId = piled, WeightKg = 19_000m, OperatorUserId = 1 })).IsSuccess.Should().BeTrue();
        long so1 = await db.Set<Core.SalesOrders.SalesOrder>().Where(o => o.CompanyId == companyId && o.OrderNumber == "WT-SO1").Select(o => o.Id).SingleAsync();
        Core.SalesOrders.SalesOrderLine line = await db.Set<Core.SalesOrders.SalesOrderLine>().AsNoTracking().SingleAsync(l => l.SalesOrderId == so1);
        line.DespatchedQuantity.Should().Be(10m, "the 10 t load went out on the order");
        WeighbridgeInvoice billed = (await bridge.InvoiceTicketsAsync(new WeighbridgeInvoiceRequest
        {
            CompanyId = companyId, CustomerId = builder.Id, DocumentNumber = "WT-INV1", DocumentDate = new DateOnly(2026, 3, 31), SalesAccountCode = "4000",
        })).Value;
        billed.Product.Should().Be(22m * 12m, "both loads (12 t and 10 t) at the order's 12 a tonne");
        (await db.Set<Core.SalesOrders.SalesOrderLine>().AsNoTracking().SingleAsync(l => l.Id == line.Id)).InvoicedQuantity.Should().Be(10m, "only what was despatched on the order");
    }
}
