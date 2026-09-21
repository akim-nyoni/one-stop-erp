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
public sealed class RestaurantVoidAuthoriserTests
{
    private const long Waiter = 22301;

    private readonly AccountingFixture _fixture;

    public RestaurantVoidAuthoriserTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Voids_and_comps_record_the_authorising_manager_on_the_oversight_report()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long warehouseId;
        long dishId;
        long managerId;
        long orderId;
        long voidLineId;
        long compLineId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Core.Inventory.Warehouse wh = new(companyId, "VAKIT", "Kitchen");
            db.Warehouses.Add(wh);
            Core.Security.User manager = new("mgr", "Morgan Manager");
            db.Set<Core.Security.User>().Add(manager);
            await db.SaveChangesAsync();
            warehouseId = wh.Id;
            managerId = manager.Id;

            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            dishId = (await res.CreateMenuItemAsync(companyId, "VA-DISH", "Dish", 30m, "4000", "5000", [])).Value;
            long tableId = (await res.CreateTableAsync(companyId, "VA1", 4)).Value;
            orderId = (await res.OpenOrderAsync(companyId, tableId, "VA-ORD-1", Waiter, new DateOnly(2026, 4, 13))).Value;
            await res.AddItemAsync(companyId, orderId, dishId, 1m);
            await res.AddItemAsync(companyId, orderId, dishId, 1m);

            TableOrder order = await db.Set<TableOrder>().Include(o => o.Lines).FirstAsync(o => o.Id == orderId);
            voidLineId = order.Lines.First().Id;
            compLineId = order.Lines.Last().Id;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            (await res.VoidLineAsync(companyId, orderId, voidLineId, "Sent back", managerId)).IsSuccess.Should().BeTrue();
            (await res.CompLineAsync(companyId, orderId, compLineId, "Regular guest", managerId)).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            (await db.Set<TableOrderLine>().FirstAsync(l => l.Id == voidLineId)).AuthorizedByUserId.Should().Be(managerId);

            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            Dictionary<string, object?> ps = new() { ["FromDate"] = new DateOnly(2026, 4, 1), ["ToDate"] = new DateOnly(2026, 4, 30) };
            string voids = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "RES-VOID", ps, ReportFormat.Html)).Value.Content);
            voids.Should().Contain("Sent back").And.Contain("Regular guest").And.Contain("Morgan Manager");
        }
    }
}
