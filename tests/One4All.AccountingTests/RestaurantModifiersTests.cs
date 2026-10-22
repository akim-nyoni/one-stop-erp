using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Restaurant;
using One4All.Core.Inventory;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class RestaurantModifiersTests
{
    private const long Waiter = 21301;

    private readonly AccountingFixture _fixture;

    public RestaurantModifiersTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Modifiers_change_the_price_and_extra_ingredients_consume_stock()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long warehouseId;
        long cheeseItemId;
        long burgerId;
        long orderId;
        long lineId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Item cheese = new(companyId, "MD-CHEESE", "Cheese slice") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
            db.Items.Add(cheese);
            Warehouse wh = new(companyId, "MDKIT", "Kitchen");
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();
            cheeseItemId = cheese.Id;
            warehouseId = wh.Id;

            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = cheeseItemId, WarehouseId = warehouseId, Date = new DateOnly(2026, 3, 1), Quantity = 100m, UnitCost = 1.50m, CreditAccountCode = "1100" });

            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            burgerId = (await res.CreateMenuItemAsync(companyId, "MD-BURGER", "Burger", 50m, "4000", "5000", [])).Value;
            long tableId = (await res.CreateTableAsync(companyId, "MD1", 4)).Value;
            orderId = (await res.OpenOrderAsync(companyId, tableId, "MD-ORD-1", Waiter, new DateOnly(2026, 3, 30))).Value;
            (await res.AddItemAsync(companyId, orderId, burgerId, 2m)).Value.Should().Be(100m); // 2 × 50
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            lineId = (await db.Set<Core.Restaurant.TableOrder>().Include(o => o.Lines).FirstAsync(o => o.Id == orderId)).Lines.Single().Id;

            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            // "No onions" is free; "extra cheese" is +3 and consumes 1 cheese slice per burger.
            (await res.AddItemModifierAsync(companyId, orderId, lineId, "No onions", 0m)).IsSuccess.Should().BeTrue();
            var withCheese = await res.AddItemModifierAsync(companyId, orderId, lineId, "Extra cheese", 3m, cheeseItemId, 1m);
            withCheese.IsSuccess.Should().BeTrue(withCheese.IsFailure ? withCheese.Error.ToString() : null);
            withCheese.Value.Should().Be(106m); // 2 × (50 + 3)

            // An ingredient modifier with no quantity is refused.
            (await res.AddItemModifierAsync(companyId, orderId, lineId, "Bad", 1m, cheeseItemId, 0m)).IsFailure.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            var bill = await res.BillOrderAsync(companyId, orderId, warehouseId, new DateOnly(2026, 3, 30), "1000", tillSessionId: await RestaurantTill.SessionAsync(scope.ServiceProvider, companyId));
            bill.IsSuccess.Should().BeTrue(bill.IsFailure ? bill.Error.ToString() : null);
            bill.Value.Net.Should().Be(106m);       // burger + extra cheese
            bill.Value.CostOfSales.Should().Be(3m); // 2 cheese slices × 1.50 (burger itself has no recipe)
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            var valuation = await inv.StockValuationAsync(companyId);
            valuation.Should().Contain(l => l.ItemCode == "MD-CHEESE" && l.Quantity == 98m); // 2 slices consumed

            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 3, 31));
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Should().Contain(r => r.AccountCode == "4000" && r.Credit == 106m);
        }
    }
}
