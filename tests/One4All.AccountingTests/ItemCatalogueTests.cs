using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Core.Inventory;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the item master: extra barcodes (unique across items, packs and aliases), synonyms, pack units with their own barcodes,
/// supplier links with one preferred source, search over all of it, scan / PLU look-up with the unit scanned, and ABC classes.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ItemCatalogueTests
{
    private readonly AccountingFixture _fixture;

    public ItemCatalogueTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Items_are_found_by_every_code_and_classified()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Item coke = new(companyId, "IC-COKE", "Coca Cola 330ml") { Barcode = "600100", Brand = "Coca-Cola", CostOfSalesAccountCode = "5000" };
        Item banana = new(companyId, "IC-BANANA", "Bananas per kg") { Plu = "4011", CostOfSalesAccountCode = "5000" };
        Item rice = new(companyId, "IC-RICE", "Rice 2kg") { CostOfSalesAccountCode = "5000" };
        Item salt = new(companyId, "IC-SALT", "Salt 1kg") { CostOfSalesAccountCode = "5000" };
        Supplier bottler = new(companyId, "IC-S1", "Bottler", Scenario.Usd), wholesaler = new(companyId, "IC-S2", "Wholesaler", Scenario.Usd);
        Warehouse store = new(companyId, "IC-WH", "Store");
        db.AddRange(coke, banana, rice, salt, bottler, wholesaler, store);
        await db.SaveChangesAsync();

        IItemCatalogueService catalogue = scope.ServiceProvider.GetRequiredService<IItemCatalogueService>();
        (await catalogue.AddAliasAsync(companyId, coke.Id, ItemAliasKind.Barcode, "600199")).IsSuccess.Should().BeTrue();
        (await catalogue.AddAliasAsync(companyId, rice.Id, ItemAliasKind.Barcode, "600100")).Error.Code.Should().Be("Inv.BarcodeInUse");
        (await catalogue.AddAliasAsync(companyId, coke.Id, ItemAliasKind.Synonym, "soda")).IsSuccess.Should().BeTrue();
        (await catalogue.AddUnitAsync(companyId, coke.Id, "case", 24m, "600124")).IsSuccess.Should().BeTrue();
        (await catalogue.AddUnitAsync(companyId, rice.Id, "BAG", 10m, "600199")).Error.Code.Should().Be("Inv.BarcodeInUse");
        (await catalogue.AddUnitAsync(companyId, coke.Id, "CASE", 12m, null)).Error.Code.Should().Be("Inv.UnitExists");

        // Search and scan.
        (await catalogue.SearchAsync(companyId, "soda")).Should().ContainSingle(h => h.Code == "IC-COKE" && h.MatchedOn == "synonym");
        (await catalogue.SearchAsync(companyId, "4011")).Should().ContainSingle(h => h.Code == "IC-BANANA" && h.MatchedOn == "exact");
        (await catalogue.SearchAsync(companyId, "coca")).Should().Contain(h => h.Code == "IC-COKE");
        (await catalogue.SearchAsync(companyId, "600124")).Should().ContainSingle(h => h.Code == "IC-COKE" && h.MatchedOn == "pack barcode");
        (await catalogue.LookupAsync(companyId, "600124")).Value.Should().Be(new ItemLookup(coke.Id, "IC-COKE", "CASE", 24m));
        (await catalogue.LookupAsync(companyId, "600199")).Value.Factor.Should().Be(1m);
        (await catalogue.LookupAsync(companyId, "4011")).Value.Code.Should().Be("IC-BANANA");
        (await catalogue.LookupAsync(companyId, "999")).Error.Code.Should().Be("Inv.NotFound");

        // One preferred supplier at a time.
        (await catalogue.LinkSupplierAsync(new ItemSupplierRequest(companyId, coke.Id, bottler.Id, "CC330", 7, 240m, 0.40m, true))).IsSuccess.Should().BeTrue();
        (await catalogue.LinkSupplierAsync(new ItemSupplierRequest(companyId, coke.Id, wholesaler.Id, "W-COKE", 2, 24m, 0.45m, true))).IsSuccess.Should().BeTrue();
        (await db.ItemSuppliers.AsNoTracking().Where(l => l.ItemId == coke.Id).OrderBy(l => l.Id).Select(l => l.IsPreferred).ToListAsync()).Should().Equal(false, true);

        // ABC: issues worth 800, 150 and 50; salt never moved.
        IInventoryService inventory = scope.ServiceProvider.GetRequiredService<IInventoryService>();
        foreach ((Item item, decimal issued) in new[] { (coke, 800m), (banana, 150m), (rice, 50m) })
        {
            (await inventory.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = item.Id, WarehouseId = store.Id, Date = new DateOnly(2026, 9, 1), Quantity = 100m, UnitCost = 10m, CreditAccountCode = "1100" }))
                .IsSuccess.Should().BeTrue();
            (await inventory.IssueAsync(new IssueStockRequest { CompanyId = companyId, ItemId = item.Id, WarehouseId = store.Id, Date = new DateOnly(2026, 9, 10), Quantity = issued / 10m })).IsSuccess.Should().BeTrue();
        }

        (await catalogue.ClassifyAbcAsync(companyId, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30))).Should().Be((1, 1, 2));
        (await db.Items.AsNoTracking().Where(i => i.CompanyId == companyId).OrderBy(i => i.Code).Select(i => i.Code + "=" + i.AbcClass).ToListAsync())
            .Should().Equal("IC-BANANA=B", "IC-COKE=A", "IC-RICE=C", "IC-SALT=C");
    }
}
