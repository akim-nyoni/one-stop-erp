using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Restaurant;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class RestaurantCategorySalesTests
{
    private const long Waiter = 21901;

    private readonly AccountingFixture _fixture;

    public RestaurantCategorySalesTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Category_sales_report_totals_billed_sales_by_menu_category()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long warehouseId;
        long steakId;
        long wineId;
        long orderId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Core.Inventory.Warehouse wh = new(companyId, "CTKIT", "Kitchen");
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();
            warehouseId = wh.Id;

            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            steakId = (await res.CreateMenuItemAsync(companyId, "CT-STEAK", "Steak", 90m, "4000", "5000", [])).Value;
            wineId = (await res.CreateMenuItemAsync(companyId, "CT-WINE", "Wine", 40m, "4000", "5000", [])).Value;

            // Categorise the menu items.
            Core.Restaurant.MenuItem steak = await db.Set<Core.Restaurant.MenuItem>().FirstAsync(m => m.Id == steakId);
            steak.Category = "Mains";
            Core.Restaurant.MenuItem wine = await db.Set<Core.Restaurant.MenuItem>().FirstAsync(m => m.Id == wineId);
            wine.Category = "Drinks";
            await db.SaveChangesAsync();

            long tableId = (await res.CreateTableAsync(companyId, "CT1", 4)).Value;
            orderId = (await res.OpenOrderAsync(companyId, tableId, "CT-ORD-1", Waiter, new DateOnly(2026, 4, 7))).Value;
            await res.AddItemAsync(companyId, orderId, steakId, 2m); // Mains 180
            await res.AddItemAsync(companyId, orderId, wineId, 1m);  // Drinks 40
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            (await res.BillOrderAsync(companyId, orderId, warehouseId, new DateOnly(2026, 4, 7), "1000", tillSessionId: await RestaurantTill.SessionAsync(scope.ServiceProvider, companyId))).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            Dictionary<string, object?> ps = new() { ["FromDate"] = new DateOnly(2026, 4, 1), ["ToDate"] = new DateOnly(2026, 4, 30) };
            string cat = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "RES-CAT", ps, ReportFormat.Html)).Value.Content);
            cat.Should().Contain("Mains").And.Contain("Drinks");
            cat.Should().Contain("180.00").And.Contain("40.00");

            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 4, 30));
            tb.IsBalanced.Should().BeTrue();
        }
    }
}
