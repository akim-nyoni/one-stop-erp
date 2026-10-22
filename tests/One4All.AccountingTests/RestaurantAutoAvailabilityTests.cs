using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Restaurant;
using One4All.Core.Inventory;
using One4All.Core.Restaurant;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class RestaurantAutoAvailabilityTests
{
    private const long Waiter = 22101;

    private readonly AccountingFixture _fixture;

    public RestaurantAutoAvailabilityTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Menu_items_are_auto_86ed_when_a_recipe_ingredient_runs_out_and_restored_when_restocked()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long warehouseId;
        long pattyId;
        long burgerId;
        long juiceId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Item patty = new(companyId, "AA-PATTY", "Patty") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
            db.Items.Add(patty);
            Warehouse wh = new(companyId, "AAKIT", "Kitchen");
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();
            pattyId = patty.Id;
            warehouseId = wh.Id;

            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = pattyId, WarehouseId = warehouseId, Date = new DateOnly(2026, 4, 10), Quantity = 2m, UnitCost = 8.00m, CreditAccountCode = "1100" });

            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            burgerId = (await res.CreateMenuItemAsync(companyId, "AA-BURGER", "Burger", 50m, "4000", "5000", [new RecipeIngredient(pattyId, 1m)])).Value;
            juiceId = (await res.CreateMenuItemAsync(companyId, "AA-JUICE", "Juice", 10m, "4000", "5000", [])).Value; // recipe-less
        }

        // With 2 patties in stock, the burger is available; refresh reports no change.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            (await res.RefreshMenuAvailabilityAsync(companyId, warehouseId)).Value.Should().Be(0);

            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            (await db.Set<MenuItem>().FirstAsync(m => m.Id == burgerId)).IsAvailable.Should().BeTrue();
        }

        // Consume all the patties via ordered burgers, then refresh → burger 86'd.
        long tableId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            tableId = (await res.CreateTableAsync(companyId, "AA1", 4)).Value;
            long orderId = (await res.OpenOrderAsync(companyId, tableId, "AA-ORD-1", Waiter, new DateOnly(2026, 4, 10))).Value;
            await res.AddItemAsync(companyId, orderId, burgerId, 2m); // uses both patties on billing
            await res.BillOrderAsync(companyId, orderId, warehouseId, new DateOnly(2026, 4, 10), "1000", tillSessionId: await RestaurantTill.SessionAsync(scope.ServiceProvider, companyId));

            int changed = (await res.RefreshMenuAvailabilityAsync(companyId, warehouseId)).Value;
            changed.Should().Be(1); // the burger flipped to unavailable
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            (await db.Set<MenuItem>().FirstAsync(m => m.Id == burgerId)).IsAvailable.Should().BeFalse();
            (await db.Set<MenuItem>().FirstAsync(m => m.Id == juiceId)).IsAvailable.Should().BeTrue(); // recipe-less, untouched

            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            long orderId = (await res.OpenOrderAsync(companyId, tableId, "AA-ORD-2", Waiter, new DateOnly(2026, 4, 10))).Value;
            (await res.AddItemAsync(companyId, orderId, burgerId, 1m)).IsFailure.Should().BeTrue(); // 86'd → cannot order
        }

        // Restock and refresh → burger comes back.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = pattyId, WarehouseId = warehouseId, Date = new DateOnly(2026, 4, 11), Quantity = 5m, UnitCost = 8.00m, CreditAccountCode = "1100" });

            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            (await res.RefreshMenuAvailabilityAsync(companyId, warehouseId)).Value.Should().Be(1);

            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            (await db.Set<MenuItem>().FirstAsync(m => m.Id == burgerId)).IsAvailable.Should().BeTrue();
        }
    }
}
