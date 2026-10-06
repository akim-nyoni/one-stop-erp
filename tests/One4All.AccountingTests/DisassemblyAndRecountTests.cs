using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Core.Inventory;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers disassembly (finished goods' cost split over the components recovered by their value) and blind stocktakes with recounts
/// (counts outside the tolerance recounted by someone else before posting).
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class DisassemblyAndRecountTests
{
    private readonly AccountingFixture _fixture;

    public DisassemblyAndRecountTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Kits_are_taken_apart_and_counts_outside_tolerance_are_recounted()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Item casing = new(companyId, "DR-CASE", "Case"), board = new(companyId, "DR-BOARD", "Board"), kit = new(companyId, "DR-KIT", "PC kit");
        Warehouse store = new(companyId, "DR-WH", "Store");
        db.AddRange(casing, board, kit, store);
        await db.SaveChangesAsync();
        BillOfMaterials bom = new(companyId, "kit-1", kit.Id, 1m);
        bom.AddLine(casing.Id, 1m);
        bom.AddLine(board.Id, 2m, scrapPercent: 5m);
        db.BillsOfMaterials.Add(bom);
        await db.SaveChangesAsync();

        IInventoryService inventory = scope.ServiceProvider.GetRequiredService<IInventoryService>();
        foreach ((Item item, decimal quantity, decimal cost) in new[] { (casing, 5m, 10m), (board, 10m, 30m), (kit, 3m, 80m) })
        {
            (await inventory.ReceiveAsync(new ReceiveStockRequest
            {
                CompanyId = companyId, ItemId = item.Id, WarehouseId = store.Id, Date = new DateOnly(2026, 5, 1), Quantity = quantity, UnitCost = cost, CreditAccountCode = "1100",
            })).IsSuccess.Should().BeTrue();
        }

        // Two kits (160) come apart into 2 cases and 4 boards (scrap does not come back), split 20 : 120 by their average cost.
        IAssemblyService assembly = scope.ServiceProvider.GetRequiredService<IAssemblyService>();
        DisassemblyRequest TakeApart(params ComponentUsage[] recovered) => new()
        {
            CompanyId = companyId, BillOfMaterialsId = bom.Id, WarehouseId = store.Id, Date = new DateOnly(2026, 5, 2), Quantity = 2m, Recovered = recovered, ClearingAccountCode = "2600",
        };
        (await assembly.DisassembleAsync(TakeApart(new ComponentUsage(casing.Id, 0m), new ComponentUsage(board.Id, 0m)))).Error.Code.Should().Be("Inv.NothingRecovered");
        AssemblyResult run = (await assembly.DisassembleAsync(TakeApart())).Value;
        run.TotalCost.Should().Be(160m);
        StockBalance Balance(Item item) => db.StockBalances.AsNoTracking().Single(b => b.ItemId == item.Id && b.WarehouseId == store.Id);
        (Balance(casing).QuantityOnHand, Balance(casing).TotalValue).Should().Be((7m, 72.86m));
        (Balance(board).QuantityOnHand, Balance(board).TotalValue).Should().Be((14m, 437.14m));
        (Balance(kit).QuantityOnHand, Balance(kit).TotalValue).Should().Be((1m, 80m));
        (await db.Journals.AsNoTracking().Where(j => j.CompanyId == companyId).SelectMany(j => j.Lines).Where(l => l.AccountCode == "2600").SumAsync(l => l.BaseDebit - l.BaseCredit)).Should().Be(0m);
        db.AssemblyRuns.AsNoTracking().Include(r => r.Lines).Single(r => r.Id == run.RunId).IsDisassembly.Should().BeTrue();

        // A blind count with a 5% tolerance: the boards are 4 short, so someone else recounts them.
        IStocktakeService stocktakes = scope.ServiceProvider.GetRequiredService<IStocktakeService>();
        long count = (await stocktakes.StartAsync(companyId, store.Id, new DateOnly(2026, 5, 3), "DR-ST1", recountTolerancePercent: 5m)).Value;
        (await stocktakes.RecordCountAsync(companyId, count, casing.Id, 7m, countedByUserId: 1)).IsSuccess.Should().BeTrue();
        (await stocktakes.RecordCountAsync(companyId, count, board.Id, 10m, countedByUserId: 1)).IsSuccess.Should().BeTrue();
        (await stocktakes.CountSheetAsync(companyId, count)).Value.Should().ContainSingle(l => l.RecountDue).Which.ItemCode.Should().Be("DR-BOARD");
        (await stocktakes.PostAsync(companyId, count, new DateOnly(2026, 5, 3), "6900")).Error.Code.Should().Be("Stocktake.RecountsDue");
        (await stocktakes.RecordRecountAsync(companyId, count, board.Id, 13m, 1)).Error.Code.Should().Be("Stocktake.SameCounter");
        (await stocktakes.RecordRecountAsync(companyId, count, casing.Id, 7m, 2)).Error.Code.Should().Be("Stocktake.NoRecountDue");
        (await stocktakes.RecordRecountAsync(companyId, count, board.Id, 13m, 2)).IsSuccess.Should().BeTrue();
        (await stocktakes.RecordCountAsync(companyId, count, board.Id, 10m, countedByUserId: 1)).Error.Code.Should().Be("Stocktake.AlreadyRecounted");
        StocktakePostResult posted = (await stocktakes.PostAsync(companyId, count, new DateOnly(2026, 5, 3), "6900")).Value;
        posted.AdjustmentsPosted.Should().Be(1);
        Balance(board).QuantityOnHand.Should().Be(13m);
    }
}
