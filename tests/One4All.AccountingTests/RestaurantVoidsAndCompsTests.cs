using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Restaurant;
using One4All.Core.Inventory;
using One4All.Core.Restaurant;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class RestaurantVoidsAndCompsTests
{
    private const long Waiter = 20901;

    private readonly AccountingFixture _fixture;

    public RestaurantVoidsAndCompsTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Voided_line_is_excluded_and_comped_line_costs_without_revenue()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long warehouseId;
        long pattyItemId;
        long burgerId;
        long friesId;
        long dessertId;
        long orderId;
        long friesLineId;
        long dessertLineId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Item patty = new(companyId, "VC-PATTY", "Patty") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
            db.Items.Add(patty);
            Warehouse wh = new(companyId, "VCKIT", "Kitchen");
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();
            pattyItemId = patty.Id;
            warehouseId = wh.Id;

            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = pattyItemId, WarehouseId = warehouseId, Date = new DateOnly(2026, 3, 1), Quantity = 100m, UnitCost = 8.00m, CreditAccountCode = "1100" });

            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            // Burger has a recipe (1 patty @ 8); fries and dessert are recipe-less.
            burgerId = (await res.CreateMenuItemAsync(companyId, "VC-BURGER", "Burger", 50m, "4000", "5000", [new RecipeIngredient(pattyItemId, 1m)])).Value;
            friesId = (await res.CreateMenuItemAsync(companyId, "VC-FRIES", "Fries", 20m, "4000", "5000", [])).Value;
            dessertId = (await res.CreateMenuItemAsync(companyId, "VC-CAKE", "Cake", 30m, "4000", "5000", [])).Value;
            long tableId = (await res.CreateTableAsync(companyId, "VC1", 4)).Value;
            orderId = (await res.OpenOrderAsync(companyId, tableId, "VC-ORD-1", Waiter, new DateOnly(2026, 3, 22))).Value;
            await res.AddItemAsync(companyId, orderId, burgerId, 1m);      // charged: 50, cost 8
            await res.AddItemAsync(companyId, orderId, friesId, 1m);       // will be voided
        }

        // Grab the line ids for fries and dessert.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            // Add the dessert now so we can comp it.
            await res.AddItemAsync(companyId, orderId, dessertId, 1m);

            TableOrder order = await db.Set<TableOrder>().Include(o => o.Lines).FirstAsync(o => o.Id == orderId);
            friesLineId = order.Lines.First(l => l.Description == "Fries").Id;
            dessertLineId = order.Lines.First(l => l.Description == "Cake").Id;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();

            // Void the fries (sent wrong) — a reason is mandatory.
            (await res.VoidLineAsync(companyId, orderId, friesLineId, "  ")).IsFailure.Should().BeTrue();
            (await res.VoidLineAsync(companyId, orderId, friesLineId, "Wrong order")).IsSuccess.Should().BeTrue();
            // Cannot void it twice.
            (await res.VoidLineAsync(companyId, orderId, friesLineId, "again")).IsFailure.Should().BeTrue();

            // Comp the dessert (on the house).
            (await res.CompLineAsync(companyId, orderId, dessertLineId, "Birthday")).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            var bill = await res.BillOrderAsync(companyId, orderId, warehouseId, new DateOnly(2026, 3, 22), "1000");
            bill.IsSuccess.Should().BeTrue(bill.IsFailure ? bill.Error.ToString() : null);
            // Only the burger is charged; fries voided (excluded), dessert comped (no revenue).
            bill.Value.Net.Should().Be(50m);
            bill.Value.Gross.Should().Be(50m);
            // Cost of sales = burger recipe (1 patty @ 8); the comped cake has no recipe, so 8 in total.
            bill.Value.CostOfSales.Should().Be(8m);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 3, 31));
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Should().Contain(r => r.AccountCode == "4000" && r.Credit == 50m); // revenue: burger only

            // The voids & comps report lists both, with reasons.
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            Dictionary<string, object?> ps = new() { ["FromDate"] = new DateOnly(2026, 3, 1), ["ToDate"] = new DateOnly(2026, 3, 31) };
            string voids = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "RES-VOID", ps, ReportFormat.Html)).Value.Content);
            voids.Should().Contain("Wrong order").And.Contain("Birthday").And.Contain("Void").And.Contain("Comp");
        }
    }

    [SkippableFact]
    public async Task Fully_comped_order_charges_nothing_but_still_releases_cost_of_sales()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long warehouseId;
        long steakId;
        long steakItemId;
        long orderId;
        long lineId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Item steakItem = new(companyId, "VC-STEAKMEAT", "Steak meat") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
            db.Items.Add(steakItem);
            Warehouse wh = new(companyId, "VC2KIT", "Kitchen");
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();
            steakItemId = steakItem.Id;
            warehouseId = wh.Id;

            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = steakItemId, WarehouseId = warehouseId, Date = new DateOnly(2026, 3, 1), Quantity = 50m, UnitCost = 12.00m, CreditAccountCode = "1100" });

            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            steakId = (await res.CreateMenuItemAsync(companyId, "VC-STEAK", "Steak", 90m, "4000", "5000", [new RecipeIngredient(steakItemId, 1m)])).Value;
            long tableId = (await res.CreateTableAsync(companyId, "VC2", 2)).Value;
            orderId = (await res.OpenOrderAsync(companyId, tableId, "VC-ORD-2", Waiter, new DateOnly(2026, 3, 23))).Value;
            await res.AddItemAsync(companyId, orderId, steakId, 1m);

            TableOrder order = await db.Set<TableOrder>().Include(o => o.Lines).FirstAsync(o => o.Id == orderId);
            lineId = order.Lines.Single().Id;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            (await res.CompLineAsync(companyId, orderId, lineId, "Service recovery")).IsSuccess.Should().BeTrue();

            var bill = await res.BillOrderAsync(companyId, orderId, warehouseId, new DateOnly(2026, 3, 23), "1000");
            bill.IsSuccess.Should().BeTrue(bill.IsFailure ? bill.Error.ToString() : null);
            bill.Value.Gross.Should().Be(0m);        // guest charged nothing
            bill.Value.CostOfSales.Should().Be(12m); // but the steak was still made
            bill.Value.FullyBilled.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            TableOrder order = await db.Set<TableOrder>().FirstAsync(o => o.Id == orderId);
            order.Status.Should().Be(TableOrderStatus.Billed); // closed even though nothing was charged

            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 3, 31));
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Should().Contain(r => r.AccountCode == "5000" && r.Debit == 12m);      // COGS recognised
            tb.Rows.Should().NotContain(r => r.AccountCode == "4000" && r.Credit != 0m);   // no revenue
        }
    }
}
