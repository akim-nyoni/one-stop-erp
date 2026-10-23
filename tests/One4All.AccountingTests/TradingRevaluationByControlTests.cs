using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.System;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers revaluing receivables on the control account each item sits on: a customer on its own receivables control (an
/// instalment debtors control) and an ordinary customer on the main control, both invoiced in forints, are each revalued
/// against their own control, so both controls reconcile and the FX gain is the total of the two.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class TradingRevaluationByControlTests
{
    // Forints: used by no other test, so the global exchange rates below are this test's alone.
    private static readonly CurrencyCode Huf = CurrencyCode.Parse("HUF");

    private readonly AccountingFixture _fixture;

    public TradingRevaluationByControlTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Each_customer_is_revalued_on_its_own_control_account()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        if (!await db.Currencies.AnyAsync(c => c.Code == Huf))
        {
            db.Currencies.Add(new Currency(Huf, "Hungarian Forint", "Ft"));
        }

        if (!await db.ExchangeRates.AnyAsync(r => r.FromCurrency == Huf && r.ToCurrency == Scenario.Usd && r.RateType == ExchangeRateType.Daily))
        {
            db.ExchangeRates.Add(new ExchangeRate(Huf, Scenario.Usd, ExchangeRateType.Daily, new DateOnly(2026, 1, 1), 0.0025m));
        }

        if (!await db.ExchangeRates.AnyAsync(r => r.FromCurrency == Huf && r.ToCurrency == Scenario.Usd && r.RateType == ExchangeRateType.ClosingRate))
        {
            db.ExchangeRates.Add(new ExchangeRate(Huf, Scenario.Usd, ExchangeRateType.ClosingRate, new DateOnly(2026, 6, 30), 0.0030m));
        }

        LedgerAccount instalmentControl = new(companyId, "1270", "Instalment Debtors Control", AccountType.Asset);
        instalmentControl.MakeControlAccount(ControlAccountType.AccountsReceivable);
        db.Accounts.Add(instalmentControl);
        Customer buyer = new(companyId, "RC-BUY", "Instalment Buyer", Scenario.Usd), trader = new(companyId, "RC-TRD", "Ordinary Customer", Scenario.Usd);
        db.Customers.AddRange(buyer, trader);
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<IReceivableControlService>().SetCustomerControlAsync(companyId, "RC-BUY", "1270")).IsSuccess.Should().BeTrue();

        // Ft 100,000 to the instalment buyer (USD 250) and Ft 40,000 to the ordinary customer (USD 100), both at 0.0025.
        ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
        SalesInvoiceRequest Invoice(Customer c, string number, decimal net) => new()
        {
            CompanyId = companyId, CustomerId = c.Id, DocumentNumber = number, DocumentDate = new DateOnly(2026, 3, 1), Currency = Huf, NetAmount = net, SalesAccountCode = "4000",
        };
        (await ar.PostInvoiceAsync(Invoice(buyer, "RC-1", 100_000m))).IsSuccess.Should().BeTrue();
        (await ar.PostInvoiceAsync(Invoice(trader, "RC-2", 40_000m))).IsSuccess.Should().BeTrue();

        // At the 0.0030 closing rate: 300 on the instalment control (+50) and 120 on the main control (+20).
        DateOnly june30 = new(2026, 6, 30);
        Result<TradingRevaluationResult> run = await scope.ServiceProvider.GetRequiredService<ITradingRevaluationService>().RevalueAsync(companyId, TradingLedger.AccountsReceivable, june30);
        run.IsSuccess.Should().BeTrue(run.IsFailure ? run.Error.ToString() : null);
        run.Value.Posted.Should().Be(70m);
        JournalEntry journal = await db.Journals.AsNoTracking().Include(j => j.Lines).SingleAsync(j => j.CompanyId == companyId && j.JournalNumber == run.Value.JournalNumber);
        journal.Lines.Where(l => l.AccountCode == "1270").Sum(l => l.BaseDebit - l.BaseCredit).Should().Be(50m);
        journal.Lines.Where(l => l.AccountCode == "1200").Sum(l => l.BaseDebit - l.BaseCredit).Should().Be(20m);
        journal.Lines.Where(l => l.AccountCode == "7100").Sum(l => l.BaseCredit - l.BaseDebit).Should().Be(70m);

        // Both controls reconcile, each carrying its own revaluation.
        ControlReconciliationReport reconciliation = await scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>().ControlReconciliationAsync(companyId, june30);
        reconciliation.Rows.Single(r => r.AccountCode == "1270").Should().BeEquivalentTo(new { GlBalance = 300m, SubLedgerBalance = 250m, Revaluation = 50m, Difference = 0m });
        reconciliation.Rows.Single(r => r.AccountCode == "1200").Should().BeEquivalentTo(new { GlBalance = 120m, SubLedgerBalance = 100m, Revaluation = 20m, Difference = 0m });

        // A repeat run finds each control already carrying its share and posts nothing.
        TradingRevaluationResult again = (await scope.ServiceProvider.GetRequiredService<ITradingRevaluationService>().RevalueAsync(companyId, TradingLedger.AccountsReceivable, june30)).Value;
        (again.AlreadyBooked, again.JournalNumber).Should().Be((70m, (string?)null));
    }
}
