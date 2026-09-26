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
public sealed class StockAgeingReportTests
{
    private readonly AccountingFixture _fixture;

    public StockAgeingReportTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Stock_ageing_buckets_on_hand_value_by_age_since_last_movement()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long itemId, whId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Item item = new(companyId, "AGE-1", "Aged widget") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
            db.Items.Add(item);
            Warehouse wh = new(companyId, "AGE-WH", "Main");
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();
            itemId = item.Id; whId = wh.Id;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            // Received 2026-01-01; last movement is that date.
            await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = itemId, WarehouseId = whId, Date = new DateOnly(2026, 1, 1), Quantity = 10m, UnitCost = 5m, CreditAccountCode = "1100" });
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReportDataProvider vertical = scope.ServiceProvider.GetServices<IReportDataProvider>().First(p => p.CanHandle("StockAgeing"));
            // As at 2026-06-30: ~180 days since last movement → 90+ band.
            var parameters = new Dictionary<string, object?> { ["AsAtDate"] = new DateOnly(2026, 6, 30) };
            var rows = await vertical.GetRowsAsync(companyId, "StockAgeing", parameters);
            var row = rows.Single(r => (string)r["ItemCode"]! == "AGE-1");
            row["AgeBand"].Should().Be("90+");
            row["Value"].Should().Be(50m);

            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            string html = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "INV-AGE", parameters, ReportFormat.Html)).Value.Content);
            html.Should().Contain("AGE-1").And.Contain("90+");
        }
    }
}
