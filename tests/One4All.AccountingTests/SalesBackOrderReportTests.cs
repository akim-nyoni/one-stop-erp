using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.SalesOrders;
using One4All.Core.Inventory;
using One4All.Core.SalesOrders;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class SalesBackOrderReportTests
{
    private readonly AccountingFixture _fixture;

    public SalesBackOrderReportTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Back_orders_report_shows_undespatched_quantity()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long customerId;
        long itemId;
        long warehouseId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Customer customer = new(companyId, "BO-CUST", "BackOrder Buyer", Scenario.Usd);
            db.Customers.Add(customer);
            Item item = new(companyId, "BO-ITEM", "Widget") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
            db.Items.Add(item);
            Warehouse wh = new(companyId, "BO-WH", "Main");
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();
            customerId = customer.Id;
            itemId = item.Id;
            warehouseId = wh.Id;
        }

        long orderId, orderLineId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = itemId, WarehouseId = warehouseId, Date = new DateOnly(2026, 3, 1), Quantity = 100m, UnitCost = 1m, CreditAccountCode = "1100" });

            ISalesOrderService sop = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();
            var order = await sop.CreateOrderAsync(new CreateSalesOrderRequest
            {
                CompanyId = companyId, CustomerId = customerId, OrderNumber = "BO-1", OrderDate = new DateOnly(2026, 3, 2), Currency = Scenario.Usd,
                Lines = [new SalesOrderLineRequest(itemId, "Widget", 10m, 5m, null, "4000")],
            });
            order.IsSuccess.Should().BeTrue(order.IsFailure ? order.Error.ToString() : null);
            orderId = order.Value.SalesOrderId;

            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            orderLineId = db.Set<SalesOrderLine>().First(l => l.SalesOrderId == orderId).Id;

            // Despatch only 4 of 10 → 6 outstanding.
            var despatch = await sop.DespatchAsync(new DespatchRequest
            {
                CompanyId = companyId, SalesOrderId = orderId, WarehouseId = warehouseId, Date = new DateOnly(2026, 3, 5), Reference = "BO-DN1",
                Lines = [new DespatchLine(orderLineId, 4m)],
            });
            despatch.IsSuccess.Should().BeTrue(despatch.IsFailure ? despatch.Error.ToString() : null);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReportDataProvider registers = scope.ServiceProvider.GetServices<IReportDataProvider>().First(p => p.CanHandle("SalesBackOrders"));
            var rows = await registers.GetRowsAsync(companyId, "SalesBackOrders", new Dictionary<string, object?>());
            rows.Should().ContainSingle(r => (string)r["OrderNumber"]! == "BO-1");
            rows.Single()["Outstanding"].Should().Be(6m);
            rows.Single()["Despatched"].Should().Be(4m);

            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            string html = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "SOP-BO", new Dictionary<string, object?>(), ReportFormat.Html)).Value.Content);
            html.Should().Contain("BO-1").And.Contain("BackOrder Buyer");
        }
    }
}
