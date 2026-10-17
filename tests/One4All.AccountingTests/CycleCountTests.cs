using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Inventory;
using One4All.Core.Inventory;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// A cycle count takes only the items due a count - never-counted first, A items more often than C - and posts like a stocktake;
/// the next cycle count judges each item by its last posted count.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class CycleCountTests
{
    private readonly AccountingFixture _fixture;

    public CycleCountTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_cycle_count_takes_the_items_due_and_the_next_one_follows_the_last_counts()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long warehouseId;
        long fastId;
        long slowId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Item fast = new(companyId, "FAST", "Fast mover") { CostOfSalesAccountCode = "5000", AbcClass = "A" };
            Item slow = new(companyId, "SLOW", "Slow mover") { CostOfSalesAccountCode = "5000", AbcClass = "C" };
            db.Items.AddRange(fast, slow);
            Warehouse wh = new(companyId, "CYC", "Cycle store");
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();
            (fastId, slowId, warehouseId) = (fast.Id, slow.Id, wh.Id);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = fastId, WarehouseId = warehouseId, Date = new DateOnly(2026, 1, 2), Quantity = 40m, UnitCost = 3m, CreditAccountCode = "1100" });
            await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = slowId, WarehouseId = warehouseId, Date = new DateOnly(2026, 1, 2), Quantity = 10m, UnitCost = 5m, CreditAccountCode = "1100" });
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IStocktakeService stocktake = scope.ServiceProvider.GetRequiredService<IStocktakeService>();

            // Nothing counted yet: both are due; count and post them on 31 January.
            long first = (await stocktake.StartCycleCountAsync(companyId, warehouseId, new DateOnly(2026, 1, 31), "CC-1", new CycleCountRule())).Value;
            (await stocktake.StartCycleCountAsync(companyId, warehouseId, new DateOnly(2026, 1, 31), "CC-X", new CycleCountRule())).Error.Code.Should().Be("Stocktake.DraftOpen");
            (await stocktake.CountSheetAsync(companyId, first)).Value.Select(l => l.ItemCode).Should().BeEquivalentTo(["FAST", "SLOW"]);
            (await stocktake.RecordCountAsync(companyId, first, fastId, 39m)).IsSuccess.Should().BeTrue();
            (await stocktake.RecordCountAsync(companyId, first, slowId, 10m)).IsSuccess.Should().BeTrue();
            (await stocktake.PostAsync(companyId, first, new DateOnly(2026, 1, 31), "5300")).IsSuccess.Should().BeTrue();

            // Ten days on, nothing is due.
            (await stocktake.StartCycleCountAsync(companyId, warehouseId, new DateOnly(2026, 2, 10), "CC-2", new CycleCountRule())).Error.Code.Should().Be("Stocktake.NothingDue");

            // Thirty days on the A item is due again, the C item (every 180 days) is not.
            long second = (await stocktake.StartCycleCountAsync(companyId, warehouseId, new DateOnly(2026, 3, 2), "CC-3", new CycleCountRule())).Value;
            (await stocktake.CountSheetAsync(companyId, second)).Value.Select(l => l.ItemCode).Should().Equal("FAST");
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            StockBalance fastBalance = await db.Set<StockBalance>().SingleAsync(b => b.ItemId == fastId && b.WarehouseId == warehouseId);
            fastBalance.QuantityOnHand.Should().Be(39m);
        }
    }
}
