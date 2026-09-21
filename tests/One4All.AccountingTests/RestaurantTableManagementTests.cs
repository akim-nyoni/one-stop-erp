using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Restaurant;
using One4All.Core.Restaurant;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class RestaurantTableManagementTests
{
    private const long Waiter = 20101;

    private readonly AccountingFixture _fixture;

    public RestaurantTableManagementTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Orders_can_be_transferred_between_tables_and_cancelled()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long t1;
        long t2;
        long menuId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            t1 = (await res.CreateTableAsync(companyId, "T1", 2)).Value;
            t2 = (await res.CreateTableAsync(companyId, "T2", 4)).Value;
            menuId = (await res.CreateMenuItemAsync(companyId, "COFFEE", "Coffee", 20m, "4000", "5000", [])).Value;
        }

        long orderId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            orderId = (await res.OpenOrderAsync(companyId, t1, "ORD-T1", Waiter, new DateOnly(2026, 3, 2))).Value;
            await res.AddItemAsync(companyId, orderId, menuId, 1m);

            // Transfer the order from T1 to T2.
            var transfer = await res.TransferOrderAsync(companyId, orderId, t2);
            transfer.IsSuccess.Should().BeTrue(transfer.IsFailure ? transfer.Error.ToString() : null);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            (await db.Set<RestaurantTable>().FindAsync(t1))!.Status.Should().Be(TableStatus.Free);     // origin freed
            (await db.Set<RestaurantTable>().FindAsync(t2))!.Status.Should().Be(TableStatus.Occupied);  // destination occupied
            (await db.Set<TableOrder>().FindAsync(orderId))!.TableId.Should().Be(t2);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            (await res.CancelOrderAsync(companyId, orderId, "  ")).IsFailure.Should().BeTrue(); // reason required
            var cancel = await res.CancelOrderAsync(companyId, orderId, "Guests left");
            cancel.IsSuccess.Should().BeTrue(cancel.IsFailure ? cancel.Error.ToString() : null);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            (await db.Set<TableOrder>().FindAsync(orderId))!.Status.Should().Be(TableOrderStatus.Cancelled);
            (await db.Set<RestaurantTable>().FindAsync(t2))!.Status.Should().Be(TableStatus.Free); // freed on cancel
        }
    }
}
