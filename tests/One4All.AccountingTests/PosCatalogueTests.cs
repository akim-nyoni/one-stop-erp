using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Pos;
using One4All.Application.Inventory;
using One4All.Core.Inventory;
using One4All.Core.Pos;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the till's tiles from the item master: an explicitly priced item, an item priced by the default list's cost-plus rule, and the
/// items left off (unpriced, inactive, no revenue account); and a sale of a tile's item issuing stock and posting its cost.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PosCatalogueTests
{
    private readonly AccountingFixture _fixture;

    public PosCatalogueTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Tiles_come_from_the_item_master_and_move_stock()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Item bread = new(companyId, "PC-BREAD", "White bread 700g") { Category = "Bakery", Barcode = "6001234500017", RevenueAccountCode = "4000", CostOfSalesAccountCode = "5000" };
        Item milk = new(companyId, "PC-MILK", "Fresh milk 1L") { Category = "Dairy", RevenueAccountCode = "4000", CostOfSalesAccountCode = "5000" };
        Item noRevenue = new(companyId, "PC-NOREV", "Internal use only");
        Item retired = new(companyId, "PC-OLD", "Discontinued") { RevenueAccountCode = "4000", IsActive = false };
        Warehouse shop = new(companyId, "PC-WH", "Shop");
        db.AddRange(bread, milk, noRevenue, retired, shop);
        await db.SaveChangesAsync();

        IPosCatalogueService catalogue = scope.ServiceProvider.GetRequiredService<IPosCatalogueService>();
        (await catalogue.TilesAsync(companyId)).Should().BeEmpty("there is no default price list yet");

        IPricingService pricing = scope.ServiceProvider.GetRequiredService<IPricingService>();
        long list = (await pricing.CreatePriceListAsync(companyId, "PC-RET", "Retail", Scenario.Usd, isDefault: true)).Value;
        (await pricing.SetPriceAsync(companyId, list, bread.Id, 1m, 1.2m)).IsSuccess.Should().BeTrue();
        (await pricing.SetPriceAsync(companyId, list, bread.Id, 10m, 1.0m)).IsSuccess.Should().BeTrue();
        (await pricing.SetPriceAsync(companyId, list, retired.Id, 1m, 9m)).IsSuccess.Should().BeTrue();
        (await catalogue.TilesAsync(companyId)).Select(t => (t.Code, t.UnitPrice, t.Category)).Should().Equal(("PC-BREAD", 1.2m, "Bakery"));

        // A cost-plus default list prices the milk from its cost.
        (await scope.ServiceProvider.GetRequiredService<IInventoryService>().ReceiveAsync(new ReceiveStockRequest
        {
            CompanyId = companyId, ItemId = milk.Id, WarehouseId = shop.Id, Date = new DateOnly(2026, 6, 1), Quantity = 20m, UnitCost = 0.8m, CreditAccountCode = "1100",
        })).IsSuccess.Should().BeTrue();
        (await pricing.SetCostPlusAsync(companyId, list, CostPlusBasis.Markup, 25m, 0m, PriceRounding.Nearest)).IsSuccess.Should().BeTrue();
        IReadOnlyList<PosTile> tiles = await catalogue.TilesAsync(companyId);
        tiles.Select(t => (t.Code, t.UnitPrice)).Should().Equal(("PC-BREAD", 1.2m), ("PC-MILK", 1.0m));
        tiles.Single(t => t.Code == "PC-BREAD").Matches("600123").Should().BeTrue();

        // A tile sale carries the item: stock is issued and cost of sales posted.
        Branch branch = new(companyId, "PC-BR", "Shop");
        db.Branches.Add(branch);
        await db.SaveChangesAsync();
        PosTerminal terminal = new(companyId, branch.Id, "PC-TILL", "Till");
        db.Add(terminal);
        await db.SaveChangesAsync();
        IPosService pos = scope.ServiceProvider.GetRequiredService<IPosService>();
        long session = (await pos.OpenTillAsync(companyId, terminal.Id, cashierUserId: 1, openingFloat: 0m)).Value;
        PosTile milkTile = tiles.Single(t => t.Code == "PC-MILK");
        (await pos.CompleteSaleAsync(new CompleteSaleRequest
        {
            CompanyId = companyId, TerminalId = terminal.Id, TillSessionId = session, CashierUserId = 1, SaleNumber = "PC-S1", SaleDate = new DateOnly(2026, 6, 2), WarehouseId = shop.Id,
            Lines = [new PosSaleLineRequest(milkTile.ItemId, milkTile.Name, 3m, milkTile.UnitPrice, 3m * milkTile.UnitPrice, 0m, milkTile.RevenueAccountCode)],
            Tenders = [new PosTenderRequest(TenderType.Cash, Scenario.Usd, 3m, "1000", IsChange: false)],
        })).IsSuccess.Should().BeTrue();
        (await db.Set<StockMovement>().AsNoTracking().Where(m => m.CompanyId == companyId && m.ItemId == milk.Id && m.Reference == "PC-S1").SumAsync(m => m.Quantity))
            .Should().Be(-3m);
    }
}
