using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Core.Inventory;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class InventoryTests
{
    private readonly AccountingFixture _fixture;

    public InventoryTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Weighted_average_costing_across_receipts_and_issue()
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
            Item item = new(companyId, "WIDGET", "Widget") { CostOfSalesAccountCode = "5000" };
            db.Items.Add(item);
            Warehouse wh = new(companyId, "WH1", "Main Warehouse");
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();
            itemId = item.Id;
            warehouseId = wh.Id;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();

            // Receive 100 @ 2.00 then 100 @ 3.00 -> moving average 2.50.
            var r1 = await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = itemId, WarehouseId = warehouseId, Date = new DateOnly(2026, 3, 1), Quantity = 100m, UnitCost = 2.00m, CreditAccountCode = "1100" });
            r1.IsSuccess.Should().BeTrue(r1.IsFailure ? r1.Error.ToString() : null);
            r1.Value.AverageCostAfter.Should().Be(2.00m);

            var r2 = await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = itemId, WarehouseId = warehouseId, Date = new DateOnly(2026, 3, 5), Quantity = 100m, UnitCost = 3.00m, CreditAccountCode = "1100" });
            r2.Value.AverageCostAfter.Should().Be(2.50m);

            // Issue 50 at the average 2.50 -> COGS 125.
            var issue = await inv.IssueAsync(new IssueStockRequest { CompanyId = companyId, ItemId = itemId, WarehouseId = warehouseId, Date = new DateOnly(2026, 3, 10), Quantity = 50m });
            issue.IsSuccess.Should().BeTrue(issue.IsFailure ? issue.Error.ToString() : null);
            issue.Value.Value.Should().Be(125m);
            issue.Value.AverageCostAfter.Should().Be(2.50m);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            IReadOnlyList<StockValuationLine> valuation = await inv.StockValuationAsync(companyId);

            valuation.Should().ContainSingle(l => l.ItemCode == "WIDGET");
            StockValuationLine widget = valuation.First(l => l.ItemCode == "WIDGET");
            widget.Quantity.Should().Be(150m);        // 200 received − 50 issued
            widget.AverageCost.Should().Be(2.50m);
            widget.Value.Should().Be(375m);           // 150 × 2.50
        }
    }
}
