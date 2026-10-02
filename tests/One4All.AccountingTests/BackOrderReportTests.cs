using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.SalesOrders;
using One4All.Core.Inventory;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers the back-order book rendered as a catalogue report (INV-BACKORDER).</summary>
[Collection(AccountingCollection.Name)]
public sealed class BackOrderReportTests
{
    private readonly AccountingFixture _fixture;

    public BackOrderReportTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Back_order_book_renders_through_the_report_runner()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long customerId;
        long itemId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Customer customer = new(companyId, "BOR-C", "Waiting Buyer", Scenario.Usd);
            db.Customers.Add(customer);
            Item item = new(companyId, "BOR-WIDGET", "Widget") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
            db.Items.Add(item);
            Warehouse wh = new(companyId, "BORWH", "Main");
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();
            customerId = customer.Id;
            itemId = item.Id;

            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            (await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = itemId, WarehouseId = wh.Id, Date = new DateOnly(2026, 3, 1), Quantity = 5m, UnitCost = 1.00m, CreditAccountCode = "1100" })).IsSuccess.Should().BeTrue();

            ISalesOrderService sop = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();
            (await sop.CreateOrderAsync(new CreateSalesOrderRequest
            {
                CompanyId = companyId, CustomerId = customerId, OrderNumber = "BOR-SO1", OrderDate = new DateOnly(2026, 3, 2), Currency = Scenario.Usd,
                Lines = [new SalesOrderLineRequest(itemId, "Widget", 12m, 4.00m, null, "4000")],
            })).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            var rendered = await runner.RenderAsync(companyId, "INV-BACKORDER", new Dictionary<string, object?>(), ReportFormat.Html);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? rendered.Error.ToString() : null);
            string html = Encoding.UTF8.GetString(rendered.Value.Content);

            html.Should().Contain("Back-order Book");
            html.Should().Contain("BOR-SO1").And.Contain("BOR-C").And.Contain("BOR-WIDGET");
            html.Should().Contain("12.00").And.Contain("5.00").And.Contain("7.00"); // outstanding, on hand, shortfall
            html.Should().Contain("No"); // cannot fulfil
        }
    }
}
