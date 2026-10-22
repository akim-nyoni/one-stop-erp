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
public sealed class RestaurantServerSalesTests
{
    private readonly AccountingFixture _fixture;

    public RestaurantServerSalesTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Orders_can_be_reassigned_and_server_sales_report_credits_the_current_waiter()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long warehouseId;
        long menuId;
        long anaId;
        long benId;
        long anaOrder;
        long handoverOrder;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Core.Inventory.Warehouse wh = new(companyId, "SVKIT", "Kitchen");
            db.Warehouses.Add(wh);
            Core.Security.User ana = new("ana", "Ana Server");
            Core.Security.User ben = new("ben", "Ben Server");
            db.Set<Core.Security.User>().AddRange(ana, ben);
            await db.SaveChangesAsync();
            warehouseId = wh.Id;
            anaId = ana.Id;
            benId = ben.Id;

            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            menuId = (await res.CreateMenuItemAsync(companyId, "SV-DISH", "Dish", 50m, "4000", "5000", [])).Value;
            long t1 = (await res.CreateTableAsync(companyId, "SV1", 4)).Value;
            long t2 = (await res.CreateTableAsync(companyId, "SV2", 4)).Value;

            anaOrder = (await res.OpenOrderAsync(companyId, t1, "SV-ORD-1", anaId, new DateOnly(2026, 4, 2))).Value;
            await res.AddItemAsync(companyId, anaOrder, menuId, 2m); // 100, Ana

            // Opened by Ana but handed over to Ben mid-service.
            handoverOrder = (await res.OpenOrderAsync(companyId, t2, "SV-ORD-2", anaId, new DateOnly(2026, 4, 2))).Value;
            await res.AddItemAsync(companyId, handoverOrder, menuId, 1m); // 50, will move to Ben
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            (await res.ReassignWaiterAsync(companyId, handoverOrder, benId)).IsSuccess.Should().BeTrue();

            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            (await db.Set<TableOrder>().FirstAsync(o => o.Id == handoverOrder)).WaiterUserId.Should().Be(benId);

            await res.BillOrderAsync(companyId, anaOrder, warehouseId, new DateOnly(2026, 4, 2), "1000", tillSessionId: await RestaurantTill.SessionAsync(scope.ServiceProvider, companyId));
            await res.BillOrderAsync(companyId, handoverOrder, warehouseId, new DateOnly(2026, 4, 2), "1000", tillSessionId: await RestaurantTill.SessionAsync(scope.ServiceProvider, companyId));

            // A billed order cannot be reassigned.
            (await res.ReassignWaiterAsync(companyId, anaOrder, benId)).IsFailure.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            Dictionary<string, object?> ps = new() { ["FromDate"] = new DateOnly(2026, 4, 1), ["ToDate"] = new DateOnly(2026, 4, 30) };
            string server = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "RES-SERVER", ps, ReportFormat.Html)).Value.Content);
            // Ana keeps her 100; the handover order's 50 now credits Ben.
            server.Should().Contain("Ana Server").And.Contain("Ben Server");
            server.Should().Contain("100.00").And.Contain("50.00");

            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 4, 30));
            tb.IsBalanced.Should().BeTrue();
        }
    }
}
