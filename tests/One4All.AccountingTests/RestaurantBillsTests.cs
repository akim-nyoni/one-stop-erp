using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Restaurant;
using One4All.Core.Restaurant;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class RestaurantBillsTests
{
    private const long Waiter = 22401;

    private readonly AccountingFixture _fixture;

    public RestaurantBillsTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Every_bill_is_persisted_with_its_money_breakdown()
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
            Core.Inventory.Warehouse wh = new(companyId, "BLKIT", "Kitchen");
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();
            warehouseId = wh.Id;

            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            menuId = (await res.CreateMenuItemAsync(companyId, "BL-MEAL", "Meal", 100m, "4000", "5000", [])).Value;
            long tableId = (await res.CreateTableAsync(companyId, "BL1", 4)).Value;
            orderId = (await res.OpenOrderAsync(companyId, tableId, "BL-ORD-1", Waiter, new DateOnly(2026, 4, 14))).Value;
            await res.AddItemAsync(companyId, orderId, menuId, 1m, taxAmount: 15m);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            // Bill with a 12 service charge and 8 discount.
            var bill = await res.BillOrderAsync(companyId, orderId, warehouseId, new DateOnly(2026, 4, 14), "1000",
                taxAccountCode: "2200", serviceChargeAmount: 12m, serviceChargeAccountCode: "4600", discountAmount: 8m, discountAccountCode: "4700", tillSessionId: await RestaurantTill.SessionAsync(scope.ServiceProvider, companyId));
            bill.IsSuccess.Should().BeTrue(bill.IsFailure ? bill.Error.ToString() : null);
            bill.Value.Gross.Should().Be(119m); // 100 + 15 + 12 - 8
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            RestaurantBill persisted = await db.Set<RestaurantBill>().FirstAsync(b => b.TableOrderId == orderId);
            persisted.Net.Should().Be(100m);
            persisted.Tax.Should().Be(15m);
            persisted.ServiceCharge.Should().Be(12m);
            persisted.Discount.Should().Be(8m);
            persisted.Gross.Should().Be(119m);
            persisted.RevenueJournalNumber.Should().NotBeNullOrEmpty();

            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            Dictionary<string, object?> ps = new() { ["FromDate"] = new DateOnly(2026, 4, 1), ["ToDate"] = new DateOnly(2026, 4, 30) };
            string bills = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "RES-BILLS", ps, ReportFormat.Html)).Value.Content);
            bills.Should().Contain("BL-ORD-1").And.Contain("119.00");
        }
    }
}
