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
public sealed class RestaurantMenuCostTests
{
    private readonly AccountingFixture _fixture;

    public RestaurantMenuCostTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Menu_cost_card_shows_recipe_cost_margin_and_food_cost_percent()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long warehouseId;
        long bunId;
        long pattyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Item bun = new(companyId, "MC-BUN", "Bun") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
            Item patty = new(companyId, "MC-PATTY", "Patty") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
            db.Items.AddRange(bun, patty);
            Warehouse wh = new(companyId, "MCKIT", "Kitchen");
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();
            bunId = bun.Id;
            pattyId = patty.Id;
            warehouseId = wh.Id;

            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = bunId, WarehouseId = warehouseId, Date = new DateOnly(2026, 4, 15), Quantity = 100m, UnitCost = 2.00m, CreditAccountCode = "1100" });
            await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = pattyId, WarehouseId = warehouseId, Date = new DateOnly(2026, 4, 15), Quantity = 100m, UnitCost = 8.00m, CreditAccountCode = "1100" });

            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            // Burger priced 40, recipe bun (2) + patty (8) = cost 10 → margin 30, food cost 25%.
            await res.CreateMenuItemAsync(companyId, "MC-BURGER", "Cheeseburger", 40m, "4000", "5000",
                [new RecipeIngredient(bunId, 1m), new RecipeIngredient(pattyId, 1m)]);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            string card = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "RES-COST", new Dictionary<string, object?>(), ReportFormat.Html)).Value.Content);
            card.Should().Contain("Cheeseburger");
            card.Should().Contain("40.00");  // price
            card.Should().Contain("10.00");  // recipe cost
            card.Should().Contain("30.00");  // margin
            card.Should().Contain("25.00");  // food cost %
        }
    }
}
