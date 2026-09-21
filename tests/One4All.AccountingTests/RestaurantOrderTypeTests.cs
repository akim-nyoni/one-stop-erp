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
public sealed class RestaurantOrderTypeTests
{
    private const long Waiter = 21401;

    private readonly AccountingFixture _fixture;

    public RestaurantOrderTypeTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Takeaway_and_delivery_orders_bill_with_no_table_and_split_by_type()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long warehouseId;
        long menuId;
        long dineOrderId;
        long takeawayId;
        long deliveryId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Core.Inventory.Warehouse wh = new(companyId, "OTKIT", "Kitchen");
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();
            warehouseId = wh.Id;

            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            menuId = (await res.CreateMenuItemAsync(companyId, "OT-PIZZA", "Pizza", 60m, "4000", "5000", [])).Value;
            long tableId = (await res.CreateTableAsync(companyId, "OT1", 4)).Value;

            // Dine-in via a table.
            dineOrderId = (await res.OpenOrderAsync(companyId, tableId, "OT-DINE-1", Waiter, new DateOnly(2026, 3, 31))).Value;
            await res.AddItemAsync(companyId, dineOrderId, menuId, 1m); // 60

            // A dine-in type cannot be opened off-premise.
            (await res.OpenOffPremiseOrderAsync(companyId, "OT-BAD", OrderType.DineIn, Waiter, new DateOnly(2026, 3, 31))).IsFailure.Should().BeTrue();

            // Takeaway + delivery with no table.
            takeawayId = (await res.OpenOffPremiseOrderAsync(companyId, "OT-TAKE-1", OrderType.Takeaway, Waiter, new DateOnly(2026, 3, 31), "Ada")).Value;
            await res.AddItemAsync(companyId, takeawayId, menuId, 2m); // 120

            deliveryId = (await res.OpenOffPremiseOrderAsync(companyId, "OT-DEL-1", OrderType.Delivery, Waiter, new DateOnly(2026, 3, 31), "Grace", "555-9")).Value;
            await res.AddItemAsync(companyId, deliveryId, menuId, 1m); // 60
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            TableOrder takeaway = await db.Set<TableOrder>().FirstAsync(o => o.Id == takeawayId);
            takeaway.TableId.Should().BeNull();
            takeaway.OrderType.Should().Be(OrderType.Takeaway);
            takeaway.CustomerName.Should().Be("Ada");
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            // All three bill through the same path; off-premise ones free no table.
            (await res.BillOrderAsync(companyId, dineOrderId, warehouseId, new DateOnly(2026, 3, 31), "1000")).Value.Gross.Should().Be(60m);
            var takeBill = await res.BillOrderAsync(companyId, takeawayId, warehouseId, new DateOnly(2026, 3, 31), "1000");
            takeBill.IsSuccess.Should().BeTrue(takeBill.IsFailure ? takeBill.Error.ToString() : null);
            takeBill.Value.Gross.Should().Be(120m);
            takeBill.Value.FullyBilled.Should().BeTrue();
            (await res.BillOrderAsync(companyId, deliveryId, warehouseId, new DateOnly(2026, 3, 31), "1000")).Value.Gross.Should().Be(60m);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            Dictionary<string, object?> ps = new() { ["FromDate"] = new DateOnly(2026, 3, 1), ["ToDate"] = new DateOnly(2026, 3, 31) };
            string byType = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "RES-OTYPE", ps, ReportFormat.Html)).Value.Content);
            byType.Should().Contain("DineIn").And.Contain("Takeaway").And.Contain("Delivery");
            byType.Should().Contain("120.00"); // takeaway net

            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 3, 31));
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Should().Contain(r => r.AccountCode == "4000" && r.Credit == 240m); // 60 + 120 + 60
        }
    }
}
