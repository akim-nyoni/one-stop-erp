using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Restaurant;
using One4All.Core.Inventory;
using One4All.Core.Restaurant;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class RestaurantTests
{
    private const long Waiter = 20001;

    private readonly AccountingFixture _fixture;

    public RestaurantTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Menu_item_bill_recognises_revenue_and_recipe_cost_of_sales()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long bunId;
        long pattyId;
        long warehouseId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            // Ingredients are stock-tracked items with a cost-of-sales account.
            Item bun = new(companyId, "BUN", "Bun") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
            Item patty = new(companyId, "PATTY", "Beef patty") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
            db.Items.AddRange(bun, patty);
            Warehouse wh = new(companyId, "KITCHEN", "Kitchen");
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();
            bunId = bun.Id;
            pattyId = patty.Id;
            warehouseId = wh.Id;

            // Receive stock: buns @ 2.00, patties @ 8.00.
            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = bunId, WarehouseId = warehouseId, Date = new DateOnly(2026, 3, 1), Quantity = 100m, UnitCost = 2.00m, CreditAccountCode = "1100" });
            await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = pattyId, WarehouseId = warehouseId, Date = new DateOnly(2026, 3, 1), Quantity = 100m, UnitCost = 8.00m, CreditAccountCode = "1100" });
        }

        long menuId;
        long tableId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            // A "Cheeseburger" priced 50, recipe = 1 bun + 1 patty.
            menuId = (await res.CreateMenuItemAsync(companyId, "BURGER", "Cheeseburger", 50m, "4000", "5000",
                [new RecipeIngredient(bunId, 1m), new RecipeIngredient(pattyId, 1m)])).Value;
            tableId = (await res.CreateTableAsync(companyId, "T1", 4)).Value;
        }

        long orderId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            orderId = (await res.OpenOrderAsync(companyId, tableId, "ORD-1", Waiter, new DateOnly(2026, 3, 2))).Value;

            // A second order on the occupied table is refused.
            (await res.OpenOrderAsync(companyId, tableId, "ORD-X", Waiter, new DateOnly(2026, 3, 2))).IsFailure.Should().BeTrue();

            // Order 2 burgers (net 100 + VAT 15).
            (await res.AddItemAsync(companyId, orderId, menuId, 2m, taxAmount: 15m)).Value.Should().Be(100m);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            var bill = await res.BillOrderAsync(companyId, orderId, warehouseId, new DateOnly(2026, 3, 2), "1000", "2200", tillSessionId: await RestaurantTill.SessionAsync(scope.ServiceProvider, companyId));
            bill.IsSuccess.Should().BeTrue(bill.IsFailure ? bill.Error.ToString() : null);
            bill.Value.Net.Should().Be(100m);
            bill.Value.Tax.Should().Be(15m);
            bill.Value.Gross.Should().Be(115m);
            bill.Value.CostOfSales.Should().Be(20m); // 2 burgers × (bun 2 + patty 8)
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            RestaurantTable table = await db.Set<RestaurantTable>().FindAsync(tableId) ?? throw new Xunit.Sdk.XunitException("table gone");
            table.Status.Should().Be(TableStatus.Free); // freed after billing

            // Stock consumed: 2 buns and 2 patties issued.
            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            var valuation = await inv.StockValuationAsync(companyId);
            valuation.Should().Contain(l => l.ItemCode == "BUN" && l.Quantity == 98m);
            valuation.Should().Contain(l => l.ItemCode == "PATTY" && l.Quantity == 98m);

            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 3, 31));
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Should().Contain(r => r.AccountCode == "4000" && r.Credit == 100m);   // revenue
            tb.Rows.Should().Contain(r => r.AccountCode == "2200" && r.Credit == 15m);    // VAT
            tb.Rows.Should().Contain(r => r.AccountCode == "5000" && r.Debit == 20m);     // recipe COGS

            // Restaurant reports render live through the generic runner.
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            string menu = System.Text.Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "RES-MENU", new Dictionary<string, object?>(), ReportFormat.Html)).Value.Content);
            menu.Should().Contain("BURGER").And.Contain("50.00");
            string tables = System.Text.Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "RES-TBL", new Dictionary<string, object?>(), ReportFormat.Html)).Value.Content);
            tables.Should().Contain("T1").And.Contain("Free");
        }
    }
}
