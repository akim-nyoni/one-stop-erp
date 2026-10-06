using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Core.Inventory;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers bins and warehouse-level replenishment: put-away limited to unbinned stock and bin capacity, moves and picks between bins, a
/// pick walk in bin order with the unbinned remainder last, and reorder levels set per warehouse with the preferred supplier suggested.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class BinTests
{
    private readonly AccountingFixture _fixture;

    public BinTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Stock_is_put_away_picked_in_walk_order_and_reordered_per_warehouse()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Item bolt = new(companyId, "BN-BOLT", "Bolt M10") { CostOfSalesAccountCode = "5000" };
        Item nut = new(companyId, "BN-NUT", "Nut M10") { CostOfSalesAccountCode = "5000" };
        Warehouse depot = new(companyId, "BN-WH", "Depot"), branch = new(companyId, "BN-WH2", "Branch");
        Supplier fasteners = new(companyId, "BN-SUP", "Fasteners Ltd", Scenario.Usd);
        db.AddRange(bolt, nut, depot, branch, fasteners);
        await db.SaveChangesAsync();
        IInventoryService inventory = scope.ServiceProvider.GetRequiredService<IInventoryService>();
        (await inventory.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = bolt.Id, WarehouseId = depot.Id, Date = new DateOnly(2026, 9, 1), Quantity = 100m, UnitCost = 0.2m, CreditAccountCode = "1100" }))
            .IsSuccess.Should().BeTrue();

        IBinService bins = scope.ServiceProvider.GetRequiredService<IBinService>();
        (await bins.AddBinAsync(companyId, depot.Id, "a-01", 1, 50m)).IsSuccess.Should().BeTrue();
        (await bins.AddBinAsync(companyId, depot.Id, "B-07", 7, null)).IsSuccess.Should().BeTrue();
        (await bins.AddBinAsync(companyId, depot.Id, "A-01", 2, null)).Error.Code.Should().Be("Inv.BinExists");

        (await bins.PutAwayAsync(companyId, depot.Id, "A-01", bolt.Id, 60m)).Error.Code.Should().Be("Inv.BinFull");
        (await bins.PutAwayAsync(companyId, depot.Id, "A-01", bolt.Id, 40m)).IsSuccess.Should().BeTrue();
        (await bins.PutAwayAsync(companyId, depot.Id, "B-07", bolt.Id, 70m)).Error.Code.Should().Be("Inv.NotEnoughUnbinned");
        (await bins.PutAwayAsync(companyId, depot.Id, "B-07", bolt.Id, 50m)).IsSuccess.Should().BeTrue();
        (await bins.MoveAsync(companyId, depot.Id, "B-07", "A-01", bolt.Id, 20m)).Error.Code.Should().Be("Inv.BinFull");
        (await bins.MoveAsync(companyId, depot.Id, "A-01", "B-07", bolt.Id, 15m)).IsSuccess.Should().BeTrue();
        (await bins.ContentsAsync(companyId, depot.Id)).Should().Equal(new BinContent("A-01", 1, "BN-BOLT", 25m), new BinContent("B-07", 7, "BN-BOLT", 65m));

        // Picking 95: A-01 first, then B-07, then the 5 not yet in a bin.
        (await bins.PickRouteAsync(companyId, depot.Id, [(bolt.Id, 95m)])).Should().Equal(
            new PickStop("A-01", 1, bolt.Id, 25m), new PickStop("B-07", 7, bolt.Id, 65m), new PickStop("UNBINNED", int.MaxValue, bolt.Id, 5m));
        (await bins.TakeAsync(companyId, depot.Id, "A-01", bolt.Id, 30m)).Error.Code.Should().Be("Inv.NotInBin");
        (await bins.TakeAsync(companyId, depot.Id, "A-01", bolt.Id, 25m)).IsSuccess.Should().BeTrue();

        // The branch reorders nuts at its own level, from the preferred supplier at its last cost.
        (await bins.SetWarehouseLevelAsync(companyId, nut.Id, branch.Id, 200m, 500m)).IsSuccess.Should().BeTrue();
        (await scope.ServiceProvider.GetRequiredService<IItemCatalogueService>().LinkSupplierAsync(new ItemSupplierRequest(companyId, nut.Id, fasteners.Id, "N10", 3, 100m, 0.05m, true)))
            .IsSuccess.Should().BeTrue();
        IReplenishmentService replenishment = scope.ServiceProvider.GetRequiredService<IReplenishmentService>();
        ReplenishmentSuggestion nuts = (await replenishment.SuggestAsync(companyId, branch.Id)).Should().ContainSingle().Subject;
        nuts.ItemId.Should().Be(nut.Id);
        nuts.ReorderLevel.Should().Be(200m);
        nuts.SuggestedQuantity.Should().Be(500m);
        nuts.LastSupplierId.Should().Be(fasteners.Id);
        nuts.LastUnitPrice.Should().Be(0.05m);
        (await replenishment.SuggestAsync(companyId, depot.Id)).Should().BeEmpty();
    }
}
