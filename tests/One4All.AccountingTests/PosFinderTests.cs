using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Pos;
using One4All.Core.Inventory;
using One4All.Core.Pos;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers the till's product finder: category with sub-categories, brand, supplier, price band, in stock, on promotion and the choices offered.</summary>
[Collection(AccountingCollection.Name)]
public sealed class PosFinderTests
{
    private readonly AccountingFixture _fixture;

    public PosFinderTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task The_finder_narrows_the_items_by_every_filter()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.ItemCategories.AddRange(new ItemCategory(companyId, "PFBEV", "Beverages"), new ItemCategory(companyId, "PFSODA", "Sodas") { ParentCode = "PFBEV" });
        Item cola = new(companyId, "PF-COLA", "Cola 330ml") { Category = "PFSODA", Brand = "Fizz", RevenueAccountCode = "4000" };
        Item water = new(companyId, "PF-WATER", "Still water 500ml") { Category = "PFBEV", Brand = "Spring", RevenueAccountCode = "4000" };
        Item bread = new(companyId, "PF-BREAD", "Bread") { Category = "PFBAKE", Brand = "Fizz", RevenueAccountCode = "4000" };
        Supplier wholesaler = new(companyId, "PF-WHOLE", "Drinks Wholesale", Scenario.Usd);
        db.AddRange(cola, water, bread, wholesaler);
        await db.SaveChangesAsync();
        PriceList list = new(companyId, $"PF{companyId % 100000}", "Default", Scenario.Usd) { IsDefault = true };
        list.SetPrice(cola.Id, 1m, 1.20m);
        list.SetPrice(water.Id, 1m, 0.80m);
        list.SetPrice(bread.Id, 1m, 1.50m);
        db.PriceLists.Add(list);
        db.Add(new ItemSupplier(companyId, cola.Id, wholesaler.Id));
        Promotion promotion = new(companyId, "PF-PROMO", "Water week", PromotionKind.PercentOff, "4000") { Percent = 10m, StartsOn = new DateOnly(2026, 5, 1), EndsOn = new DateOnly(2026, 5, 31) };
        promotion.SetItems([(water.Id, 0)]);
        db.Promotions.Add(promotion);
        await db.SaveChangesAsync();

        IPosCatalogueService catalogue = scope.ServiceProvider.GetRequiredService<IPosCatalogueService>();
        DateOnly may = new(2026, 5, 10);
        async Task<string[]> Find(PosFinderFilter filter, DateOnly? on = null)
            => [.. (await catalogue.FindAsync(companyId, filter, on ?? may)).Where(t => t.Code.StartsWith("PF-", StringComparison.Ordinal)).Select(t => t.Code)];

        (await Find(new PosFinderFilter(Category: "PFBEV"))).Should().BeEquivalentTo(["PF-COLA", "PF-WATER"], "sodas sit under beverages");
        (await Find(new PosFinderFilter(Brand: "fizz"))).Should().BeEquivalentTo(["PF-COLA", "PF-BREAD"]);
        (await Find(new PosFinderFilter(SupplierCode: "PF-WHOLE"))).Should().Equal("PF-COLA");
        (await Find(new PosFinderFilter(Category: "PFBEV", MinPrice: 1m, MaxPrice: 2m))).Should().Equal("PF-COLA");
        (await Find(new PosFinderFilter(Category: "PFBEV", InStockOnly: true))).Should().BeEmpty("nothing is on hand");
        (await Find(new PosFinderFilter(PromotionOnly: true))).Should().Equal("PF-WATER");
        (await Find(new PosFinderFilter(PromotionOnly: true), new DateOnly(2026, 6, 10))).Should().BeEmpty("the promotion has ended");

        (IReadOnlyList<string> brands, IReadOnlyList<string> suppliers) = await catalogue.FinderChoicesAsync(companyId);
        brands.Should().Contain(["Fizz", "Spring"]);
        suppliers.Should().Contain("PF-WHOLE");
    }
}
