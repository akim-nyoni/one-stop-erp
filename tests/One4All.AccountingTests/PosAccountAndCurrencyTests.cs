using FluentAssertions;
using One4All.Core.Accounting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Pos;
using One4All.Core.Common;
using One4All.Core.Inventory;
using One4All.Core.Pos;
using One4All.Core.System;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the till's on-account sales and foreign-currency tenders: a sale charged to a customer raises an invoice on their account
/// (posted to the receivables control), a second one past the credit limit and one for a customer on hold are refused, voiding it
/// credits and settles the account; the currencies the till takes come with today's rate, and a sale paid in euros balances in base.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PosAccountAndCurrencyTests
{
    private readonly AccountingFixture _fixture;

    public PosAccountAndCurrencyTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Accounts_and_currencies_at_the_till()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Branch branch = new(companyId, "AC", "Shop");
        db.Branches.Add(branch);
        await db.SaveChangesAsync();
        PosTerminal terminal = new(companyId, branch.Id, "ACTILL", "Till");
        Warehouse shop = new(companyId, "AC-WH", "Shop floor");
        Item cement = new(companyId, "AC-CEM", "Cement") { RevenueAccountCode = "4000" };
        Customer builder = new(companyId, "AC-BLD", "Builder on account", Scenario.Usd) { CreditLimit = 100m };
        Customer held = new(companyId, "AC-HLD", "Held account", Scenario.Usd) { CreditLimit = 1_000m, OnHold = true };
        db.AddRange(terminal, shop, cement, builder, held);
        await db.SaveChangesAsync();

        IPosService pos = scope.ServiceProvider.GetRequiredService<IPosService>();
        long session = (await pos.OpenTillAsync(companyId, terminal.Id, cashierUserId: 1, openingFloat: 0m)).Value;
        DateOnly day = new(2026, 6, 2);
        Task<Result<CompleteSaleResult>> Sell(string number, long? customer, decimal amount, params PosTenderRequest[] tenders) => pos.CompleteSaleAsync(new CompleteSaleRequest
        {
            CompanyId = companyId, TerminalId = terminal.Id, TillSessionId = session, CashierUserId = 1, SaleNumber = number, SaleDate = day, WarehouseId = shop.Id, CustomerId = customer,
            Lines = [new PosSaleLineRequest(cement.Id, "Cement", 1m, amount, amount, 0m, "4000")], Tenders = tenders,
        });
        PosTenderRequest Account(decimal amount) => new(TenderType.Credit, Scenario.Usd, amount, string.Empty, IsChange: false);

        // On account: an invoice on the customer's account, on the sale's journal.
        CompleteSaleResult charged = (await Sell("AC-1", builder.Id, 60m, Account(60m))).Value;
        OpenItem invoice = await db.OpenItems.AsNoTracking().SingleAsync(i => i.CompanyId == companyId && i.DocumentNumber == "AC-1");
        (invoice.TraderId, invoice.ItemType, invoice.OutstandingBase, invoice.DueDate).Should().Be((builder.Id, OpenItemType.Invoice, 60m, day.AddDays(30)));
        (await db.Set<SaleTender>().AsNoTracking().SingleAsync(t => t.SaleId == charged.SaleId)).AccountCode.Should().Be("1200");

        (await Sell("AC-2", builder.Id, 60m, Account(60m))).Error.Code.Should().Be("Pos.CreditLimit");
        (await Sell("AC-3", held.Id, 10m, Account(10m))).Error.Code.Should().Be("Pos.CustomerOnHold");
        (await Sell("AC-4", null, 10m, Account(10m))).Error.Code.Should().Be("Pos.AccountCustomer");
        (await Sell("AC-5", builder.Id, 30m, Account(20m), new PosTenderRequest(TenderType.Cash, Scenario.Usd, 10m, "1000", IsChange: false))).IsSuccess.Should().BeTrue();
        (await db.OpenItems.AsNoTracking().SingleAsync(i => i.CompanyId == companyId && i.DocumentNumber == "AC-5")).OutstandingBase.Should().Be(20m);

        // Voiding credits the account and settles the invoice.
        (await pos.VoidSaleAsync(companyId, charged.SaleId, day, "Wrong customer")).IsSuccess.Should().BeTrue();
        List<OpenItem> account = await db.OpenItems.AsNoTracking().Where(i => i.CompanyId == companyId && i.TraderId == builder.Id).ToListAsync();
        account.Single(i => i.DocumentNumber == "AC-1").OutstandingBase.Should().Be(0m);
        account.Single(i => i.DocumentNumber == "VOID-AC-1").Should().Match<OpenItem>(i => i.ItemType == OpenItemType.CreditNote && i.OutstandingBase == 0m);
        account.Sum(i => i.Sign == LedgerSign.Debit ? i.OutstandingBase : -i.OutstandingBase).Should().Be(20m);

        // Currencies: base and euros at today's rate; a euro payment with change in base.
        IReadOnlyList<TillCurrency> currencies = await pos.TenderCurrenciesAsync(companyId, day);
        currencies[0].Should().Be(new TillCurrency(Scenario.Usd, 1m));
        currencies.Should().Contain(new TillCurrency(Scenario.Eur, 1.10m));
        CompleteSaleResult euros = (await Sell("AC-6", null, 23m, new PosTenderRequest(TenderType.Cash, Scenario.Eur, 20.91m, "1000", IsChange: false, ExchangeRate: 1.10m),
            new PosTenderRequest(TenderType.Cash, Scenario.Usd, 0.001m, "1000", IsChange: true))).Value;

        // The euros reach the ledger as euros (so revaluation can retranslate the drawer), at the till's rate; the journal balances in base.
        JournalEntry sold = await db.Journals.AsNoTracking().Include(j => j.Lines).SingleAsync(j => j.CompanyId == companyId && j.DocumentNumber == "AC-6" && j.DocumentType == "PosSale");
        JournalLine drawer = sold.Lines.Single(l => l.TransactionCurrency == Scenario.Eur);
        (drawer.AccountCode, drawer.TransactionDebit, drawer.ExchangeRate, drawer.BaseDebit).Should().Be(("1000", 20.91m, 1.10m, 23.001m));
        sold.IsBalancedInBase.Should().BeTrue();

        (await pos.VoidSaleAsync(companyId, euros.SaleId, day, "Rung up twice")).IsSuccess.Should().BeTrue();
        JournalEntry voided = await db.Journals.AsNoTracking().Include(j => j.Lines).Where(j => j.CompanyId == companyId && j.SourceModule == "POS").OrderByDescending(j => j.Id).FirstAsync();
        voided.Lines.Single(l => l.TransactionCurrency == Scenario.Eur).TransactionCredit.Should().Be(20.91m, "the void gives the euros back as euros");
    }
}
