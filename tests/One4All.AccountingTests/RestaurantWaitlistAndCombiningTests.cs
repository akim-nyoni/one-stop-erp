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
public sealed class RestaurantWaitlistAndCombiningTests
{
    private const long Waiter = 20601;

    private readonly AccountingFixture _fixture;

    public RestaurantWaitlistAndCombiningTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Waitlist_queues_walk_ins_and_seating_opens_an_order()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long tableId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            tableId = (await res.CreateTableAsync(companyId, "W1", 4)).Value;
        }

        long firstId;
        long secondId;
        long leaverId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            firstId = (await res.AddToWaitlistAsync(companyId, "Alice", 2, 20, new DateTime(2026, 3, 14, 18, 0, 0, DateTimeKind.Utc), "555-1")).Value;
            leaverId = (await res.AddToWaitlistAsync(companyId, "Bob", 3, 30, new DateTime(2026, 3, 14, 18, 5, 0, DateTimeKind.Utc))).Value;
            secondId = (await res.AddToWaitlistAsync(companyId, "Carol", 4, 40, new DateTime(2026, 3, 14, 18, 10, 0, DateTimeKind.Utc))).Value;

            // A zero party is refused.
            (await res.AddToWaitlistAsync(companyId, "Nobody", 0, 10, new DateTime(2026, 3, 14, 18, 15, 0, DateTimeKind.Utc))).IsFailure.Should().BeTrue();
        }

        // The waitlist report lists all waiting parties in join order.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            string wait = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "RES-WAIT", new Dictionary<string, object?>(), ReportFormat.Html)).Value.Content);
            wait.Should().Contain("Alice").And.Contain("Bob").And.Contain("Carol");
            wait.IndexOf("Alice", StringComparison.Ordinal).Should().BeLessThan(wait.IndexOf("Carol", StringComparison.Ordinal)); // queue order
        }

        long orderId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();

            // Bob gives up and leaves the queue; a no-show and a left entry cannot be seated.
            (await res.RemoveFromWaitlistAsync(companyId, leaverId)).IsSuccess.Should().BeTrue();
            (await res.SeatFromWaitlistAsync(companyId, leaverId, tableId, "W-ORD-X", Waiter, new DateOnly(2026, 3, 14))).IsFailure.Should().BeTrue();

            // Seat Alice at the free table → opens an order.
            var seated = await res.SeatFromWaitlistAsync(companyId, firstId, tableId, "W-ORD-1", Waiter, new DateOnly(2026, 3, 14));
            seated.IsSuccess.Should().BeTrue(seated.IsFailure ? seated.Error.ToString() : null);
            orderId = seated.Value;

            // The table is now occupied, so Carol cannot be seated there.
            (await res.SeatFromWaitlistAsync(companyId, secondId, tableId, "W-ORD-2", Waiter, new DateOnly(2026, 3, 14))).IsFailure.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            WaitlistEntry alice = await db.Set<WaitlistEntry>().FirstAsync(w => w.Id == firstId);
            alice.Status.Should().Be(WaitlistStatus.Seated);
            alice.SeatedTableId.Should().Be(tableId);
            alice.SeatedOrderId.Should().Be(orderId);
            (await db.Set<WaitlistEntry>().FirstAsync(w => w.Id == leaverId)).Status.Should().Be(WaitlistStatus.Left);

            RestaurantTable table = await db.Set<RestaurantTable>().FirstAsync(t => t.Id == tableId);
            table.Status.Should().Be(TableStatus.Occupied);

            // Seated + left parties drop off the waiting list; Carol remains.
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            string wait = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "RES-WAIT", new Dictionary<string, object?>(), ReportFormat.Html)).Value.Content);
            wait.Should().NotContain("Alice").And.NotContain("Bob").And.Contain("Carol");
        }
    }

    [SkippableFact]
    public async Task Combined_tables_occupy_and_free_together_and_bill_through_the_primary()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long warehouseId;
        long menuId;
        long t1;
        long t2;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Core.Inventory.Warehouse wh = new(companyId, "CBKIT", "Kitchen");
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();
            warehouseId = wh.Id;

            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            menuId = (await res.CreateMenuItemAsync(companyId, "CB-PLATTER", "Sharing Platter", 120m, "4000", "5000", [])).Value;
            t1 = (await res.CreateTableAsync(companyId, "C1", 4)).Value;
            t2 = (await res.CreateTableAsync(companyId, "C2", 4)).Value;
        }

        long groupId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();

            // Fewer than two tables, or an unknown table, is refused.
            (await res.CombineTablesAsync(companyId, [t1])).IsFailure.Should().BeTrue();

            var combined = await res.CombineTablesAsync(companyId, [t2, t1]); // order shouldn't matter
            combined.IsSuccess.Should().BeTrue(combined.IsFailure ? combined.Error.ToString() : null);
            groupId = combined.Value;

            // Combining an already-combined table is refused.
            (await res.CombineTablesAsync(companyId, [t1, t2])).IsFailure.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            TableGroup group = await db.Set<TableGroup>().FirstAsync(g => g.Id == groupId);
            group.Seats.Should().Be(8);        // 4 + 4
            group.MemberCount.Should().Be(2);
            group.PrimaryTableId.Should().Be(t1); // C1 sorts before C2

            // The combined group shows on the RES-COMB report.
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            string comb = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "RES-COMB", new Dictionary<string, object?>(), ReportFormat.Html)).Value.Content);
            comb.Should().Contain("C1+C2").And.Contain("C1");
        }

        long orderId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();

            // Ordering on the non-primary member is refused; use the primary.
            (await res.OpenOrderAsync(companyId, t2, "CB-ORD-X", Waiter, new DateOnly(2026, 3, 16))).IsFailure.Should().BeTrue();

            orderId = (await res.OpenOrderAsync(companyId, t1, "CB-ORD-1", Waiter, new DateOnly(2026, 3, 16))).Value;
            await res.AddItemAsync(companyId, orderId, menuId, 1m);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            // Opening the order on the primary occupies the whole group.
            (await db.Set<RestaurantTable>().FirstAsync(t => t.Id == t1)).Status.Should().Be(TableStatus.Occupied);
            (await db.Set<RestaurantTable>().FirstAsync(t => t.Id == t2)).Status.Should().Be(TableStatus.Occupied);

            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            // The group is seated, so it cannot be released yet.
            (await res.UncombineTablesAsync(companyId, groupId)).IsFailure.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            var bill = await res.BillOrderAsync(companyId, orderId, warehouseId, new DateOnly(2026, 3, 16), "1000");
            bill.IsSuccess.Should().BeTrue(bill.IsFailure ? bill.Error.ToString() : null);
            bill.Value.Gross.Should().Be(120m);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            // Billing frees the whole group.
            (await db.Set<RestaurantTable>().FirstAsync(t => t.Id == t1)).Status.Should().Be(TableStatus.Free);
            (await db.Set<RestaurantTable>().FirstAsync(t => t.Id == t2)).Status.Should().Be(TableStatus.Free);

            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            // Now free, the group can be released; the tables leave the group.
            (await res.UncombineTablesAsync(companyId, groupId)).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            (await db.Set<TableGroup>().FirstAsync(g => g.Id == groupId)).Status.Should().Be(TableGroupStatus.Released);
            (await db.Set<RestaurantTable>().FirstAsync(t => t.Id == t1)).GroupId.Should().BeNull();
            (await db.Set<RestaurantTable>().FirstAsync(t => t.Id == t2)).GroupId.Should().BeNull();

            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 3, 31));
            tb.IsBalanced.Should().BeTrue();

            // Separated tables can be combined again.
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            (await res.CombineTablesAsync(companyId, [t1, t2])).IsSuccess.Should().BeTrue();
        }
    }
}
