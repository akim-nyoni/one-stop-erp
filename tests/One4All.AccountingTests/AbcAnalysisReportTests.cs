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
public sealed class AbcAnalysisReportTests
{
    private readonly AccountingFixture _fixture;

    public AbcAnalysisReportTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Abc_analysis_classifies_items_by_cumulative_value()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long aId, bId, cId, whId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Item a = new(companyId, "ABC-A", "High value") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
            Item b = new(companyId, "ABC-B", "Mid value") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
            Item c = new(companyId, "ABC-C", "Low value") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
            db.Items.AddRange(a, b, c);
            Warehouse wh = new(companyId, "WH-ABC", "Main");
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();
            aId = a.Id; bId = b.Id; cId = c.Id; whId = wh.Id;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            // Values: A 800, B 150, C 50 → total 1000 (cumulative 80% / 95% / 100%).
            await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = aId, WarehouseId = whId, Date = new DateOnly(2026, 3, 1), Quantity = 800m, UnitCost = 1m, CreditAccountCode = "1100" });
            await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = bId, WarehouseId = whId, Date = new DateOnly(2026, 3, 1), Quantity = 150m, UnitCost = 1m, CreditAccountCode = "1100" });
            await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = cId, WarehouseId = whId, Date = new DateOnly(2026, 3, 1), Quantity = 50m, UnitCost = 1m, CreditAccountCode = "1100" });
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            string html = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "INV-ABC", new Dictionary<string, object?>(), ReportFormat.Html)).Value.Content);
            // Highest-value item ranks first and cumulative reaches 100 at the tail.
            html.Should().Contain("ABC-A").And.Contain("ABC-B").And.Contain("ABC-C");
            html.Should().Contain("80.00").And.Contain("100.00");

            // Verify the classification through the provider rows directly.
            IReportDataProvider vertical = scope.ServiceProvider.GetServices<IReportDataProvider>().First(p => p.CanHandle("AbcAnalysis"));
            var rows = await vertical.GetRowsAsync(companyId, "AbcAnalysis", new Dictionary<string, object?>());
            rows.Should().HaveCount(3);
            rows[0]["ItemCode"].Should().Be("ABC-A");
            rows[0]["Class"].Should().Be("A");
            rows[1]["Class"].Should().Be("B");
            rows[2]["Class"].Should().Be("C");
        }
    }
}
