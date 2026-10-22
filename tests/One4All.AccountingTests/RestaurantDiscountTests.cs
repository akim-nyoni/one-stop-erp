using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Restaurant;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class RestaurantDiscountTests
{
    private const long Waiter = 21001;

    private readonly AccountingFixture _fixture;

    public RestaurantDiscountTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Bill_discount_posts_to_discounts_allowed_and_reduces_cash()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long warehouseId;
        long menuId;
        long orderId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Core.Inventory.Warehouse wh = new(companyId, "DSKIT", "Kitchen");
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();
            warehouseId = wh.Id;

            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            menuId = (await res.CreateMenuItemAsync(companyId, "DS-MEAL", "Meal", 100m, "4000", "5000", [])).Value;
            long tableId = (await res.CreateTableAsync(companyId, "DS1", 4)).Value;
            orderId = (await res.OpenOrderAsync(companyId, tableId, "DS-ORD-1", Waiter, new DateOnly(2026, 3, 25))).Value;
            await res.AddItemAsync(companyId, orderId, menuId, 1m); // net 100
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();

            // A discount bigger than the bill is refused.
            (await res.BillOrderAsync(companyId, orderId, warehouseId, new DateOnly(2026, 3, 25), "1000", discountAmount: 500m, discountAccountCode: "4700", tillSessionId: await RestaurantTill.SessionAsync(scope.ServiceProvider, companyId))).IsFailure.Should().BeTrue();

            // 20 off a 100 meal.
            var bill = await res.BillOrderAsync(companyId, orderId, warehouseId, new DateOnly(2026, 3, 25), "1000", discountAmount: 20m, discountAccountCode: "4700", tillSessionId: await RestaurantTill.SessionAsync(scope.ServiceProvider, companyId));
            bill.IsSuccess.Should().BeTrue(bill.IsFailure ? bill.Error.ToString() : null);
            bill.Value.Net.Should().Be(100m);
            bill.Value.Discount.Should().Be(20m);
            bill.Value.Gross.Should().Be(80m); // guest pays 80
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 3, 31));
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Should().Contain(r => r.AccountCode == "4000" && r.Credit == 100m); // revenue at full menu price
            tb.Rows.Should().Contain(r => r.AccountCode == "4700" && r.Debit == 20m);   // discount allowed (contra-revenue)
            tb.Rows.Should().Contain(r => r.AccountCode == "1000" && r.Debit == 80m);   // cash actually collected
        }
    }
}
