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
public sealed class InventoryReorderReportTests
{
    private readonly AccountingFixture _fixture;

    public InventoryReorderReportTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Reorder_report_lists_items_at_or_below_their_reorder_level()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long warehouseId;
        long lowId;
        long okId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Item low = new(companyId, "RO-LOW", "Low stock item") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000", ReorderLevel = 20m, ReorderQuantity = 100m };
            Item ok = new(companyId, "RO-OK", "Healthy item") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000", ReorderLevel = 20m, ReorderQuantity = 100m };
            Item untracked = new(companyId, "RO-NONE", "No reorder policy") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" }; // ReorderLevel 0
            db.Items.AddRange(low, ok, untracked);
            Warehouse wh = new(companyId, "ROWH", "Store");
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();
            lowId = low.Id;
            okId = ok.Id;
            warehouseId = wh.Id;

            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = lowId, WarehouseId = warehouseId, Date = new DateOnly(2026, 4, 26), Quantity = 15m, UnitCost = 1.00m, CreditAccountCode = "1100" }); // 15 <= 20 → reorder
            await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = okId, WarehouseId = warehouseId, Date = new DateOnly(2026, 4, 26), Quantity = 50m, UnitCost = 1.00m, CreditAccountCode = "1100" }); // 50 > 20 → healthy
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            string reorder = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "INV-REORDER", new Dictionary<string, object?>(), ReportFormat.Html)).Value.Content);
            reorder.Should().Contain("RO-LOW");        // below reorder level
            reorder.Should().NotContain("RO-OK");      // healthy stock excluded
            reorder.Should().NotContain("RO-NONE");    // no reorder policy excluded
            reorder.Should().Contain("100.00");        // suggested order quantity
        }
    }
}
