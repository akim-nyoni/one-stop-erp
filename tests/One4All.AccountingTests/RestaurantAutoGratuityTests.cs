using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Restaurant;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class RestaurantAutoGratuityTests
{
    private const long Waiter = 21201;

    private readonly AccountingFixture _fixture;

    public RestaurantAutoGratuityTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Large_parties_are_quoted_an_auto_gratuity_small_parties_are_not()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long warehouseId;
        long menuId;
        long bigOrderId;
        long smallOrderId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Core.Inventory.Warehouse wh = new(companyId, "AGKIT", "Kitchen");
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();
            warehouseId = wh.Id;

            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            // Policy: parties of 6+ get a 10% gratuity to 4600.
            (await res.SetAutoGratuityPolicyAsync(companyId, 6, 10m, "4600")).IsSuccess.Should().BeTrue();

            menuId = (await res.CreateMenuItemAsync(companyId, "AG-PLATTER", "Platter", 100m, "4000", "5000", [])).Value;
            long bigTable = (await res.CreateTableAsync(companyId, "AG1", 8)).Value;
            long smallTable = (await res.CreateTableAsync(companyId, "AG2", 4)).Value;
            bigOrderId = (await res.OpenOrderAsync(companyId, bigTable, "AG-ORD-1", Waiter, new DateOnly(2026, 3, 29), partySize: 6)).Value;
            smallOrderId = (await res.OpenOrderAsync(companyId, smallTable, "AG-ORD-2", Waiter, new DateOnly(2026, 3, 29), partySize: 2)).Value;
            await res.AddItemAsync(companyId, bigOrderId, menuId, 2m);   // net 200
            await res.AddItemAsync(companyId, smallOrderId, menuId, 1m); // net 100
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();

            // Small party: no auto-gratuity.
            var smallQuote = await res.QuoteAutoGratuityAsync(companyId, smallOrderId);
            smallQuote.IsSuccess.Should().BeTrue();
            smallQuote.Value.Applies.Should().BeFalse();
            smallQuote.Value.Amount.Should().Be(0m);

            // Big party: 10% of 200 = 20 to 4600.
            var bigQuote = await res.QuoteAutoGratuityAsync(companyId, bigOrderId);
            bigQuote.IsSuccess.Should().BeTrue();
            bigQuote.Value.Applies.Should().BeTrue();
            bigQuote.Value.Amount.Should().Be(20m);
            bigQuote.Value.ServiceChargeAccountCode.Should().Be("4600");

            // Apply the quote as the service charge when billing the big party.
            var bill = await res.BillOrderAsync(companyId, bigOrderId, warehouseId, new DateOnly(2026, 3, 29), "1000",
                serviceChargeAmount: bigQuote.Value.Amount, serviceChargeAccountCode: bigQuote.Value.ServiceChargeAccountCode, tillSessionId: await RestaurantTill.SessionAsync(scope.ServiceProvider, companyId));
            bill.IsSuccess.Should().BeTrue(bill.IsFailure ? bill.Error.ToString() : null);
            bill.Value.ServiceCharge.Should().Be(20m);
            bill.Value.Gross.Should().Be(220m); // 200 + 20 gratuity
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 3, 31));
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Should().Contain(r => r.AccountCode == "4000" && r.Credit == 200m); // food revenue
            tb.Rows.Should().Contain(r => r.AccountCode == "4600" && r.Credit == 20m);  // auto-gratuity as service-charge income
        }
    }
}
