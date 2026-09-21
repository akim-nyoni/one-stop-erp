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
public sealed class RestaurantSeatBillingTests
{
    private const long Waiter = 21101;

    private readonly AccountingFixture _fixture;

    public RestaurantSeatBillingTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Each_seat_settles_its_own_share_and_the_last_seat_closes_the_table()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long warehouseId;
        long steakId;
        long wineId;
        long saladId;
        long orderId;
        long tableId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Core.Inventory.Warehouse wh = new(companyId, "STKIT", "Kitchen");
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();
            warehouseId = wh.Id;

            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            steakId = (await res.CreateMenuItemAsync(companyId, "ST-STEAK", "Steak", 90m, "4000", "5000", [])).Value;
            wineId = (await res.CreateMenuItemAsync(companyId, "ST-WINE", "Wine", 40m, "4000", "5000", [])).Value;
            saladId = (await res.CreateMenuItemAsync(companyId, "ST-SALAD", "Salad", 30m, "4000", "5000", [])).Value;
            tableId = (await res.CreateTableAsync(companyId, "ST1", 4)).Value;
            orderId = (await res.OpenOrderAsync(companyId, tableId, "ST-ORD-1", Waiter, new DateOnly(2026, 3, 27))).Value;
            // Seat 1: steak + wine (130). Seat 2: salad (30).
            await res.AddItemAsync(companyId, orderId, steakId, 1m, seatNumber: 1);
            await res.AddItemAsync(companyId, orderId, wineId, 1m, seatNumber: 1);
            await res.AddItemAsync(companyId, orderId, saladId, 1m, seatNumber: 2);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();

            // Bill seat 1 → 130, order not fully billed yet.
            var seat1 = await res.BillSeatAsync(companyId, orderId, 1, warehouseId, new DateOnly(2026, 3, 27), "1000");
            seat1.IsSuccess.Should().BeTrue(seat1.IsFailure ? seat1.Error.ToString() : null);
            seat1.Value.Net.Should().Be(130m);
            seat1.Value.FullyBilled.Should().BeFalse();

            // Re-billing seat 1 → nothing left for that seat.
            (await res.BillSeatAsync(companyId, orderId, 1, warehouseId, new DateOnly(2026, 3, 27), "1000")).IsFailure.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            (await db.Set<RestaurantTable>().FirstAsync(t => t.Id == tableId)).Status.Should().Be(TableStatus.Occupied); // still seated

            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            // Bill seat 2 → 30, now fully billed and the table frees.
            var seat2 = await res.BillSeatAsync(companyId, orderId, 2, warehouseId, new DateOnly(2026, 3, 27), "1000");
            seat2.IsSuccess.Should().BeTrue(seat2.IsFailure ? seat2.Error.ToString() : null);
            seat2.Value.Net.Should().Be(30m);
            seat2.Value.FullyBilled.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            (await db.Set<RestaurantTable>().FirstAsync(t => t.Id == tableId)).Status.Should().Be(TableStatus.Free);
            (await db.Set<TableOrder>().FirstAsync(o => o.Id == orderId)).Status.Should().Be(TableOrderStatus.Billed);

            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 3, 31));
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Should().Contain(r => r.AccountCode == "4000" && r.Credit == 160m); // 130 + 30 across the two seat bills
        }
    }
}
