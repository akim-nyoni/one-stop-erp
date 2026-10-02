using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Procurement;
using One4All.Core.Inventory;
using One4All.Core.Procurement;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers replenishment: suggestions net open purchase orders against on-hand stock, carry the last
/// purchase price and supplier, and a purchase order raised from them prices lines at that last price.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ReplenishmentTests
{
    private readonly AccountingFixture _fixture;

    public ReplenishmentTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Suggestions_net_on_order_stock_and_raise_a_po_at_last_price()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long supplierId;
        long lowId;
        long coveredId;
        long fineId;
        long warehouseId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Supplier supplier = new(companyId, "RP-SUP", "Parts Supplier", Scenario.Usd);
            db.Suppliers.Add(supplier);
            Item low = new(companyId, "RP-LOW", "Low stock bolt") { CostOfSalesAccountCode = "5000", ReorderLevel = 50m, ReorderQuantity = 200m };
            Item covered = new(companyId, "RP-COV", "Covered by open PO") { CostOfSalesAccountCode = "5000", ReorderLevel = 50m, ReorderQuantity = 100m };
            Item fine = new(companyId, "RP-OK", "Plenty on hand") { CostOfSalesAccountCode = "5000", ReorderLevel = 10m };
            db.Items.AddRange(low, covered, fine);
            Warehouse wh = new(companyId, "RPWH", "Main");
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();
            supplierId = supplier.Id;
            lowId = low.Id;
            coveredId = covered.Id;
            fineId = fine.Id;
            warehouseId = wh.Id;

            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            (await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = lowId, WarehouseId = warehouseId, Date = new DateOnly(2026, 3, 1), Quantity = 20m, UnitCost = 1.50m, CreditAccountCode = "1100" })).IsSuccess.Should().BeTrue();
            (await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = coveredId, WarehouseId = warehouseId, Date = new DateOnly(2026, 3, 1), Quantity = 20m, UnitCost = 3.00m, CreditAccountCode = "1100" })).IsSuccess.Should().BeTrue();
            (await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = fineId, WarehouseId = warehouseId, Date = new DateOnly(2026, 3, 1), Quantity = 500m, UnitCost = 0.10m, CreditAccountCode = "1100" })).IsSuccess.Should().BeTrue();

            IProcurementService pop = scope.ServiceProvider.GetRequiredService<IProcurementService>();

            // An older PO for the low item at 1.20, then a newer one at 1.40 (fully received, so it adds nothing to on-order).
            (await pop.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest
            {
                CompanyId = companyId, SupplierId = supplierId, OrderNumber = "RP-PO-OLD", OrderDate = new DateOnly(2026, 2, 1), Currency = Scenario.Usd,
                Lines = [new PurchaseOrderLineRequest(lowId, "Low stock bolt", 10m, 1.20m)],
            })).IsSuccess.Should().BeTrue();
            var newer = await pop.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest
            {
                CompanyId = companyId, SupplierId = supplierId, OrderNumber = "RP-PO-NEW", OrderDate = new DateOnly(2026, 3, 5), Currency = Scenario.Usd,
                Lines = [new PurchaseOrderLineRequest(lowId, "Low stock bolt", 10m, 1.40m)],
            });
            newer.IsSuccess.Should().BeTrue();
            long newerLineId = await db.Set<PurchaseOrderLine>().Where(l => l.PurchaseOrderId == newer.Value.PurchaseOrderId).Select(l => l.Id).SingleAsync();
            (await pop.ReceiveGoodsAsync(new ReceiveGoodsRequest
            {
                CompanyId = companyId, PurchaseOrderId = newer.Value.PurchaseOrderId, WarehouseId = warehouseId, GrnNumber = "RP-GRN1", Date = new DateOnly(2026, 3, 6),
                Lines = [new ReceiveLine(newerLineId, 10m)],
            })).IsSuccess.Should().BeTrue();

            // An open PO for the covered item: 40 ordered, 0 received → 20 on hand + 40 on order = 60 > 50, so no suggestion.
            (await pop.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest
            {
                CompanyId = companyId, SupplierId = supplierId, OrderNumber = "RP-PO-COV", OrderDate = new DateOnly(2026, 3, 7), Currency = Scenario.Usd,
                Lines = [new PurchaseOrderLineRequest(coveredId, "Covered", 40m, 2.50m)],
            })).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReplenishmentService replenish = scope.ServiceProvider.GetRequiredService<IReplenishmentService>();
            IReadOnlyList<ReplenishmentSuggestion> suggestions = await replenish.SuggestAsync(companyId);

            suggestions.Should().ContainSingle(s => s.ItemId == lowId);
            suggestions.Should().NotContain(s => s.ItemId == coveredId); // on-order covers it
            suggestions.Should().NotContain(s => s.ItemId == fineId);

            ReplenishmentSuggestion low = suggestions.Single(s => s.ItemId == lowId);
            low.OnHand.Should().Be(30m); // 20 received directly + 10 via the GRN
            low.OnOrder.Should().Be(10m); // the older PO is still open and un-received; the newer one is fully received
            low.Available.Should().Be(40m); // still at or below the reorder level of 50
            low.SuggestedQuantity.Should().Be(200m); // the item's reorder quantity
            low.LastUnitPrice.Should().Be(1.40m); // the newer PO, not the older 1.20
            low.LastSupplierId.Should().Be(supplierId);

            // Validation.
            (await replenish.RaiseOrderAsync(companyId, supplierId, "RP-PO-X", new DateOnly(2026, 3, 10), [])).IsFailure.Should().BeTrue();
            (await replenish.RaiseOrderAsync(companyId, supplierId, "RP-PO-X", new DateOnly(2026, 3, 10), [new ReplenishmentLine(lowId, 0m)])).IsFailure.Should().BeTrue();
            (await replenish.RaiseOrderAsync(companyId, supplierId, "RP-PO-X", new DateOnly(2026, 3, 10), [new ReplenishmentLine(999_999, 5m)])).Error.Code.Should().Be("Inv.ItemNotFound");

            var raised = await replenish.RaiseOrderAsync(companyId, supplierId, "RP-PO-REP", new DateOnly(2026, 3, 10),
                [new ReplenishmentLine(lowId, low.SuggestedQuantity), new ReplenishmentLine(fineId, 5m, 0.12m)]);
            raised.IsSuccess.Should().BeTrue(raised.IsFailure ? raised.Error.ToString() : null);

            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            PurchaseOrder po = await db.Set<PurchaseOrder>().Include(p => p.Lines).SingleAsync(p => p.Id == raised.Value);
            po.SupplierId.Should().Be(supplierId);
            po.Status.Should().Be(PurchaseOrderStatus.Open);
            po.Lines.Should().HaveCount(2);
            po.Lines.Single(l => l.ItemId == lowId).UnitPrice.Should().Be(1.40m); // last purchase price
            po.Lines.Single(l => l.ItemId == lowId).OrderedQuantity.Should().Be(200m);
            po.Lines.Single(l => l.ItemId == fineId).UnitPrice.Should().Be(0.12m); // explicit price wins

            // The raised order is now on-order, so the item drops out of the suggestions.
            (await replenish.SuggestAsync(companyId)).Should().NotContain(s => s.ItemId == lowId);
        }
    }
}
