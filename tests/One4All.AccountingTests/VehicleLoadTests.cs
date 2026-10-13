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
/// Covers vehicle loads: a 500 kg van with three drops a day takes a 250 kg delivery and a 150 kg one put in front of it as stop 1; a third of
/// 125 kg would overload it and is refused; on a bigger truck the stops limit is enforced; the van's load lists its drops in run order.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class VehicleLoadTests
{
    private readonly AccountingFixture _fixture;

    public VehicleLoadTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Deliveries_are_planned_on_vehicles_within_their_capacity_in_stop_order()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer customer = new(companyId, "VL-C", "Builder", Scenario.Usd);
        Item bag = new(companyId, "VL-BAG", "Cement bag") { CostOfSalesAccountCode = "5000", UnitWeightKg = 25m };
        Warehouse store = new(companyId, "VL-WH", "Yard");
        db.AddRange(customer, bag, store);
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<IInventoryService>().ReceiveAsync(new ReceiveStockRequest
        {
            CompanyId = companyId, ItemId = bag.Id, WarehouseId = store.Id, Date = new DateOnly(2026, 3, 1), Quantity = 100m, UnitCost = 5m, CreditAccountCode = "1100",
        })).IsSuccess.Should().BeTrue();

        ISalesOrderService orders = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();
        async Task Despatch(string reference, decimal bags)
        {
            long id = (await orders.CreateOrderAsync(new CreateSalesOrderRequest
            {
                CompanyId = companyId, CustomerId = customer.Id, OrderNumber = $"{reference}-O", OrderDate = new DateOnly(2026, 3, 1), Currency = Scenario.Usd,
                Lines = [new SalesOrderLineRequest(bag.Id, "Cement bag", bags, 8m, null, "4000")],
            })).Value.SalesOrderId;
            long line = (await db.SalesOrders.AsNoTracking().Include(o => o.Lines).SingleAsync(o => o.Id == id)).Lines[0].Id;
            (await orders.DespatchAsync(new DespatchRequest
            {
                CompanyId = companyId, SalesOrderId = id, WarehouseId = store.Id, Date = new DateOnly(2026, 3, 9), Reference = reference, Lines = [new DespatchLine(line, bags)],
            })).IsSuccess.Should().BeTrue();
        }

        await Despatch("VL-D1", 10m);   // 250 kg
        await Despatch("VL-D2", 6m);    // 150 kg
        await Despatch("VL-D3", 5m);    // 125 kg
        await Despatch("VL-D4", 1m);    // 25 kg

        IDeliveryPlanningService planning = scope.ServiceProvider.GetRequiredService<IDeliveryPlanningService>();
        (await planning.SaveVehicleAsync(companyId, new DeliveryVehicleRequest("vl van", "Panel van", 0m))).Error.Code.Should().Be("Sop.Vehicle");
        (await planning.SaveVehicleAsync(companyId, new DeliveryVehicleRequest("vl van", "Panel van", 500m, MaxStops: 3, DefaultDriver: "Tendai"))).IsSuccess.Should().BeTrue();
        (await planning.SaveVehicleAsync(companyId, new DeliveryVehicleRequest("VL TRUCK", "Truck", 5_000m, MaxStops: 1))).IsSuccess.Should().BeTrue();

        DateOnly day = new(2026, 3, 10);
        (await planning.AssignAsync(companyId, new DeliveryAssignment("VL-D1", "VL VAN", day, "North"))).IsSuccess.Should().BeTrue();
        VehicleLoad load = (await planning.AssignAsync(companyId, new DeliveryAssignment("VL-D2", "vl van", day, "North", Stop: 1))).Value;
        load.Stops.Select(s => (s.Stop, s.DeliveryReference)).Should().Equal((1, "VL-D2"), (2, "VL-D1"));
        load.WeightKg.Should().Be(400m);
        load.Utilisation.Should().Be(80m);
        (await planning.AssignAsync(companyId, new DeliveryAssignment("VL-D3", "VL VAN", day))).Error.Code.Should().Be("Sop.VehicleOverloaded");
        (await planning.AssignAsync(companyId, new DeliveryAssignment("VL-D3", "VL TRUCK", day))).IsSuccess.Should().BeTrue();
        (await planning.AssignAsync(companyId, new DeliveryAssignment("VL-D4", "VL TRUCK", day))).Error.Code.Should().Be("Sop.VehicleFull");
        (await planning.AssignAsync(companyId, new DeliveryAssignment("VL-D4", "NO SUCH", day))).Error.Code.Should().Be("Sop.VehicleNotFound");

        VehicleLoad van = (await planning.LoadAsync(companyId, "VL VAN", day)).Value;
        van.Stops.Select(s => s.DeliveryReference).Should().Equal("VL-D2", "VL-D1");
        (await db.SalesDeliveries.AsNoTracking().SingleAsync(d => d.CompanyId == companyId && d.Reference == "VL-D1")).Driver.Should().Be("Tendai");
    }
}
