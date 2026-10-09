using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Restaurant;
using One4All.Core.Restaurant;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers routing a fired order to preparation stations: the bar gets the drinks and the grill the steaks, each on its own ticket and display.</summary>
[Collection(AccountingCollection.Name)]
public sealed class PrepStationTests
{
    private readonly AccountingFixture _fixture;

    public PrepStationTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_fired_order_is_split_into_one_ticket_per_station()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Core.Security.User waiter = new($"wtr{Guid.NewGuid():N}"[..12], "Waiter");
        db.Add(waiter);
        await db.SaveChangesAsync();
        IRestaurantService restaurant = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
        long steak = (await restaurant.CreateMenuItemAsync(companyId, "PS-STEAK", "Steak", 30m, "4000", "5000", [])).Value;
        long beer = (await restaurant.CreateMenuItemAsync(companyId, "PS-BEER", "Beer", 5m, "4000", "5000", [])).Value;
        long salad = (await restaurant.CreateMenuItemAsync(companyId, "PS-SALAD", "Salad", 8m, "4000", "5000", [])).Value;
        foreach ((long id, string station) in new[] { (steak, "Grill"), (beer, "Bar") })
        {
            (await db.Set<MenuItem>().SingleAsync(m => m.Id == id)).PrepStation = station;
        }

        await db.SaveChangesAsync();
        long table = (await restaurant.CreateTableAsync(companyId, "PS1", 4)).Value;
        DateOnly night = new(2026, 4, 10);
        long order = (await restaurant.OpenOrderAsync(companyId, table, "PS-ORD-1", waiter.Id, night, 2)).Value;
        foreach ((long item, decimal quantity) in new[] { (steak, 2m), (beer, 3m), (salad, 1m) })
        {
            (await restaurant.AddItemAsync(companyId, order, item, quantity)).IsSuccess.Should().BeTrue();
        }

        (await restaurant.FireOrderAsync(companyId, order, "KT-9", night)).IsSuccess.Should().BeTrue();
        List<KitchenTicket> tickets = await db.Set<KitchenTicket>().AsNoTracking().Include(t => t.Lines).Where(t => t.TableOrderId == order).OrderBy(t => t.TicketNumber).ToListAsync();
        tickets.Select(t => (t.TicketNumber, t.Station)).Should().Equal(("KT-9-Bar", "Bar"), ("KT-9-Grill", "Grill"), ("KT-9-KITCHEN", (string?)null));

        IReadOnlyList<StationTicket> bar = await restaurant.StationQueueAsync(companyId, "Bar");
        bar.Should().ContainSingle().Which.Lines.Should().Equal(("Beer", 3m));
        (await restaurant.StationQueueAsync(companyId, null)).Single(t => t.TicketNumber == "KT-9-KITCHEN").Lines.Should().Equal(("Salad", 1m));
    }
}
