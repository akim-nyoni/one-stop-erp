using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Pos;
using One4All.Application.Inventory;
using One4All.Core.Accounting;
using One4All.Core.Inventory;
using One4All.Core.Pos;
using One4All.Core.System;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers cost-plus price lists (markup on average cost, margin on standard cost, list prices still winning) and the till's item limits
/// (age check, most per sale across lines, prescription-only).
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class CostPlusAndSaleLimitTests
{
    private readonly AccountingFixture _fixture;

    public CostPlusAndSaleLimitTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Lists_price_from_cost_when_they_have_no_price()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Item stocked = new(companyId, "CP-STK", "Stocked"), standard = new(companyId, "CP-STD", "Standard costed") { StandardCost = 8m }, listed = new(companyId, "CP-LST", "Listed");
        Warehouse store = new(companyId, "CP-WH", "Store");
        Customer trade = new(companyId, "CP-TRADE", "Trade buyer", Scenario.Usd);
        db.AddRange(stocked, standard, listed, store, trade);
        await db.SaveChangesAsync();
        IInventoryService inventory = scope.ServiceProvider.GetRequiredService<IInventoryService>();
        foreach (Item item in new[] { stocked, listed })
        {
            (await inventory.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = item.Id, WarehouseId = store.Id, Date = new DateOnly(2026, 6, 1), Quantity = 10m, UnitCost = 10m, CreditAccountCode = "1100" }))
                .IsSuccess.Should().BeTrue();
        }

        IPricingService pricing = scope.ServiceProvider.GetRequiredService<IPricingService>();
        long retail = (await pricing.CreatePriceListAsync(companyId, "CP-RET", "Retail", Scenario.Usd, isDefault: true)).Value;
        long tradeList = (await pricing.CreatePriceListAsync(companyId, "CP-TRD", "Trade", Scenario.Usd, isDefault: false)).Value;
        long foreign = (await pricing.CreatePriceListAsync(companyId, "CP-EUR", "Euro", CurrencyCode.Parse("EUR"), isDefault: false)).Value;
        (await pricing.SetPriceAsync(companyId, retail, listed.Id, 1m, 15m)).IsSuccess.Should().BeTrue();
        (await pricing.SetCostPlusAsync(companyId, retail, CostPlusBasis.Markup, 25m, 0.05m, PriceRounding.Nearest)).IsSuccess.Should().BeTrue();
        (await pricing.SetCostPlusAsync(companyId, tradeList, CostPlusBasis.Margin, 100m, 0m, PriceRounding.Nearest)).Error.Code.Should().Be("Price.CostPlusInvalid");
        (await pricing.SetCostPlusAsync(companyId, tradeList, CostPlusBasis.Margin, 20m, 0m, PriceRounding.Nearest)).IsSuccess.Should().BeTrue();
        (await pricing.SetCostPlusAsync(companyId, foreign, CostPlusBasis.Markup, 10m, 0m, PriceRounding.Nearest)).Error.Code.Should().Be("Price.CostPlusCurrency");
        (await pricing.AssignCustomerAsync(companyId, trade.Id, tradeList)).IsSuccess.Should().BeTrue();

        ResolvedPrice stockedRetail = (await pricing.PriceAsync(companyId, null, stocked.Id, 1m)).Value;
        (stockedRetail.UnitPrice, stockedRetail.FromCost, stockedRetail.PriceListCode).Should().Be((12.5m, true, "CP-RET"));
        (await pricing.PriceAsync(companyId, null, listed.Id, 1m)).Value.Should().Match<ResolvedPrice>(p => p.UnitPrice == 15m && !p.FromCost);
        (await pricing.PriceAsync(companyId, trade.Id, standard.Id, 1m)).Value.Should().Match<ResolvedPrice>(p => p.UnitPrice == 10m && p.FromCost && p.FromCustomerList);
        (await pricing.SetCostPlusAsync(companyId, retail, CostPlusBasis.None, 0m, 0m, PriceRounding.Nearest)).IsSuccess.Should().BeTrue();
        (await pricing.PriceAsync(companyId, null, stocked.Id, 1m)).Error.Code.Should().Be("Price.NoPrice");
    }

    [SkippableFact]
    public async Task Till_enforces_age_checks_quantity_limits_and_prescriptions()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Branch branch = new(companyId, "SL", "Shop");
        db.Branches.Add(branch);
        await db.SaveChangesAsync();
        PosTerminal terminal = new(companyId, branch.Id, "SLTILL", "Till");
        Item beer = new(companyId, "SL-BEER", "Lager") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
        Item pills = new(companyId, "SL-RX", "Antibiotic") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
        Warehouse shop = new(companyId, "SL-WH", "Shop floor");
        db.AddRange(terminal, beer, pills, shop);
        await db.SaveChangesAsync();
        IItemCatalogueService catalogue = scope.ServiceProvider.GetRequiredService<IItemCatalogueService>();
        (await catalogue.SetSaleLimitsAsync(companyId, beer.Id, 18, 6m, false)).IsSuccess.Should().BeTrue();
        (await catalogue.SetSaleLimitsAsync(companyId, pills.Id, null, null, true)).IsSuccess.Should().BeTrue();
        (await catalogue.SetSaleLimitsAsync(companyId, pills.Id, 0, null, true)).Error.Code.Should().Be("Inv.SaleLimitsInvalid");
        IInventoryService inventory = scope.ServiceProvider.GetRequiredService<IInventoryService>();
        foreach (Item item in new[] { beer, pills })
        {
            (await inventory.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = item.Id, WarehouseId = shop.Id, Date = new DateOnly(2026, 6, 1), Quantity = 50m, UnitCost = 1m, CreditAccountCode = "1100" }))
                .IsSuccess.Should().BeTrue();
        }

        IPosService pos = scope.ServiceProvider.GetRequiredService<IPosService>();
        long session = (await pos.OpenTillAsync(companyId, terminal.Id, cashierUserId: 1, openingFloat: 0m)).Value;
        int number = 0;
        CompleteSaleRequest Sale(bool ageVerified, string? prescription, params (Item Item, decimal Quantity)[] lines) => new()
        {
            CompanyId = companyId, TerminalId = terminal.Id, TillSessionId = session, CashierUserId = 1, SaleNumber = $"SL-{++number}", SaleDate = new DateOnly(2026, 6, 2), WarehouseId = shop.Id,
            AgeVerified = ageVerified, PrescriptionReference = prescription,
            Lines = [.. lines.Select(l => new PosSaleLineRequest(l.Item.Id, l.Item.Description, l.Quantity, 2m, 2m * l.Quantity, 0m, "4000"))],
            Tenders = [new PosTenderRequest(TenderType.Cash, Scenario.Usd, lines.Sum(l => 2m * l.Quantity), "1000", IsChange: false)],
        };

        (await pos.CompleteSaleAsync(Sale(false, null, (beer, 2m)))).Error.Code.Should().Be("Pos.AgeCheck");
        (await pos.CompleteSaleAsync(Sale(true, null, (beer, 4m), (beer, 3m)))).Error.Code.Should().Be("Pos.MaxQuantity");
        (await pos.CompleteSaleAsync(Sale(true, null, (beer, 6m), (pills, 1m)))).Error.Code.Should().Be("Pos.PrescriptionRequired");
        CompleteSaleResult sold = (await pos.CompleteSaleAsync(Sale(true, "RX-1001", (beer, 6m), (pills, 1m)))).Value;
        Sale recorded = await db.Sales.AsNoTracking().SingleAsync(s => s.Id == sold.SaleId);
        (recorded.AgeVerified, recorded.PrescriptionReference).Should().Be((true, "RX-1001"));
    }
}
