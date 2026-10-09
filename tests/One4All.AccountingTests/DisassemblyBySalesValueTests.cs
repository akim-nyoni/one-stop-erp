using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Core.Inventory;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers disassembly costed by relative sales value: a carcass's cost lands on its cuts in proportion to what they sell for.</summary>
[Collection(AccountingCollection.Name)]
public sealed class DisassemblyBySalesValueTests
{
    private readonly AccountingFixture _fixture;

    public DisassemblyBySalesValueTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Joint_products_take_the_cost_in_proportion_to_their_selling_prices()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Item fillet = new(companyId, "DS-FIL", "Fillet"), mince = new(companyId, "DS-MIN", "Mince"), carcass = new(companyId, "DS-CAR", "Carcass");
        Warehouse butchery = new(companyId, "DS-WH", "Butchery");
        db.AddRange(fillet, mince, carcass, butchery);
        await db.SaveChangesAsync();
        BillOfMaterials cuts = new(companyId, "carcass-1", carcass.Id, 1m);
        cuts.AddLine(fillet.Id, 2m);
        cuts.AddLine(mince.Id, 8m);
        db.BillsOfMaterials.Add(cuts);
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<IInventoryService>().ReceiveAsync(new ReceiveStockRequest
        {
            CompanyId = companyId, ItemId = carcass.Id, WarehouseId = butchery.Id, Date = new DateOnly(2026, 5, 1), Quantity = 1m, UnitCost = 100m, CreditAccountCode = "1100",
        })).IsSuccess.Should().BeTrue();

        IAssemblyService assembly = scope.ServiceProvider.GetRequiredService<IAssemblyService>();
        DisassemblyRequest TakeApart() => new()
        {
            CompanyId = companyId, BillOfMaterialsId = cuts.Id, WarehouseId = butchery.Id, Date = new DateOnly(2026, 5, 2), Quantity = 1m, ClearingAccountCode = "2600", BySalesValue = true,
        };
        (await assembly.DisassembleAsync(TakeApart())).Error.Code.Should().Be("Inv.NoDefaultPriceList");
        IPricingService pricing = scope.ServiceProvider.GetRequiredService<IPricingService>();
        long retail = (await pricing.CreatePriceListAsync(companyId, "DS-RET", "Retail", Scenario.Usd, isDefault: true)).Value;
        (await assembly.DisassembleAsync(TakeApart())).Error.Code.Should().Be("Inv.NoSalesValue");

        // Fillet sells at 30 a kilo, mince at 5: sales value 60 : 40, so the 100 cost splits 60 : 40.
        (await pricing.SetPriceAsync(companyId, retail, fillet.Id, 1m, 30m)).IsSuccess.Should().BeTrue();
        (await pricing.SetPriceAsync(companyId, retail, mince.Id, 1m, 5m)).IsSuccess.Should().BeTrue();
        (await assembly.DisassembleAsync(TakeApart())).Value.TotalCost.Should().Be(100m);
        StockBalance Balance(Item item) => db.StockBalances.AsNoTracking().Single(b => b.ItemId == item.Id && b.WarehouseId == butchery.Id);
        (Balance(fillet).QuantityOnHand, Balance(fillet).TotalValue).Should().Be((2m, 60m));
        (Balance(mince).QuantityOnHand, Balance(mince).TotalValue).Should().Be((8m, 40m));
    }
}
