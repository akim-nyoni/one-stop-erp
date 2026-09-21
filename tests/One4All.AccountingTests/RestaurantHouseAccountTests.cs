using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Restaurant;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Inventory;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class RestaurantHouseAccountTests
{
    private const long Waiter = 21601;

    private readonly AccountingFixture _fixture;

    public RestaurantHouseAccountTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Charging_a_bill_to_a_house_account_ages_on_the_debtor_and_takes_no_cash()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long warehouseId;
        long customerId;
        long steakItemId;
        long menuId;
        long orderId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Customer hotel = new(companyId, "ROOM204", "Room 204 Guest", Scenario.Usd) { PaymentTermsDays = 7 };
            db.Customers.Add(hotel);
            Item steakItem = new(companyId, "HA-STEAK", "Steak meat") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
            db.Items.Add(steakItem);
            Warehouse wh = new(companyId, "HAKIT", "Kitchen");
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();
            customerId = hotel.Id;
            steakItemId = steakItem.Id;
            warehouseId = wh.Id;

            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = steakItemId, WarehouseId = warehouseId, Date = new DateOnly(2026, 4, 4), Quantity = 50m, UnitCost = 12.00m, CreditAccountCode = "1100" });

            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            menuId = (await res.CreateMenuItemAsync(companyId, "HA-STEAKPLATE", "Steak Plate", 100m, "4000", "5000", [new RecipeIngredient(steakItemId, 1m)])).Value;
            long tableId = (await res.CreateTableAsync(companyId, "HA1", 4)).Value;
            orderId = (await res.OpenOrderAsync(companyId, tableId, "HA-ORD-1", Waiter, new DateOnly(2026, 4, 4))).Value;
            await res.AddItemAsync(companyId, orderId, menuId, 1m, taxAmount: 15m); // net 100 + VAT 15
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            var bill = await res.BillToHouseAccountAsync(companyId, orderId, customerId, warehouseId, new DateOnly(2026, 4, 4), "4000", "2200");
            bill.IsSuccess.Should().BeTrue(bill.IsFailure ? bill.Error.ToString() : null);
            bill.Value.Net.Should().Be(100m);
            bill.Value.Tax.Should().Be(15m);
            bill.Value.Gross.Should().Be(115m);
            bill.Value.CostOfSales.Should().Be(12m); // steak still made
            bill.Value.FullyBilled.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 4, 30));
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Should().Contain(r => r.AccountCode == "1200" && r.Debit == 115m); // AR control charged
            tb.Rows.Should().Contain(r => r.AccountCode == "4000" && r.Credit == 100m); // revenue
            tb.Rows.Should().Contain(r => r.AccountCode == "2200" && r.Credit == 15m);  // VAT
            tb.Rows.Should().Contain(r => r.AccountCode == "5000" && r.Debit == 12m);   // COGS
            tb.Rows.Should().NotContain(r => r.AccountCode == "1000" && (r.Debit != 0m || r.Credit != 0m)); // no cash taken

            // It ages on the debtor.
            ITradingReports trading = scope.ServiceProvider.GetRequiredService<ITradingReports>();
            AgeingReport aged = await trading.AgedDebtorsAsync(companyId, new DateOnly(2026, 4, 4));
            aged.Rows.Should().Contain(r => r.TraderCode == "ROOM204" && r.Total == 115m);
        }
    }
}
