using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Restaurant;
using One4All.Core.Inventory;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class RestaurantNegativeStockBillingTests
{
    private const long Waiter = 22601;

    private readonly AccountingFixture _fixture;

    public RestaurantNegativeStockBillingTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_served_meal_still_bills_when_stock_is_short_but_wastage_of_missing_stock_is_refused()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long warehouseId;
        long pattyId;
        long burgerId;
        long orderId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Item patty = new(companyId, "NS-PATTY", "Patty") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
            db.Items.Add(patty);
            Warehouse wh = new(companyId, "NSKIT", "Kitchen");
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();
            pattyId = patty.Id;
            warehouseId = wh.Id;

            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = pattyId, WarehouseId = warehouseId, Date = new DateOnly(2026, 4, 28), Quantity = 1m, UnitCost = 8.00m, CreditAccountCode = "1100" });

            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            burgerId = (await res.CreateMenuItemAsync(companyId, "NS-BURGER", "Burger", 50m, "4000", "5000", [new RecipeIngredient(pattyId, 1m)])).Value;
            long tableId = (await res.CreateTableAsync(companyId, "NS1", 4)).Value;
            orderId = (await res.OpenOrderAsync(companyId, tableId, "NS-ORD-1", Waiter, new DateOnly(2026, 4, 28))).Value;
            await res.AddItemAsync(companyId, orderId, burgerId, 3m); // 3 burgers but only 1 patty in stock
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            // The guests were served: the bill still posts, driving patty stock negative.
            var bill = await res.BillOrderAsync(companyId, orderId, warehouseId, new DateOnly(2026, 4, 28), "1000", tillSessionId: await RestaurantTill.SessionAsync(scope.ServiceProvider, companyId));
            bill.IsSuccess.Should().BeTrue(bill.IsFailure ? bill.Error.ToString() : null);
            bill.Value.Net.Should().Be(150m);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            (await inv.StockValuationAsync(companyId)).Should().Contain(l => l.ItemCode == "NS-PATTY" && l.Quantity == -2m); // 1 − 3

            // But recording wastage of stock we do not have is refused (deliberate action).
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            (await res.RecordWastageAsync(companyId, pattyId, warehouseId, new DateOnly(2026, 4, 28), 5m, "5200", "spoiled")).IsFailure.Should().BeTrue();
        }
    }
}
