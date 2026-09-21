using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Inventory;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class InventoryAdjustmentTests
{
    private readonly AccountingFixture _fixture;

    public InventoryAdjustmentTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Stocktake_adjustment_posts_shortage_and_surplus_against_the_adjustment_account()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long itemId;
        long warehouseId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Item item = new(companyId, "ADJ-BOLT", "Bolt") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
            db.Items.Add(item);
            Warehouse wh = new(companyId, "ADJWH", "Store");
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();
            itemId = item.Id;
            warehouseId = wh.Id;

            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = itemId, WarehouseId = warehouseId, Date = new DateOnly(2026, 4, 24), Quantity = 100m, UnitCost = 4.00m, CreditAccountCode = "1100" });
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();

            // Count finds 95 (shortage of 5 → loss 20).
            var shortage = await inv.AdjustAsync(companyId, itemId, warehouseId, new DateOnly(2026, 4, 24), 95m, "5300", "STK-1");
            shortage.IsSuccess.Should().BeTrue(shortage.IsFailure ? shortage.Error.ToString() : null);
            shortage.Value.Should().Be(-20m);

            // A nil variance is a no-op.
            (await inv.AdjustAsync(companyId, itemId, warehouseId, new DateOnly(2026, 4, 24), 95m, "5300")).Value.Should().Be(0m);

            // A later count finds 98 (surplus of 3 → gain 12 at avg 4).
            var surplus = await inv.AdjustAsync(companyId, itemId, warehouseId, new DateOnly(2026, 4, 24), 98m, "5300", "STK-2");
            surplus.Value.Should().Be(12m);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            var valuation = await inv.StockValuationAsync(companyId);
            valuation.Should().Contain(l => l.ItemCode == "ADJ-BOLT" && l.Quantity == 98m && l.Value == 392m); // 98 × 4

            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 4, 30));
            tb.IsBalanced.Should().BeTrue();
            // Adjustment account: 20 loss (Dr) less 12 gain (Cr) = net 8 Dr.
            tb.Rows.Should().Contain(r => r.AccountCode == "5300" && r.Debit == 8m);
        }
    }
}
