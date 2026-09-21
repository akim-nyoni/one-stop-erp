using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Restaurant;
using One4All.Core.Restaurant;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class RestaurantCoursingTests
{
    private const long Waiter = 20801;

    private readonly AccountingFixture _fixture;

    public RestaurantCoursingTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Courses_fire_independently_to_the_kitchen()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long starterId;
        long mainId;
        long dessertId;
        long orderId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            starterId = (await res.CreateMenuItemAsync(companyId, "CRS-SOUP", "Soup", 20m, "4000", "5000", [])).Value;
            mainId = (await res.CreateMenuItemAsync(companyId, "CRS-STEAK", "Steak", 90m, "4000", "5000", [])).Value;
            dessertId = (await res.CreateMenuItemAsync(companyId, "CRS-CAKE", "Cake", 30m, "4000", "5000", [])).Value;
            long tableId = (await res.CreateTableAsync(companyId, "CR1", 2)).Value;
            orderId = (await res.OpenOrderAsync(companyId, tableId, "CRS-ORD-1", Waiter, new DateOnly(2026, 3, 20))).Value;
            await res.AddItemAsync(companyId, orderId, starterId, 1m, course: MenuCourse.Starter);
            await res.AddItemAsync(companyId, orderId, mainId, 1m, course: MenuCourse.Main);
            await res.AddItemAsync(companyId, orderId, dessertId, 1m, course: MenuCourse.Dessert);
        }

        long starterTicketId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();

            // Fire the starter only.
            var fired = await res.FireCourseAsync(companyId, orderId, MenuCourse.Starter, "KOT-S1", new DateOnly(2026, 3, 20));
            fired.IsSuccess.Should().BeTrue(fired.IsFailure ? fired.Error.ToString() : null);
            starterTicketId = fired.Value;

            // Firing the starter again → nothing left in that course.
            (await res.FireCourseAsync(companyId, orderId, MenuCourse.Starter, "KOT-S2", new DateOnly(2026, 3, 20))).IsFailure.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            KitchenTicket starterTicket = await db.Set<KitchenTicket>().Include(t => t.Lines).FirstAsync(t => t.Id == starterTicketId);
            starterTicket.Course.Should().Be(MenuCourse.Starter);
            starterTicket.Lines.Should().ContainSingle().Which.Description.Should().Be("Soup");
        }

        long mainTicketId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();

            // Later, fire the main.
            mainTicketId = (await res.FireCourseAsync(companyId, orderId, MenuCourse.Main, "KOT-M1", new DateOnly(2026, 3, 20))).Value;

            // "Fire everything else" picks up only the still-unfired dessert.
            long restId = (await res.FireOrderAsync(companyId, orderId, "KOT-REST", new DateOnly(2026, 3, 20))).Value;

            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            KitchenTicket mainTicket = await db.Set<KitchenTicket>().Include(t => t.Lines).FirstAsync(t => t.Id == mainTicketId);
            mainTicket.Lines.Should().ContainSingle().Which.Description.Should().Be("Steak");
            KitchenTicket restTicket = await db.Set<KitchenTicket>().Include(t => t.Lines).FirstAsync(t => t.Id == restId);
            restTicket.Lines.Should().ContainSingle().Which.Description.Should().Be("Cake");

            // Nothing left unfired at all.
            (await res.FireOrderAsync(companyId, orderId, "KOT-NONE", new DateOnly(2026, 3, 20))).IsFailure.Should().BeTrue();
        }
    }
}
