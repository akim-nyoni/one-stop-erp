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
public sealed class SalesOrderDespatchReturnTests
{
    private readonly AccountingFixture _fixture;

    public SalesOrderDespatchReturnTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Returning_despatched_goods_before_invoicing_restores_stock_and_reverses_cost_of_sales()
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
            Customer customer = new(companyId, "DR-C", "Buyer", Scenario.Usd);
            db.Customers.Add(customer);
            Item item = new(companyId, "DR-GADGET", "Gadget") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
            db.Items.Add(item);
            Warehouse wh = new(companyId, "DRWH", "Main");
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();
            customerId = customer.Id;
            itemId = item.Id;
            warehouseId = wh.Id;

            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = itemId, WarehouseId = warehouseId, Date = new DateOnly(2026, 4, 29), Quantity = 100m, UnitCost = 2.00m, CreditAccountCode = "1100" });
        }

        long orderId;
        long orderLineId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ISalesOrderService sop = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();
            orderId = (await sop.CreateOrderAsync(new CreateSalesOrderRequest
            {
                CompanyId = companyId, CustomerId = customerId, OrderNumber = "DR-SO1", OrderDate = new DateOnly(2026, 4, 29), Currency = Scenario.Usd,
                Lines = [new SalesOrderLineRequest(itemId, "Gadget", 20m, 5.00m, null, "4000")],
            })).Value.SalesOrderId;

            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            orderLineId = db.Set<SalesOrderLine>().First(l => l.SalesOrderId == orderId).Id;

            await sop.DespatchAsync(new DespatchRequest
            {
                CompanyId = companyId, SalesOrderId = orderId, WarehouseId = warehouseId, Date = new DateOnly(2026, 4, 29), Reference = "DR-DN1",
                Lines = [new DespatchLine(orderLineId, 20m)],
            });
        }

        // Despatched 20 → stock 80, COGS 40.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            (await inv.StockValuationAsync(companyId)).Should().Contain(l => l.ItemCode == "DR-GADGET" && l.Quantity == 80m);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ISalesOrderService sop = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();

            (await sop.ReturnDespatchAsync(companyId, orderId, orderLineId, warehouseId, new DateOnly(2026, 4, 30), 5m, "  ")).IsFailure.Should().BeTrue(); // reason required
            (await sop.ReturnDespatchAsync(companyId, orderId, orderLineId, warehouseId, new DateOnly(2026, 4, 30), 999m, "too many")).IsFailure.Should().BeTrue();

            // Customer sent 8 back before invoicing.
            var ret = await sop.ReturnDespatchAsync(companyId, orderId, orderLineId, warehouseId, new DateOnly(2026, 4, 30), 8m, "Damaged in transit");
            ret.IsSuccess.Should().BeTrue(ret.IsFailure ? ret.Error.ToString() : null);
            ret.Value.Should().Be(16m); // 8 × 2.00
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            (await db.Set<SalesOrderLine>().FirstAsync(l => l.Id == orderLineId)).DespatchedQuantity.Should().Be(12m); // 20 − 8

            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            (await inv.StockValuationAsync(companyId)).Should().Contain(l => l.ItemCode == "DR-GADGET" && l.Quantity == 88m); // 80 + 8 back

            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 4, 30));
            tb.IsBalanced.Should().BeTrue();
            // COGS: 40 despatched (Dr) less 16 returned (Cr) = net 24 Dr.
            tb.Rows.Should().Contain(r => r.AccountCode == "5000" && r.Debit == 24m);
        }
    }
}
