using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Restaurant;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class RestaurantPriceLevelTests
{
    private const long Waiter = 21801;

    private readonly AccountingFixture _fixture;

    public RestaurantPriceLevelTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Price_levels_charge_alternate_prices_and_fall_back_to_the_base_price()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long warehouseId;
        long beerId;
        long orderId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Core.Inventory.Warehouse wh = new(companyId, "PLKIT", "Kitchen");
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();
            warehouseId = wh.Id;

            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            beerId = (await res.CreateMenuItemAsync(companyId, "PL-BEER", "Beer", 8m, "4000", "5000", [])).Value; // base 8
            (await res.SetMenuItemPriceLevelAsync(companyId, beerId, "happyhour", 5m)).IsSuccess.Should().BeTrue();
            // Update the same level (idempotent upsert).
            (await res.SetMenuItemPriceLevelAsync(companyId, beerId, "HAPPYHOUR", 4m)).IsSuccess.Should().BeTrue();

            long tableId = (await res.CreateTableAsync(companyId, "PL1", 4)).Value;
            orderId = (await res.OpenOrderAsync(companyId, tableId, "PL-ORD-1", Waiter, new DateOnly(2026, 4, 6))).Value;

            // One at happy-hour price, one at the base price, one at an unknown level (falls back to base).
            (await res.AddItemAsync(companyId, orderId, beerId, 1m, priceLevel: "HAPPYHOUR")).Value.Should().Be(4m);
            (await res.AddItemAsync(companyId, orderId, beerId, 1m)).Value.Should().Be(8m);
            (await res.AddItemAsync(companyId, orderId, beerId, 1m, priceLevel: "NOPE")).Value.Should().Be(8m);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            var bill = await res.BillOrderAsync(companyId, orderId, warehouseId, new DateOnly(2026, 4, 6), "1000", tillSessionId: await RestaurantTill.SessionAsync(scope.ServiceProvider, companyId));
            bill.IsSuccess.Should().BeTrue(bill.IsFailure ? bill.Error.ToString() : null);
            bill.Value.Net.Should().Be(20m); // 4 + 8 + 8
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 4, 30));
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Should().Contain(r => r.AccountCode == "4000" && r.Credit == 20m);
        }
    }
}
