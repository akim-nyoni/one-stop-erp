using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Inventory;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class InventorySlowStockReportTests
{
    private readonly AccountingFixture _fixture;

    public InventorySlowStockReportTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Slow_stock_report_lists_items_with_no_movement_since_the_cutoff()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long warehouseId;
        long staleId;
        long freshId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Item stale = new(companyId, "SL-OLD", "Dusty widget") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
            Item fresh = new(companyId, "SL-NEW", "Fast mover") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
            db.Items.AddRange(stale, fresh);
            Warehouse wh = new(companyId, "SLWH", "Store");
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();
            staleId = stale.Id;
            freshId = fresh.Id;
            warehouseId = wh.Id;

            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            // Stale item last moved in January.
            await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = staleId, WarehouseId = warehouseId, Date = new DateOnly(2026, 1, 10), Quantity = 40m, UnitCost = 5.00m, CreditAccountCode = "1100" });
            // Fresh item moved in April.
            await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = freshId, WarehouseId = warehouseId, Date = new DateOnly(2026, 4, 20), Quantity = 10m, UnitCost = 1.00m, CreditAccountCode = "1100" });
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            // Anything with no movement since 1 March is "slow".
            Dictionary<string, object?> ps = new() { ["SinceDate"] = new DateOnly(2026, 3, 1) };
            string slow = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "INV-SLOW", ps, ReportFormat.Html)).Value.Content);
            slow.Should().Contain("SL-OLD");        // last moved January → slow
            slow.Should().NotContain("SL-NEW");     // moved April → not slow
            slow.Should().Contain("200.00");        // 40 × 5 value tied up
        }
    }
}
