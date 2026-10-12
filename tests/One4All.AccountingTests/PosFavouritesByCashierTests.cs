using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Pos;
using One4All.Core.Inventory;
using One4All.Core.Pos;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers favourites per cashier and per till, on pages and in colours.</summary>
[Collection(AccountingCollection.Name)]
public sealed class PosFavouritesByCashierTests
{
    private readonly AccountingFixture _fixture;

    public PosFavouritesByCashierTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_cashier_sees_their_own_favourites_first()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Item lager = new(companyId, "PFC-LAGER", "Lager") { RevenueAccountCode = "4000" }, cola = new(companyId, "PFC-COLA", "Cola") { RevenueAccountCode = "4000" },
            chips = new(companyId, "PFC-CHIPS", "Chips") { RevenueAccountCode = "4000" };
        PosTerminal bar = new(companyId, 0, $"PFC{companyId % 10000}", "Bar till") { Profile = TerminalProfile.Bar };
        db.AddRange(lager, cola, chips, bar);
        await db.SaveChangesAsync();
        PriceList list = new(companyId, $"PFC{companyId % 100000}", "Default", Scenario.Usd) { IsDefault = true };
        list.SetPrice(lager.Id, 1m, 2m);
        list.SetPrice(cola.Id, 1m, 1m);
        list.SetPrice(chips.Id, 1m, 1.5m);
        db.PriceLists.Add(list);
        await db.SaveChangesAsync();

        IPosCatalogueService catalogue = scope.ServiceProvider.GetRequiredService<IPosCatalogueService>();
        const long cashier = 990777;
        (await catalogue.SetFavouriteAsync(companyId, null, null, "PFC-COLA", 10, 1, null, false)).IsSuccess.Should().BeTrue();
        (await catalogue.SetFavouriteAsync(companyId, bar.Id, null, "PFC-CHIPS", 10, 2, "#AA3300", false)).IsSuccess.Should().BeTrue();
        (await catalogue.SetFavouriteAsync(companyId, bar.Id, cashier, "PFC-LAGER", 5, 1, "Gold", false)).IsSuccess.Should().BeTrue();
        (await catalogue.SetFavouriteAsync(companyId, bar.Id, cashier, "PFC-COLA", 20, 1, "Red", false)).IsSuccess.Should().BeTrue();
        (await catalogue.SetFavouriteAsync(companyId, bar.Id, cashier, "PFC-LAGER", 5, 0, null, false)).Error.Code.Should().Be("Pos.FavouritePage");

        IReadOnlyList<FavouriteTile> mine = await catalogue.FavouriteTilesAsync(companyId, bar.Id, cashier);
        mine.Select(f => (f.Tile.Code, f.Colour, f.Page)).Should().Equal(("PFC-LAGER", "Gold", 1), ("PFC-COLA", "Red", 1), ("PFC-CHIPS", "#AA3300", 2));
        IReadOnlyList<FavouriteTile> someoneElse = await catalogue.FavouriteTilesAsync(companyId, bar.Id, 123);
        someoneElse.Select(f => (f.Tile.Code, f.Colour)).Should().Equal(("PFC-COLA", (string?)null), ("PFC-CHIPS", "#AA3300"));
        (await catalogue.FavouritesAsync(companyId, bar.Id)).Select(t => t.Code).Should().Contain(["PFC-COLA", "PFC-CHIPS"]).And.NotContain("PFC-LAGER");
    }
}
