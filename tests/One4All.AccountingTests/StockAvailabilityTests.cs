using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Procurement;
using One4All.Application.Abstractions.SalesOrders;
using One4All.Core.Inventory;
using One4All.Core.SalesOrders;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers stock availability: free stock nets open sales-order commitments, available-to-promise adds
/// inbound purchase orders, and the back-order book allocates on-hand oldest-first so a later order shows
/// its shortfall.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class StockAvailabilityTests
{
    private readonly AccountingFixture _fixture;

    public StockAvailabilityTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Availability_nets_order_commitments_and_back_orders_allocate_oldest_first()
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
            Customer customer = new(companyId, "ATP-C", "Buyer", Scenario.Usd);
            db.Customers.Add(customer);
            Supplier supplier = new(companyId, "ATP-S", "Supplier", Scenario.Usd);
            db.Suppliers.Add(supplier);
            Item item = new(companyId, "ATP-GADGET", "Gadget") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
            db.Items.Add(item);
            Warehouse wh = new(companyId, "ATPWH", "Main");
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();
            customerId = customer.Id;
            supplierId = supplier.Id;
            itemId = item.Id;
            warehouseId = wh.Id;

            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            (await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = itemId, WarehouseId = warehouseId, Date = new DateOnly(2026, 3, 1), Quantity = 50m, UnitCost = 2.00m, CreditAccountCode = "1100" })).IsSuccess.Should().BeTrue();
        }

        long firstOrderId;
        long secondOrderId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ISalesOrderService sop = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();

            // Oldest order: 30, of which 10 is despatched → 20 outstanding. Newer order: 40 outstanding.
            var first = await sop.CreateOrderAsync(new CreateSalesOrderRequest
            {
                CompanyId = companyId, CustomerId = customerId, OrderNumber = "ATP-SO1", OrderDate = new DateOnly(2026, 3, 2), Currency = Scenario.Usd,
                Lines = [new SalesOrderLineRequest(itemId, "Gadget", 30m, 5.00m, null, "4000")],
            });
            first.IsSuccess.Should().BeTrue(first.IsFailure ? first.Error.ToString() : null);
            firstOrderId = first.Value.SalesOrderId;

            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            long firstLineId = await db.Set<SalesOrderLine>().Where(l => l.SalesOrderId == firstOrderId).Select(l => l.Id).SingleAsync();
            (await sop.DespatchAsync(new DespatchRequest
            {
                CompanyId = companyId, SalesOrderId = firstOrderId, WarehouseId = warehouseId, Date = new DateOnly(2026, 3, 3), Reference = "ATP-DN1",
                Lines = [new DespatchLine(firstLineId, 10m)],
            })).IsSuccess.Should().BeTrue();

            var second = await sop.CreateOrderAsync(new CreateSalesOrderRequest
            {
                CompanyId = companyId, CustomerId = customerId, OrderNumber = "ATP-SO2", OrderDate = new DateOnly(2026, 3, 4), Currency = Scenario.Usd,
                Lines = [new SalesOrderLineRequest(itemId, "Gadget", 40m, 5.00m, null, "4000")],
            });
            second.IsSuccess.Should().BeTrue();
            secondOrderId = second.Value.SalesOrderId;

            // Inbound: an open PO for 25.
            IProcurementService pop = scope.ServiceProvider.GetRequiredService<IProcurementService>();
            (await pop.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest
            {
                CompanyId = companyId, SupplierId = supplierId, OrderNumber = "ATP-PO1", OrderDate = new DateOnly(2026, 3, 5), Currency = Scenario.Usd,
                Lines = [new PurchaseOrderLineRequest(itemId, "Gadget", 25m, 1.90m)],
            })).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IStockAvailabilityService availability = scope.ServiceProvider.GetRequiredService<IStockAvailabilityService>();

            // On hand 50 − 10 despatched = 40; allocated 20 + 40 = 60; free −20; ATP −20 + 25 = 5.
            var atp = await availability.AvailabilityAsync(companyId, itemId);
            atp.IsSuccess.Should().BeTrue(atp.IsFailure ? atp.Error.ToString() : null);
            atp.Value.Should().BeEquivalentTo(new { OnHand = 40m, AllocatedToOrders = 60m, OnOrder = 25m, Free = -20m, AvailableToPromise = 5m });
            (await availability.AvailabilityAsync(companyId, 999_999)).Error.Code.Should().Be("Inv.ItemNotFound");

            // Back-orders, oldest first: SO1's 20 is covered by the 40 on hand; SO2's 40 only gets the remaining 20.
            IReadOnlyList<BackOrderLine> book = await availability.BackOrdersAsync(companyId);
            book.Where(b => b.ItemId == itemId).Select(b => b.OrderNumber).Should().Equal("ATP-SO1", "ATP-SO2");

            BackOrderLine so1 = book.Single(b => b.SalesOrderId == firstOrderId);
            so1.Outstanding.Should().Be(20m);
            so1.OnHand.Should().Be(40m);
            so1.CanFulfil.Should().BeTrue();
            so1.Shortfall.Should().Be(0m);

            BackOrderLine so2 = book.Single(b => b.SalesOrderId == secondOrderId);
            so2.Outstanding.Should().Be(40m);
            so2.CanFulfil.Should().BeFalse();
            so2.Shortfall.Should().Be(20m);
        }
    }
}
