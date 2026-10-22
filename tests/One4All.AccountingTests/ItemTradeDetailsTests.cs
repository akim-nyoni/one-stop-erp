using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.MasterData;
using One4All.Application.Api;
using One4All.Core.Accounting;
using One4All.Core.Inventory;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Thirteenth audit #23: an item's HS code, country of origin, brand and tax code are set from its details, the tax code checked against the item's kind.</summary>
[Collection(AccountingCollection.Name)]
public sealed class ItemTradeDetailsTests
{
    private readonly AccountingFixture _fixture;

    public ItemTradeDetailsTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Hs_code_origin_brand_and_tax_code_are_set_and_checked()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        (await scope.ServiceProvider.GetRequiredService<IMasterDataService>().CreateTaxCodeAsync(new NewTaxCode(companyId, "SVC15", "Services 15%", TaxType.Standard, 15m,
            new DateOnly(2026, 1, 1), "2300", "2300", TaxAppliesTo.Services))).IsSuccess.Should().BeTrue();
        db.Items.Add(new Item(companyId, "ITD-CEM", "Cement 50kg"));
        await db.SaveChangesAsync();
        IItemDetailsService details = scope.ServiceProvider.GetRequiredService<IItemDetailsService>();

        (await details.SetDetailsAsync(companyId, "ITD-CEM", new ItemDetails { HsCode = "25AB" })).Error.Code.Should().Be("Item.HsCode");
        (await details.SetDetailsAsync(companyId, "ITD-CEM", new ItemDetails { TaxCode = "svc15" })).Error.Code.Should().Be("Tax.WrongSupply");
        (await details.SetDetailsAsync(companyId, "ITD-CEM", new ItemDetails { TaxCode = "NONE" })).Error.Code.Should().Be("Item.TaxCode");
        (await details.SetDetailsAsync(companyId, "ITD-CEM", new ItemDetails { HsCode = "2523.29", CountryOfOrigin = "Zimbabwe", Brand = "PPC" })).IsSuccess.Should().BeTrue();
        Item saved = await db.Items.AsNoTracking().SingleAsync(i => i.CompanyId == companyId && i.Code == "ITD-CEM");
        (saved.HsCode, saved.CountryOfOrigin, saved.Brand, saved.TaxCode).Should().Be(("2523.29", "Zimbabwe", "PPC", (string?)null));

        (await details.SetDetailsAsync(companyId, "ITD-CEM", new ItemDetails { HsCode = " ", Brand = string.Empty })).IsSuccess.Should().BeTrue();
        saved = await db.Items.AsNoTracking().SingleAsync(i => i.CompanyId == companyId && i.Code == "ITD-CEM");
        (saved.HsCode, saved.CountryOfOrigin, saved.Brand).Should().Be(((string?)null, "Zimbabwe", (string?)null), "blank clears, null leaves as it is");
    }

    [SkippableFact]
    public async Task A_standard_cost_on_stock_on_hand_changes_only_by_revaluation()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        IMasterDataService master = scope.ServiceProvider.GetRequiredService<IMasterDataService>();
        Item std = new(companyId, "ITD-STD", "Standard bolt", ItemType.Stock, CostingMethod.Standard) { StandardCost = 2m, PriceVarianceAccountCode = "6000" };
        Warehouse store = new(companyId, "ITD-WH", "Store");
        db.AddRange(std, store);
        await db.SaveChangesAsync();
        ItemUpdate update = new("itd-std", "Standard bolt M8", null, null, 2.5m, "6000", null, null);

        (await master.UpdateItemAsync(companyId, update)).IsSuccess.Should().BeTrue("with nothing on hand there is nothing to revalue");
        (await db.Items.AsNoTracking().SingleAsync(i => i.Id == std.Id)).StandardCost.Should().Be(2.5m);

        StockBalance balance = new(companyId, std.Id, store.Id);
        balance.ReceiveAtValue(10m, 25m);
        db.Add(balance);
        await db.SaveChangesAsync();
        (await master.UpdateItemAsync(companyId, update with { StandardCost = 3m })).Error.Code.Should().Be("Master.StandardCostRevalue");
        (await master.UpdateItemAsync(companyId, update with { Description = "Standard bolt M8 zinc" })).IsSuccess.Should().BeTrue("the standard cost is unchanged");
        (await master.UpdateItemAsync(companyId, update with { Code = "NONE" })).Error.Code.Should().Be("Master.ItemNotFound");

        // The REST API's PUT /api/master/items/{id} maps its request through TradingApiMapping.ItemChange into the same update, so the rule holds there too.
        Item current = await db.Items.AsNoTracking().SingleAsync(i => i.Id == std.Id);
        (await master.UpdateItemAsync(companyId, TradingApiMapping.ItemChange(current, new ItemApi(current.Code, string.Empty, StandardCost: 3m)))).Error.Code
            .Should().Be("Master.StandardCostRevalue", "the API cannot set a standard cost on stock on hand directly either");
        (await master.UpdateItemAsync(companyId, TradingApiMapping.ItemChange(current, new ItemApi(current.Code, "Standard bolt M8 API")))).IsSuccess
            .Should().BeTrue("an API change that leaves the standard cost alone still applies");
        Item afterApi = await db.Items.AsNoTracking().SingleAsync(i => i.Id == std.Id);
        (afterApi.Description, afterApi.StandardCost, afterApi.PriceVarianceAccountCode).Should().Be(("Standard bolt M8 API", (decimal?)2.5m, (string?)"6000"));
    }
}
