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
/// Covers delivery charges: a zone's base charge plus rates per kilogram and cubic metre of the goods sent, free above a goods value,
/// invoiced to the customer once per delivery.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class DeliveryChargeTests
{
    private readonly AccountingFixture _fixture;

    public DeliveryChargeTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Deliveries_are_charged_by_zone_weight_and_volume()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer customer = new(companyId, "DC-C", "Site Builder", Scenario.Usd);
        Item bag = new(companyId, "DC-BAG", "Cement bag") { CostOfSalesAccountCode = "5000", UnitWeightKg = 25m, UnitVolumeM3 = 0.02m };
        Warehouse store = new(companyId, "DC-WH", "Yard");
        db.AddRange(customer, bag, store);
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<IInventoryService>().ReceiveAsync(new ReceiveStockRequest
        {
            CompanyId = companyId, ItemId = bag.Id, WarehouseId = store.Id, Date = new DateOnly(2026, 3, 1), Quantity = 500m, UnitCost = 5m, CreditAccountCode = "1100",
        })).IsSuccess.Should().BeTrue();

        ISalesOrderService orders = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();
        async Task Ship(string order, string delivery, decimal quantity)
        {
            long id = (await orders.CreateOrderAsync(new CreateSalesOrderRequest
            {
                CompanyId = companyId, CustomerId = customer.Id, OrderNumber = order, OrderDate = new DateOnly(2026, 3, 1), Currency = Scenario.Usd,
                Lines = [new SalesOrderLineRequest(bag.Id, "Cement bag", quantity, 8m, null, "4000")],
            })).Value.SalesOrderId;
            long line = (await db.SalesOrders.AsNoTracking().Include(o => o.Lines).SingleAsync(o => o.Id == id)).Lines[0].Id;
            (await orders.DespatchAsync(new DespatchRequest
            {
                CompanyId = companyId, SalesOrderId = id, WarehouseId = store.Id, Date = new DateOnly(2026, 3, 5), Reference = delivery, Lines = [new DespatchLine(line, quantity)],
            })).IsSuccess.Should().BeTrue();
        }

        await Ship("DC-O1", "DC-D1", 20m);
        await Ship("DC-O2", "DC-D2", 200m);

        IDeliveryChargeService charges = scope.ServiceProvider.GetRequiredService<IDeliveryChargeService>();
        (await charges.QuoteAsync(companyId, "DC-D1")).Error.Code.Should().Be("Sop.NoZone");
        customer.DeliveryZone = "north";
        await db.SaveChangesAsync();
        (await charges.QuoteAsync(companyId, "DC-D1")).Error.Code.Should().Be("Sop.NoZoneRate");
        (await charges.SaveRateAsync(companyId, "North", 50m, 0.10m, 20m, 1000m)).IsSuccess.Should().BeTrue();

        // 20 bags: 500 kg and 0.4 m³ → 50 + 50 + 8 = 108 on goods of 160.
        (await charges.QuoteAsync(companyId, "DC-D1")).Value.Should().Be(new DeliveryChargeQuote("NORTH", 500m, 0.4m, 160m, 108m));
        (await charges.InvoiceAsync(companyId, "DC-D1", "DC-INV-1", new DateOnly(2026, 3, 6), "4000", null, null)).Value.Charge.Should().Be(108m);
        (await charges.InvoiceAsync(companyId, "DC-D1", "DC-INV-1B", new DateOnly(2026, 3, 6), "4000", null, null)).Error.Code.Should().Be("Sop.DeliveryCharged");
        (await db.OpenItems.AsNoTracking().SingleAsync(i => i.CompanyId == companyId && i.DocumentNumber == "DC-INV-1")).OriginalBase.Should().Be(108m);

        // 200 bags are worth 1,600 - above the 1,000 free-delivery value.
        (await charges.QuoteAsync(companyId, "DC-D2")).Value.Charge.Should().Be(0m);
        (await charges.InvoiceAsync(companyId, "DC-D2", "DC-INV-2", new DateOnly(2026, 3, 6), "4000", null, null)).Error.Code.Should().Be("Sop.FreeDelivery");
    }
}
