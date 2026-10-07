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
/// Covers sales order amendments with history: a change makes a new version and keeps the old one with its reason, quantities cannot go below
/// what has been despatched, and a cancelled order cannot be amended.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class SalesOrderAmendmentTests
{
    private readonly AccountingFixture _fixture;

    public SalesOrderAmendmentTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Amendments_version_the_order_and_protect_what_has_been_despatched()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer customer = new(companyId, "AM-C", "Buyer", Scenario.Usd);
        Item item = new(companyId, "AM-WIDGET", "Widget") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
        Warehouse wh = new(companyId, "AMWH", "Main");
        db.Customers.Add(customer);
        db.Items.Add(item);
        db.Warehouses.Add(wh);
        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<IInventoryService>().ReceiveAsync(new ReceiveStockRequest
        {
            CompanyId = companyId, ItemId = item.Id, WarehouseId = wh.Id, Date = new DateOnly(2026, 5, 4), Quantity = 100m, UnitCost = 2m, CreditAccountCode = "1100",
        });

        ISalesOrderService sop = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();
        long orderId = (await sop.CreateOrderAsync(new CreateSalesOrderRequest
        {
            CompanyId = companyId, CustomerId = customer.Id, OrderNumber = "AM-SO1", OrderDate = new DateOnly(2026, 5, 4), Currency = Scenario.Usd,
            Lines = [new SalesOrderLineRequest(item.Id, "Widget", 20m, 5m, null, "4000")],
        })).Value.SalesOrderId;
        long lineId = await db.Set<SalesOrderLine>().Where(l => l.SalesOrderId == orderId).Select(l => l.Id).SingleAsync();
        (await sop.DespatchAsync(new DespatchRequest
        {
            CompanyId = companyId, SalesOrderId = orderId, WarehouseId = wh.Id, Date = new DateOnly(2026, 5, 5), Reference = "AM-DN1", Lines = [new DespatchLine(lineId, 8m)],
        })).IsSuccess.Should().BeTrue();

        ISalesOrderAmendmentService amendments = scope.ServiceProvider.GetRequiredService<ISalesOrderAmendmentService>();
        SalesOrderAmendmentResult amended = (await amendments.AmendAsync(companyId, orderId,
            new SalesOrderAmendment(new DateOnly(2026, 5, 6), 1, "Customer cut the order", [new SalesOrderLineChange(lineId, 12m, 5.5m)]))).Value;
        (amended.Version, amended.TotalBefore, amended.TotalAfter).Should().Be((2, 100m, 66m));

        // Below the 8 already despatched: refused, nothing changes.
        var below = await amendments.AmendAsync(companyId, orderId, new SalesOrderAmendment(new DateOnly(2026, 5, 6), 1, "Too far", [new SalesOrderLineChange(lineId, 5m, 5.5m)]));
        below.Error.Code.Should().Be("Sop.AmendQuantity");
        db.ChangeTracker.Clear();

        IReadOnlyList<SalesOrderVersion> history = (await amendments.HistoryAsync(companyId, orderId)).Value;
        history.Select(v => (v.Version, v.TotalNet, v.Reason)).Should().Equal((1, 100m, "Customer cut the order"), (2, 66m, (string?)null));
        history[0].Lines.Should().ContainSingle().Which.Should().Be(new SalesOrderLineChange(lineId, 20m, 5m));
        history[1].Lines.Should().ContainSingle().Which.Should().Be(new SalesOrderLineChange(lineId, 12m, 5.5m));
    }
}
