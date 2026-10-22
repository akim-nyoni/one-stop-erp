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
public sealed class RestaurantTipPoolingAndMenuEngineeringTests
{
    private const long Waiter = 20401;
    private const long StaffA = 20411;
    private const long StaffB = 20412;

    private readonly AccountingFixture _fixture;

    public RestaurantTipPoolingAndMenuEngineeringTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Tip_pool_distributes_by_weight_and_payout_clears_the_liability()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long warehouseId;
        long orderId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Warehouse wh = new(companyId, "TPKIT", "Kitchen");
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();
            warehouseId = wh.Id;

            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            long menuId = (await res.CreateMenuItemAsync(companyId, "TP-STEAK", "Steak", 90m, "4000", "5000", [])).Value;
            long tableId = (await res.CreateTableAsync(companyId, "TP1", 2)).Value;
            orderId = (await res.OpenOrderAsync(companyId, tableId, "TP-ORD-1", Waiter, new DateOnly(2026, 3, 2))).Value;
            await res.AddItemAsync(companyId, orderId, menuId, 1m);
        }

        // Bill with a 30 tip → tips payable holds 30 for the pool.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            var bill = await res.BillOrderAsync(companyId, orderId, warehouseId, new DateOnly(2026, 3, 2), "1000", tipAmount: 30m, tipsPayableAccountCode: "2450", tillSessionId: await RestaurantTill.SessionAsync(scope.ServiceProvider, companyId));
            bill.IsSuccess.Should().BeTrue(bill.IsFailure ? bill.Error.ToString() : null);
            bill.Value.Tip.Should().Be(30m);
        }

        // Distribute the pool 2:1 between two staff → 20 and 10 (no GL, just who is owed what).
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            var dist = await res.DistributeTipsAsync(companyId, new DateOnly(2026, 3, 2), 30m, "2450",
                [new TipShare(StaffA, 2m), new TipShare(StaffB, 1m)]);
            dist.IsSuccess.Should().BeTrue(dist.IsFailure ? dist.Error.ToString() : null);
            dist.Value.Should().Be(2);

            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            List<TipAllocation> allocations = await db.Set<TipAllocation>().Where(a => a.CompanyId == companyId).OrderBy(a => a.Id).ToListAsync();
            allocations.Should().HaveCount(2);
            allocations.Sum(a => a.Amount).Should().Be(30m); // shares sum exactly to the pool
            allocations.Single(a => a.StaffUserId == StaffA).Amount.Should().Be(20m);
            allocations.Single(a => a.StaffUserId == StaffB).Amount.Should().Be(10m);
            allocations.Should().OnlyContain(a => a.Status == TipAllocationStatus.Owed);
        }

        // Pay staff A's 20 allocation → Dr tips payable, Cr bank; the allocation is marked paid.
        long staffAllocationId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            staffAllocationId = (await db.Set<TipAllocation>().FirstAsync(a => a.CompanyId == companyId && a.StaffUserId == StaffA)).Id;

            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            (await res.PayTipAllocationAsync(companyId, staffAllocationId, new DateOnly(2026, 3, 3), "1000")).IsSuccess.Should().BeTrue();
            // Paying it again is refused.
            (await res.PayTipAllocationAsync(companyId, staffAllocationId, new DateOnly(2026, 3, 3), "1000")).IsFailure.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            TipAllocation paid = await db.Set<TipAllocation>().FirstAsync(a => a.Id == staffAllocationId);
            paid.Status.Should().Be(TipAllocationStatus.Paid);

            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 3, 31));
            tb.IsBalanced.Should().BeTrue();
            // 30 credited by the bill, 20 debited by the payout → 10 still owed in the liability.
            tb.Rows.Should().Contain(r => r.AccountCode == "2450" && r.Credit == 10m);

            // The tip allocations report lists both, one paid, one still owed.
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            string tips = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "RES-TIPS", new Dictionary<string, object?>(), ReportFormat.Html)).Value.Content);
            tips.Should().Contain("Paid").And.Contain("Owed").And.Contain("20.00").And.Contain("10.00");
        }
    }

    [SkippableFact]
    public async Task Menu_engineering_classifies_items_by_popularity_and_profitability()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long warehouseId;
        long burgerId;
        long saladId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Item bun = new(companyId, "ME-BUN", "Bun") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
            Item patty = new(companyId, "ME-PATTY", "Patty") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
            Item lettuce = new(companyId, "ME-LET", "Lettuce") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
            db.Items.AddRange(bun, patty, lettuce);
            Warehouse wh = new(companyId, "MEKIT", "Kitchen");
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();
            warehouseId = wh.Id;

            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = bun.Id, WarehouseId = warehouseId, Date = new DateOnly(2026, 3, 1), Quantity = 100m, UnitCost = 2.00m, CreditAccountCode = "1100" });
            await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = patty.Id, WarehouseId = warehouseId, Date = new DateOnly(2026, 3, 1), Quantity = 100m, UnitCost = 8.00m, CreditAccountCode = "1100" });
            await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = lettuce.Id, WarehouseId = warehouseId, Date = new DateOnly(2026, 3, 1), Quantity = 100m, UnitCost = 1.00m, CreditAccountCode = "1100" });

            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            // Burger: recipe cost 10, price 50 → very profitable; sold a lot → Star.
            burgerId = (await res.CreateMenuItemAsync(companyId, "ME-BURGER", "Cheeseburger", 50m, "4000", "5000",
                [new RecipeIngredient(bun.Id, 1m), new RecipeIngredient(patty.Id, 1m)])).Value;
            // Salad: recipe cost 1, price 40 → sold rarely and (relative to burger) thinner unit margin → Dog.
            saladId = (await res.CreateMenuItemAsync(companyId, "ME-SALAD", "Garden Salad", 40m, "4000", "5000",
                [new RecipeIngredient(lettuce.Id, 1m)])).Value;
        }

        // Sell 5 burgers on one table, 1 salad on another; bill both in the period.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            long t1 = (await res.CreateTableAsync(companyId, "ME1", 4)).Value;
            long o1 = (await res.OpenOrderAsync(companyId, t1, "ME-ORD-1", Waiter, new DateOnly(2026, 3, 5))).Value;
            await res.AddItemAsync(companyId, o1, burgerId, 5m);
            (await res.BillOrderAsync(companyId, o1, warehouseId, new DateOnly(2026, 3, 5), "1000", tillSessionId: await RestaurantTill.SessionAsync(scope.ServiceProvider, companyId))).IsSuccess.Should().BeTrue();

            long t2 = (await res.CreateTableAsync(companyId, "ME2", 4)).Value;
            long o2 = (await res.OpenOrderAsync(companyId, t2, "ME-ORD-2", Waiter, new DateOnly(2026, 3, 5))).Value;
            await res.AddItemAsync(companyId, o2, saladId, 1m);
            (await res.BillOrderAsync(companyId, o2, warehouseId, new DateOnly(2026, 3, 5), "1000", tillSessionId: await RestaurantTill.SessionAsync(scope.ServiceProvider, companyId))).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            Dictionary<string, object?> ps = new()
            {
                ["FromDate"] = new DateOnly(2026, 3, 1),
                ["ToDate"] = new DateOnly(2026, 3, 31),
            };
            string mix = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "RES-MIX", ps, ReportFormat.Html)).Value.Content);
            mix.Should().Contain("Cheeseburger").And.Contain("Garden Salad");
            mix.Should().Contain("Star");   // burger: popular + profitable
            mix.Should().Contain("Dog");    // salad: unpopular + (relatively) unprofitable
            mix.Should().Contain("200.00"); // burger margin = 5 × (50 − 10)
        }
    }
}
