using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.SalesOrders;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Inventory;
using One4All.Core.SalesOrders;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class SalesOrderTests
{
    private readonly AccountingFixture _fixture;

    public SalesOrderTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Order_despatch_invoice_flow()
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
            Customer customer = new(companyId, "SOP-C", "Distribution Buyer", Scenario.Usd);
            db.Customers.Add(customer);
            Item item = new(companyId, "GADGET", "Gadget") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
            db.Items.Add(item);
            Warehouse wh = new(companyId, "WH1", "Main");
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();
            customerId = customer.Id;
            itemId = item.Id;
            warehouseId = wh.Id;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = itemId, WarehouseId = warehouseId, Date = new DateOnly(2026, 3, 1), Quantity = 100m, UnitCost = 1.00m, CreditAccountCode = "1100" });
        }

        long orderId;
        long orderLineId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ISalesOrderService sop = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();
            var order = await sop.CreateOrderAsync(new CreateSalesOrderRequest
            {
                CompanyId = companyId, CustomerId = customerId, OrderNumber = "SO-1", OrderDate = new DateOnly(2026, 3, 2), Currency = Scenario.Usd,
                Lines = [new SalesOrderLineRequest(itemId, "Gadget", 10m, 5.00m, null, "4000")],
            });
            order.IsSuccess.Should().BeTrue(order.IsFailure ? order.Error.ToString() : null);
            orderId = order.Value.SalesOrderId;

            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            orderLineId = db.Set<SalesOrderLine>().First(l => l.SalesOrderId == orderId).Id;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ISalesOrderService sop = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();
            var despatch = await sop.DespatchAsync(new DespatchRequest
            {
                CompanyId = companyId, SalesOrderId = orderId, WarehouseId = warehouseId, Date = new DateOnly(2026, 3, 5), Reference = "DN-1",
                Lines = [new DespatchLine(orderLineId, 10m)],
            });
            despatch.IsSuccess.Should().BeTrue(despatch.IsFailure ? despatch.Error.ToString() : null);
            despatch.Value.CostOfSales.Should().Be(10m); // 10 units × 1.00 average cost

            var invoice = await sop.InvoiceAsync(new InvoiceOrderRequest
            {
                CompanyId = companyId, SalesOrderId = orderId, InvoiceNumber = "SINV-SO1", Date = new DateOnly(2026, 3, 6), TaxAccountCode = "2200",
                Lines = [new InvoiceOrderLine(orderLineId, 10m, 7.50m)],
            });
            invoice.IsSuccess.Should().BeTrue(invoice.IsFailure ? invoice.Error.ToString() : null);
            invoice.Value.InvoiceValue.Should().Be(57.50m); // net 50 + VAT 7.50
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ITradingReports reports = scope.ServiceProvider.GetRequiredService<ITradingReports>();
            AgeingReport debtors = await reports.AgedDebtorsAsync(companyId, new DateOnly(2026, 3, 31));
            debtors.Total.Should().Be(57.50m);

            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            var valuation = await inv.StockValuationAsync(companyId);
            valuation.Should().Contain(l => l.ItemCode == "GADGET" && l.Quantity == 90m);
        }
    }
}
