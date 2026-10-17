using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.SalesOrders;
using One4All.Core.Inventory;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers delivery tracking (brief §14 H 140–142): despatches raise delivery notes on vehicles and routes; proof of delivery is signed once
/// and never before despatch; OTIF judges each order by its requested date; unsigned deliveries and the load sheet are listed.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class DeliveryTrackingTests
{
    private readonly AccountingFixture _fixture;

    public DeliveryTrackingTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Deliveries_are_tracked_to_proof_of_delivery_and_reported()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        Customer customer = new(companyId, "DT-C", "Depot Buyer", Scenario.Usd);
        Item bag = new(companyId, "DT-BAG", "Cement bag") { CostOfSalesAccountCode = "5000", UnitWeightKg = 25m };
        Warehouse store = new(companyId, "DT-WH", "Yard");
        db.AddRange(customer, bag, store);
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<IInventoryService>().ReceiveAsync(new ReceiveStockRequest
        {
            CompanyId = companyId, ItemId = bag.Id, WarehouseId = store.Id, Date = new DateOnly(2026, 3, 1), Quantity = 100m, UnitCost = 5m, CreditAccountCode = "1100",
        })).IsSuccess.Should().BeTrue();

        ISalesOrderService orders = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();
        async Task<(long Id, long LineId)> Order(string number, decimal quantity, DateOnly requested)
        {
            long id = (await orders.CreateOrderAsync(new CreateSalesOrderRequest
            {
                CompanyId = companyId, CustomerId = customer.Id, OrderNumber = number, OrderDate = new DateOnly(2026, 3, 1), Currency = Scenario.Usd, RequestedDeliveryDate = requested,
                Lines = [new SalesOrderLineRequest(bag.Id, "Cement bag", quantity, 8m, null, "4000")],
            })).Value.SalesOrderId;
            return (id, (await db.SalesOrders.AsNoTracking().Include(o => o.Lines).SingleAsync(o => o.Id == id)).Lines[0].Id);
        }

        async Task Despatch((long Id, long LineId) order, string reference, DateOnly date, decimal quantity, string? vehicle, string? route, string? driver, DateOnly? planned = null)
            => (await orders.DespatchAsync(new DespatchRequest
            {
                CompanyId = companyId, SalesOrderId = order.Id, WarehouseId = store.Id, Date = date, Reference = reference, Lines = [new DespatchLine(order.LineId, quantity)],
                Vehicle = vehicle, Route = route, Driver = driver, PlannedFor = planned,
            })).IsSuccess.Should().BeTrue();

        var o1 = await Order("DT-O1", 10m, new DateOnly(2026, 3, 10));
        var o2 = await Order("DT-O2", 5m, new DateOnly(2026, 3, 15));
        var o3 = await Order("DT-O3", 2m, new DateOnly(2026, 3, 20));
        await Despatch(o1, "DT-D1", new DateOnly(2026, 3, 8), 6m, "abc123", "North", "Sam", new DateOnly(2026, 3, 9));
        await Despatch(o1, "DT-D2", new DateOnly(2026, 3, 12), 4m, "ABC123", "North", "Sam");
        await Despatch(o2, "DT-D3", new DateOnly(2026, 3, 14), 5m, "XYZ", null, null);
        await Despatch(o3, "DT-D4", new DateOnly(2026, 3, 18), 2m, null, null, null);
        (await orders.ConfirmDeliveryAsync(companyId, "DT-D1", new DateOnly(2026, 3, 9), "J Doe", "POD-1")).IsSuccess.Should().BeTrue();
        (await orders.ConfirmDeliveryAsync(companyId, "DT-D1", new DateOnly(2026, 3, 9), "J Doe", "POD-1")).Error.Code.Should().Be("Sop.DeliveryConfirmed");
        (await orders.ConfirmDeliveryAsync(companyId, "DT-D2", new DateOnly(2026, 3, 13), "J Doe", "POD-2")).IsSuccess.Should().BeTrue();
        (await orders.ConfirmDeliveryAsync(companyId, "DT-D3", new DateOnly(2026, 3, 15), "K Roe", null)).IsSuccess.Should().BeTrue();
        (await orders.ConfirmDeliveryAsync(companyId, "DT-D4", new DateOnly(2026, 3, 17), "K Roe", null)).Error.Code.Should().Be("Sop.DeliveredBeforeDespatch");

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        async Task<string> Render(string code)
        {
            Dictionary<string, object?> parameters = new() { ["FromDate"] = new DateOnly(2026, 3, 1), ["ToDate"] = new DateOnly(2026, 3, 31), ["AsAtDate"] = new DateOnly(2026, 3, 31) };
            var rendered = await runner.RenderAsync(companyId, code, parameters, ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? $"{code}: {rendered.Error.Message}" : null);
            return Encoding.UTF8.GetString(rendered.Value.Content);
        }

        (await Render("SO140")).Should().Contain("DT-O1,Depot Buyer,2026-03-10,10,6,2026-03-13,No,No,No").And.Contain("DT-O2,Depot Buyer,2026-03-15,5,5,2026-03-15,Yes,Yes,Yes")
            .And.Contain("DT-O3,Depot Buyer,2026-03-20,2,0,-,No,No,No");
        (await Render("SO141")).Should().Contain("DT-D4,DT-O3,Depot Buyer,2026-03-18,13,-,-,2").And.NotContain("DT-D1");
        string loads = await Render("SO142");
        loads.Should().Contain("2026-03-09,ABC123,North,Sam,DT-D1,DT-O1,Depot Buyer,6 × DT-BAG,150").And.Contain("2026-03-14,XYZ,-,-,DT-D3,DT-O2,Depot Buyer,5 × DT-BAG,125")
            .And.Contain("2026-03-18,Unassigned,");
    }
}
