using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Restaurant;
using One4All.Core.Restaurant;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class RestaurantSpendLimitTests
{
    private const long Waiter = 22201;

    private readonly AccountingFixture _fixture;

    public RestaurantSpendLimitTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Bar_tab_spend_limit_blocks_items_that_would_exceed_it()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long beerId;
        long orderId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            beerId = (await res.CreateMenuItemAsync(companyId, "BT-BEER", "Beer", 10m, "4000", "5000", [])).Value;
            long tableId = (await res.CreateTableAsync(companyId, "BT1", 2)).Value;
            orderId = (await res.OpenOrderAsync(companyId, tableId, "BT-ORD-1", Waiter, new DateOnly(2026, 4, 12))).Value;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            // Pre-authorise a $50 tab.
            (await res.SetOrderSpendLimitAsync(companyId, orderId, 50m)).IsSuccess.Should().BeTrue();

            // 4 beers = 40 is fine; a 5th round of 2 (→60) is refused.
            (await res.AddItemAsync(companyId, orderId, beerId, 4m)).IsSuccess.Should().BeTrue();      // 40
            (await res.AddItemAsync(companyId, orderId, beerId, 2m)).IsFailure.Should().BeTrue();       // would be 60
            (await res.AddItemAsync(companyId, orderId, beerId, 1m)).IsSuccess.Should().BeTrue();       // 50 exactly

            // At the limit, another is refused.
            (await res.AddItemAsync(companyId, orderId, beerId, 1m)).IsFailure.Should().BeTrue();       // would be 60

            // Raise the tab and it flows again.
            (await res.SetOrderSpendLimitAsync(companyId, orderId, 100m)).IsSuccess.Should().BeTrue();
            (await res.AddItemAsync(companyId, orderId, beerId, 3m)).IsSuccess.Should().BeTrue();       // 80

            // Clearing the limit removes the cap entirely.
            (await res.SetOrderSpendLimitAsync(companyId, orderId, null)).IsSuccess.Should().BeTrue();
            (await res.AddItemAsync(companyId, orderId, beerId, 50m)).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            TableOrder order = await db.Set<TableOrder>().Include(o => o.Lines).FirstAsync(o => o.Id == orderId);
            order.NetTotal.Should().Be(580m); // (4 + 1 + 3 + 50) beers × 10
            order.SpendLimit.Should().BeNull();
        }
    }
}
