using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Core.Inventory;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers catch weight: receipts are weighed (and may be bought by weight) within the tolerance of the nominal piece weight, the balance carries
/// pieces and weight, issues are costed by the weight that leaves, and the last pieces take whatever weight and value remain.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class CatchWeightTests
{
    private readonly AccountingFixture _fixture;

    public CatchWeightTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Catch_weight_items_are_weighed_in_and_costed_by_weight_out()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Item beef = new(companyId, "CW-BEEF", "Beef forequarter") { CostOfSalesAccountCode = "5000" };
        Warehouse cold = new(companyId, "CW-COLD", "Cold room");
        db.AddRange(beef, cold);
        await db.SaveChangesAsync();

        IInventoryService inventory = scope.ServiceProvider.GetRequiredService<IInventoryService>();
        (await inventory.SetCatchWeightAsync(companyId, beef.Id, null, 50m, null)).Error.Code.Should().Be("Inv.CatchWeight");
        (await inventory.SetCatchWeightAsync(companyId, beef.Id, "KG", 50m, 10m)).IsSuccess.Should().BeTrue();

        ReceiveStockRequest Receive(decimal pieces, decimal? weight) => new()
        {
            CompanyId = companyId, ItemId = beef.Id, WarehouseId = cold.Id, Date = new DateOnly(2026, 5, 4), Quantity = pieces, Weight = weight, CostPerWeightUnit = 6m,
            CreditAccountCode = "1100", Reference = "CW-IN",
        };
        (await inventory.ReceiveAsync(Receive(4m, null))).Error.Code.Should().Be("Inv.WeightRequired");
        (await inventory.ReceiveAsync(Receive(4m, 300m))).Error.Code.Should().Be("Inv.WeightOutOfTolerance");   // 75 kg a piece against 50
        (await inventory.ReceiveAsync(Receive(4m, 200m))).Value.Value.Should().Be(1_200m);                        // 200 kg at 6 a kg
        (await inventory.SetCatchWeightAsync(companyId, beef.Id, "t", null, null)).Error.Code.Should().Be("Inv.CatchWeightStock");

        IssueStockRequest Issue(decimal pieces, decimal? weight) => new()
        {
            CompanyId = companyId, ItemId = beef.Id, WarehouseId = cold.Id, Date = new DateOnly(2026, 5, 6), Quantity = pieces, Weight = weight, Reference = "CW-OUT",
        };
        (await inventory.IssueAsync(Issue(1m, null))).Error.Code.Should().Be("Inv.WeightRequired");
        (await inventory.IssueAsync(Issue(1m, 55m))).Value.Value.Should().Be(330m);                               // the heavier piece costs more
        StockBalance after = await db.StockBalances.AsNoTracking().SingleAsync(b => b.ItemId == beef.Id);
        (after.QuantityOnHand, after.WeightOnHand, after.TotalValue).Should().Be((3m, 145m, 870m));

        // The last three weigh 140 on the way out: they take the 870 and 145 kg left.
        (await inventory.IssueAsync(Issue(3m, 140m))).Value.Value.Should().Be(870m);
        StockBalance empty = await db.StockBalances.AsNoTracking().SingleAsync(b => b.ItemId == beef.Id);
        (empty.QuantityOnHand, empty.WeightOnHand, empty.TotalValue).Should().Be((0m, 0m, 0m));
        (await db.StockMovements.AsNoTracking().Where(m => m.ItemId == beef.Id).OrderBy(m => m.Id).Select(m => m.Weight).ToListAsync()).Should().Equal(200m, -55m, -145m);
    }
}
