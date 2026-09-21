using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Restaurant;
using One4All.Core.Inventory;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class RestaurantWastageTests
{
    private readonly AccountingFixture _fixture;

    public RestaurantWastageTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Wastage_writes_stock_off_to_the_wastage_account_not_cost_of_sales()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long warehouseId;
        long lettuceId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Item lettuce = new(companyId, "WS-LETTUCE", "Lettuce") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
            db.Items.Add(lettuce);
            Warehouse wh = new(companyId, "WSKIT", "Kitchen");
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();
            lettuceId = lettuce.Id;
            warehouseId = wh.Id;

            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = lettuceId, WarehouseId = warehouseId, Date = new DateOnly(2026, 4, 8), Quantity = 100m, UnitCost = 2.00m, CreditAccountCode = "1100" });
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();

            // A reason is mandatory and the quantity must be positive.
            (await res.RecordWastageAsync(companyId, lettuceId, warehouseId, new DateOnly(2026, 4, 8), 5m, "5200", "  ")).IsFailure.Should().BeTrue();
            (await res.RecordWastageAsync(companyId, lettuceId, warehouseId, new DateOnly(2026, 4, 8), 0m, "5200", "spoiled")).IsFailure.Should().BeTrue();

            // Write off 10 spoiled lettuces (value 10 × 2.00 = 20).
            var waste = await res.RecordWastageAsync(companyId, lettuceId, warehouseId, new DateOnly(2026, 4, 8), 10m, "5200", "Spoiled overnight");
            waste.IsSuccess.Should().BeTrue(waste.IsFailure ? waste.Error.ToString() : null);
            waste.Value.Should().Be(20m);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            var valuation = await inv.StockValuationAsync(companyId);
            valuation.Should().Contain(l => l.ItemCode == "WS-LETTUCE" && l.Quantity == 90m); // 100 - 10 wasted

            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 4, 30));
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Should().Contain(r => r.AccountCode == "5200" && r.Debit == 20m);  // wastage expense
            tb.Rows.Should().NotContain(r => r.AccountCode == "5000" && (r.Debit != 0m || r.Credit != 0m)); // COS reclassified out to zero

            // The wastage report lists it.
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            Dictionary<string, object?> ps = new() { ["FromDate"] = new DateOnly(2026, 4, 1), ["ToDate"] = new DateOnly(2026, 4, 30) };
            string waste = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "RES-WASTE", ps, ReportFormat.Html)).Value.Content);
            waste.Should().Contain("WS-LETTUCE").And.Contain("Spoiled overnight").And.Contain("20.00");
        }
    }
}
