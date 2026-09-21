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
public sealed class RestaurantFloorSectionTests
{
    private const long Waiter = 20701;

    private readonly AccountingFixture _fixture;

    public RestaurantFloorSectionTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Floor_sections_group_tables_assign_a_server_and_report_occupancy_and_sales()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long warehouseId;
        long menuId;
        long patioId;
        long barId;
        long serverUserId;
        long p1;
        long p2;
        long b1;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Core.Inventory.Warehouse wh = new(companyId, "FSKIT", "Kitchen");
            db.Warehouses.Add(wh);
            Core.Security.User server = new("patioserver", "Pat the Server");
            db.Set<Core.Security.User>().Add(server);
            await db.SaveChangesAsync();
            warehouseId = wh.Id;
            serverUserId = server.Id;

            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            menuId = (await res.CreateMenuItemAsync(companyId, "FS-DISH", "Dish", 40m, "4000", "5000", [])).Value;

            patioId = (await res.CreateSectionAsync(companyId, "PATIO", "Patio", "Outdoor seating")).Value;
            barId = (await res.CreateSectionAsync(companyId, "BAR", "Bar")).Value;
            // Duplicate code is refused.
            (await res.CreateSectionAsync(companyId, "patio", "Patio 2")).IsFailure.Should().BeTrue();

            p1 = (await res.CreateTableAsync(companyId, "P1", 2)).Value;
            p2 = (await res.CreateTableAsync(companyId, "P2", 4)).Value;
            b1 = (await res.CreateTableAsync(companyId, "B1", 2)).Value;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            (await res.AssignTableToSectionAsync(companyId, p1, patioId)).IsSuccess.Should().BeTrue();
            (await res.AssignTableToSectionAsync(companyId, p2, patioId)).IsSuccess.Should().BeTrue();
            (await res.AssignTableToSectionAsync(companyId, b1, barId)).IsSuccess.Should().BeTrue();
            (await res.AssignSectionServerAsync(companyId, patioId, serverUserId)).IsSuccess.Should().BeTrue();

            // Assigning a table to a missing section is refused.
            (await res.AssignTableToSectionAsync(companyId, p1, 999999)).IsFailure.Should().BeTrue();
        }

        // Seat and bill a patio table so the section shows occupancy and, later, sales.
        long orderId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            orderId = (await res.OpenOrderAsync(companyId, p1, "FS-ORD-1", Waiter, new DateOnly(2026, 3, 18))).Value;
            await res.AddItemAsync(companyId, orderId, menuId, 2m); // net 80
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            string plan = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "RES-SECT", new Dictionary<string, object?>(), ReportFormat.Html)).Value.Content);
            plan.Should().Contain("Patio").And.Contain("Bar");
            plan.Should().Contain("6");   // patio seats 2 + 4
            plan.Should().Contain("Pat the Server"); // the assigned section server
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            var bill = await res.BillOrderAsync(companyId, orderId, warehouseId, new DateOnly(2026, 3, 18), "1000");
            bill.IsSuccess.Should().BeTrue(bill.IsFailure ? bill.Error.ToString() : null);
            bill.Value.Net.Should().Be(80m);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            Dictionary<string, object?> ps = new()
            {
                ["FromDate"] = new DateOnly(2026, 3, 1),
                ["ToDate"] = new DateOnly(2026, 3, 31),
            };
            string sales = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "RES-SSALES", ps, ReportFormat.Html)).Value.Content);
            sales.Should().Contain("Patio").And.Contain("80.00"); // patio net sales

            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 3, 31));
            tb.IsBalanced.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            // Close the bar for service, and unassign a table from the patio.
            (await res.SetSectionActiveAsync(companyId, barId, false)).IsSuccess.Should().BeTrue();
            (await res.AssignTableToSectionAsync(companyId, p2, null)).IsSuccess.Should().BeTrue();

            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            (await db.Set<FloorSection>().FirstAsync(s => s.Id == barId)).IsActive.Should().BeFalse();
            (await db.Set<RestaurantTable>().FirstAsync(t => t.Id == p2)).SectionId.Should().BeNull();
        }
    }
}
