using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Restaurant;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class RestaurantAvailabilityKotReportTests
{
    private const long Waiter = 20401;

    private readonly AccountingFixture _fixture;

    public RestaurantAvailabilityKotReportTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Menu_availability_gates_ordering_and_kot_report_lists_open_tickets()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long soupId;
        long tableId;
        long orderId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            soupId = (await res.CreateMenuItemAsync(companyId, "SOUP", "Soup of the day", 25m, "4000", "5000", [])).Value;
            tableId = (await res.CreateTableAsync(companyId, "T3", 2)).Value;
            orderId = (await res.OpenOrderAsync(companyId, tableId, "ORD-3", Waiter, new DateOnly(2026, 3, 2))).Value;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            // 86 the soup — it can no longer be ordered.
            (await res.SetMenuAvailabilityAsync(companyId, soupId, false)).IsSuccess.Should().BeTrue();
            var blocked = await res.AddItemAsync(companyId, orderId, soupId, 1m);
            blocked.IsFailure.Should().BeTrue();
            blocked.Error.Code.Should().Be("Res.MenuItemUnavailable");

            // Bring it back and order it, then fire it.
            (await res.SetMenuAvailabilityAsync(companyId, soupId, true)).IsSuccess.Should().BeTrue();
            (await res.AddItemAsync(companyId, orderId, soupId, 2m)).IsSuccess.Should().BeTrue();
            (await res.FireOrderAsync(companyId, orderId, "KOT-9", new DateOnly(2026, 3, 2))).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();

            // Menu report shows availability.
            string menu = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "RES-MENU", new Dictionary<string, object?>(), ReportFormat.Html)).Value.Content);
            menu.Should().Contain("SOUP").And.Contain("Available").And.Contain("Yes");

            // KOT report lists the open ticket.
            string kot = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "RES-KOT", new Dictionary<string, object?>(), ReportFormat.Html)).Value.Content);
            kot.Should().Contain("KOT-9").And.Contain("Sent");
        }
    }
}
