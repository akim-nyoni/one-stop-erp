using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Procurement;
using One4All.Application.Abstractions.SalesOrders;
using One4All.Core.Inventory;
using One4All.Core.Procurement;
using One4All.Core.SalesOrders;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers order cancellation: an untouched sales or purchase order can be cancelled (and drops out of the
/// stock commitment / inbound figures), one with goods moved cannot, and a reason is mandatory.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class OrderCancellationTests
{
    private readonly AccountingFixture _fixture;

    public OrderCancellationTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Untouched_orders_cancel_and_release_commitments_but_moved_goods_block_it()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long customerId;
        long supplierId;
        long itemId;
        long warehouseId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Customer customer = new(companyId, "CXL-C", "Buyer", Scenario.Usd);
            db.Customers.Add(customer);
            Supplier supplier = new(companyId, "CXL-S", "Supplier", Scenario.Usd);
            db.Suppliers.Add(supplier);
            Item item = new(companyId, "CXL-ITEM", "Thing") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
            db.Items.Add(item);
            Warehouse wh = new(companyId, "CXLWH", "Main");
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();
            customerId = customer.Id;
            supplierId = supplier.Id;
            itemId = item.Id;
            warehouseId = wh.Id;

            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            (await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = itemId, WarehouseId = warehouseId, Date = new DateOnly(2026, 3, 1), Quantity = 100m, UnitCost = 1.00m, CreditAccountCode = "1100" })).IsSuccess.Should().BeTrue();
        }

        long cleanSoId;
        long despatchedSoId;
        long cleanPoId;
        long receivedPoId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ISalesOrderService sop = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();
            IProcurementService pop = scope.ServiceProvider.GetRequiredService<IProcurementService>();
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();

            cleanSoId = (await sop.CreateOrderAsync(Order(companyId, customerId, "CXL-SO1", itemId))).Value.SalesOrderId;
            despatchedSoId = (await sop.CreateOrderAsync(Order(companyId, customerId, "CXL-SO2", itemId))).Value.SalesOrderId;
            long despatchedLineId = await db.Set<SalesOrderLine>().Where(l => l.SalesOrderId == despatchedSoId).Select(l => l.Id).SingleAsync();
            (await sop.DespatchAsync(new DespatchRequest { CompanyId = companyId, SalesOrderId = despatchedSoId, WarehouseId = warehouseId, Date = new DateOnly(2026, 3, 3), Reference = "CXL-DN", Lines = [new DespatchLine(despatchedLineId, 2m)] })).IsSuccess.Should().BeTrue();

            cleanPoId = (await pop.CreatePurchaseOrderAsync(Po(companyId, supplierId, "CXL-PO1", itemId))).Value.PurchaseOrderId;
            receivedPoId = (await pop.CreatePurchaseOrderAsync(Po(companyId, supplierId, "CXL-PO2", itemId))).Value.PurchaseOrderId;
            long receivedLineId = await db.Set<PurchaseOrderLine>().Where(l => l.PurchaseOrderId == receivedPoId).Select(l => l.Id).SingleAsync();
            (await pop.ReceiveGoodsAsync(new ReceiveGoodsRequest { CompanyId = companyId, PurchaseOrderId = receivedPoId, WarehouseId = warehouseId, GrnNumber = "CXL-GRN", Date = new DateOnly(2026, 3, 4), Lines = [new ReceiveLine(receivedLineId, 3m)] })).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IStockAvailabilityService availability = scope.ServiceProvider.GetRequiredService<IStockAvailabilityService>();
            // Before: 100 + 3 received − 2 despatched = 101 on hand; allocated 10 + 8 = 18; on order 10 + 7 = 17.
            (await availability.AvailabilityAsync(companyId, itemId)).Value.Should().BeEquivalentTo(new { OnHand = 101m, AllocatedToOrders = 18m, OnOrder = 17m });
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ISalesOrderService sop = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();
            IProcurementService pop = scope.ServiceProvider.GetRequiredService<IProcurementService>();

            (await sop.CancelOrderAsync(companyId, cleanSoId, " ")).Error.Code.Should().Be("Sop.ReasonRequired");
            (await sop.CancelOrderAsync(companyId, 999_999, "x")).Error.Code.Should().Be("Sop.OrderNotFound");
            (await sop.CancelOrderAsync(companyId, despatchedSoId, "changed mind")).Error.Code.Should().Be("Sop.Despatched");
            var so = await sop.CancelOrderAsync(companyId, cleanSoId, "Customer withdrew");
            so.IsSuccess.Should().BeTrue(so.IsFailure ? so.Error.ToString() : null);
            (await sop.CancelOrderAsync(companyId, cleanSoId, "again")).Error.Code.Should().Be("Sop.AlreadyCancelled");

            (await pop.CancelPurchaseOrderAsync(companyId, cleanPoId, "")).Error.Code.Should().Be("Pop.ReasonRequired");
            (await pop.CancelPurchaseOrderAsync(companyId, 999_999, "x")).Error.Code.Should().Be("Pop.OrderNotFound");
            (await pop.CancelPurchaseOrderAsync(companyId, receivedPoId, "oops")).Error.Code.Should().Be("Pop.Received");
            var po = await pop.CancelPurchaseOrderAsync(companyId, cleanPoId, "Supplier cannot deliver");
            po.IsSuccess.Should().BeTrue(po.IsFailure ? po.Error.ToString() : null);
            (await pop.CancelPurchaseOrderAsync(companyId, cleanPoId, "again")).Error.Code.Should().Be("Pop.AlreadyCancelled");
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            (await db.Set<SalesOrder>().SingleAsync(o => o.Id == cleanSoId)).Status.Should().Be(SalesOrderStatus.Cancelled);
            (await db.Set<PurchaseOrder>().SingleAsync(o => o.Id == cleanPoId)).Status.Should().Be(PurchaseOrderStatus.Cancelled);

            // After: the cancelled orders no longer commit or promise stock.
            IStockAvailabilityService availability = scope.ServiceProvider.GetRequiredService<IStockAvailabilityService>();
            (await availability.AvailabilityAsync(companyId, itemId)).Value.Should().BeEquivalentTo(new { OnHand = 101m, AllocatedToOrders = 8m, OnOrder = 7m });
        }
    }

    private static CreateSalesOrderRequest Order(long companyId, long customerId, string number, long itemId) => new()
    {
        CompanyId = companyId, CustomerId = customerId, OrderNumber = number, OrderDate = new DateOnly(2026, 3, 2), Currency = Scenario.Usd,
        Lines = [new SalesOrderLineRequest(itemId, "Thing", 10m, 5m, null, "4000")],
    };

    private static CreatePurchaseOrderRequest Po(long companyId, long supplierId, string number, long itemId) => new()
    {
        CompanyId = companyId, SupplierId = supplierId, OrderNumber = number, OrderDate = new DateOnly(2026, 3, 2), Currency = Scenario.Usd,
        Lines = [new PurchaseOrderLineRequest(itemId, "Thing", 10m, 1m)],
    };
}
