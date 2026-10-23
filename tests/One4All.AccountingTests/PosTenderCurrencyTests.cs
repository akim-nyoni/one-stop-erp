using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Pos;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Inventory;
using One4All.Core.Pos;
using One4All.Core.System;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// The till offers every currency in the currency table, not only those with a daily rate: a currency with no rate at all is listed
/// without one (the till shows it greyed with the reason) and a sale tendered in it is refused with the reason; once a rate of any
/// type is captured (here a spot rate) the currency is taken at it.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PosTenderCurrencyTests
{
    private static readonly CurrencyCode Loti = CurrencyCode.Parse("LSL");
    private readonly AccountingFixture _fixture;

    public PosTenderCurrencyTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Every_currency_in_the_table_is_offered_and_any_captured_rate_type_values_it()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        if (!await db.Currencies.AnyAsync(c => c.Code == Loti))
        {
            db.Currencies.Add(new Currency(Loti, "Loti", "L"));
        }

        db.ExchangeRates.RemoveRange(db.ExchangeRates.Where(r => r.FromCurrency == Loti || r.ToCurrency == Loti));
        Branch branch = new(companyId, "TC", "Shop");
        db.Branches.Add(branch);
        await db.SaveChangesAsync();
        PosTerminal terminal = new(companyId, branch.Id, "TCTILL", "Till");
        Warehouse shop = new(companyId, "TC-WH", "Shop floor");
        Item cement = new(companyId, "TC-CEM", "Cement") { RevenueAccountCode = "4000" };
        db.AddRange(terminal, shop, cement);
        await db.SaveChangesAsync();

        IPosService pos = scope.ServiceProvider.GetRequiredService<IPosService>();
        long session = (await pos.OpenTillAsync(companyId, terminal.Id, cashierUserId: 900_020, openingFloat: 0m)).Value;
        DateOnly day = new(2026, 6, 2);
        Task<Result<CompleteSaleResult>> Sell(string number, decimal amount, params PosTenderRequest[] tenders) => pos.CompleteSaleAsync(new CompleteSaleRequest
        {
            CompanyId = companyId, TerminalId = terminal.Id, TillSessionId = session, CashierUserId = 900_020, SaleNumber = number, SaleDate = day, WarehouseId = shop.Id,
            Lines = [new PosSaleLineRequest(cement.Id, "Cement", 1m, amount, amount, 0m, "4000")], Tenders = tenders,
        });

        // No rate of any type: the currency is still listed, without a rate, and a sale in it is refused with the reason.
        IReadOnlyList<TillCurrency> currencies = await pos.TenderCurrenciesAsync(companyId, day);
        currencies[0].Should().Be(new TillCurrency(Scenario.Usd, 1m));
        TillCurrency loti = currencies.Single(c => c.Code == Loti);
        (loti.Rate, loti.HasRate).Should().Be((0m, false));
        currencies.Single(c => c.Code == Scenario.Eur).HasRate.Should().BeTrue();
        Result<CompleteSaleResult> refused = await Sell("TC-1", 10m, new PosTenderRequest(TenderType.Cash, Loti, 200m, "1000", IsChange: false));
        refused.Error.Code.Should().Be("Pos.NoRate");
        refused.Error.Message.Should().Contain("Exchange rates");

        // A spot rate (not a daily one) is enough: the currency comes with it and the sale is valued at it.
        db.ExchangeRates.Add(new ExchangeRate(Loti, Scenario.Usd, ExchangeRateType.Spot, new DateOnly(2026, 1, 1), 0.05m));
        await db.SaveChangesAsync();
        currencies = await pos.TenderCurrenciesAsync(companyId, day);
        currencies.Single(c => c.Code == Loti).Should().Be(new TillCurrency(Loti, 0.05m));
        CompleteSaleResult sold = (await Sell("TC-2", 10m, new PosTenderRequest(TenderType.Cash, Loti, 200m, "1000", IsChange: false))).Value;
        JournalEntry journal = await db.Journals.AsNoTracking().Include(j => j.Lines).SingleAsync(j => j.CompanyId == companyId && j.DocumentNumber == "TC-2" && j.DocumentType == "PosSale");
        JournalLine drawer = journal.Lines.Single(l => l.TransactionCurrency == Loti);
        (drawer.TransactionDebit, drawer.ExchangeRate, drawer.BaseDebit).Should().Be((200m, 0.05m, 10m));
        journal.IsBalancedInBase.Should().BeTrue();
        sold.SaleId.Should().BePositive();
    }
}
