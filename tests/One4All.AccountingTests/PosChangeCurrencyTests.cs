using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Pos;
using One4All.Application.Pos;
using One4All.Core.Accounting;
using One4All.Core.Inventory;
using One4All.Core.Pos;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers change in a configured change currency: the till quotes the change at the day's rate rounded to the currency's smallest note or coin,
/// the sale posts with the rounding difference to the rounding account, and change in any other currency is refused.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PosChangeCurrencyTests
{
    private static readonly CurrencyCode Kwacha = CurrencyCode.Parse("ZMW");

    private readonly AccountingFixture _fixture;

    public PosChangeCurrencyTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Change_is_given_in_the_change_currency_rounded_to_its_smallest_unit()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        if (!await db.Currencies.AnyAsync(c => c.Code == Kwacha))
        {
            Currency kwacha = new(Kwacha, "Zambian Kwacha", "K", 2, 1m);
            kwacha.AddDenomination(1m, false);
            kwacha.AddDenomination(5m, true);
            db.Currencies.Add(kwacha);
        }

        if (!await db.ExchangeRates.AnyAsync(r => r.FromCurrency == Kwacha && r.ToCurrency == Scenario.Usd && r.RateType == ExchangeRateType.Daily))
        {
            db.ExchangeRates.Add(new ExchangeRate(Kwacha, Scenario.Usd, ExchangeRateType.Daily, new DateOnly(2026, 1, 1), 0.04m));
        }

        db.Accounts.Add(new LedgerAccount(companyId, "4952", "Cash Rounding", AccountType.Income));
        Branch branch = new(companyId, "CC", "Kiosk");
        Item bread = new(companyId, "CC-BREAD", "Bread") { RevenueAccountCode = "4000" };
        Warehouse shop = new(companyId, "CC-WH", "Shop");
        db.AddRange(branch, bread, shop);
        await db.SaveChangesAsync();
        PosTerminal terminal = new(companyId, branch.Id, "CCT1", "Till 1");
        db.PosTerminals.Add(terminal);
        await db.SaveChangesAsync();

        IPosService pos = scope.ServiceProvider.GetRequiredService<IPosService>();
        (await pos.ConfigureChangeAsync(companyId, "XXX", null, null)).Error.Code.Should().Be("Pos.ChangeCurrency");
        (await pos.ConfigureChangeAsync(companyId, "ZMW", null, null)).Error.Code.Should().Be("Pos.ChangeRounding");
        (await pos.ConfigureChangeAsync(companyId, "zmw", null, "4952")).IsSuccess.Should().BeTrue();

        // 9.98 paid with 20.00: 10.02 change is 250.5 kwacha, handed over as 251 (10.04), the 0.02 extra to rounding.
        DateOnly day = new(2026, 7, 1);
        PosChangeQuote quote = (await pos.ChangeQuoteAsync(companyId, 10.02m, day)).Value!;
        (quote.Currency, quote.Amount, quote.BaseValue, quote.Rounding).Should().Be((Kwacha, 251m, 10.04m, -0.02m));

        long session = (await pos.OpenTillAsync(companyId, terminal.Id, cashierUserId: 1, openingFloat: 100m)).Value;
        Task<Core.Common.Result<CompleteSaleResult>> Sell(string number, PosTenderRequest change) => pos.CompleteSaleAsync(new CompleteSaleRequest
        {
            CompanyId = companyId, TerminalId = terminal.Id, TillSessionId = session, CashierUserId = 1, SaleNumber = number, SaleDate = day, WarehouseId = shop.Id,
            TaxAccountCode = "2200", Lines = [new PosSaleLineRequest(bread.Id, "Bread", 2m, 4.99m, 9.98m, 0m, "4000")],
            Tenders = [new PosTenderRequest(TenderType.Cash, Scenario.Usd, 20m, "1000", false), change],
        });

        (await Sell("CC-1", new PosTenderRequest(TenderType.Cash, Scenario.Eur, 9.11m, "1000", true, ExchangeRate: 1.10m))).Error.Code.Should().Be("Pos.ChangeCurrency");
        (await Sell("CC-1", new PosTenderRequest(TenderType.Cash, Kwacha, 251m, "1000", true, ExchangeRate: 0.04m))).IsSuccess.Should().BeTrue();
        Sale sale = await db.Sales.AsNoTracking().Include(s => s.Tenders).SingleAsync(s => s.CompanyId == companyId && s.SaleNumber == "CC-1");
        sale.CashRounding.Should().Be(-0.02m);
        sale.Tenders.Single(t => t.IsChange).BaseAmount.Should().Be(10.04m);

        // Two notes' worth too far off is not rounding.
        (await Sell("CC-2", new PosTenderRequest(TenderType.Cash, Kwacha, 253m, "1000", true, ExchangeRate: 0.04m))).Error.Code.Should().Be("Pos.Unbalanced");
    }
}
