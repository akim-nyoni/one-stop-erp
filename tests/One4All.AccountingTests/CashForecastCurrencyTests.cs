using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Banking;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Banking;
using One4All.Core.Accounting;
using One4All.Core.Banking;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>The cash forecast for one currency holds that currency's bank accounts and documents at their own amounts.</summary>
[Collection(AccountingCollection.Name)]
public sealed class CashForecastCurrencyTests
{
    private readonly AccountingFixture _fixture;

    public CashForecastCurrencyTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_currency_forecast_is_in_that_currency()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.Add(new LedgerAccount(companyId, "1120", "Euro bank", AccountType.Asset));
        db.BankAccounts.AddRange(new BankAccount(companyId, "CFC-USD", "Dollars", Scenario.Usd, "1100"), new BankAccount(companyId, "CFC-EUR", "Euros", Scenario.Eur, "1120"));
        Customer buyer = new(companyId, "CFC-CUS", "Euro buyer", Scenario.Eur);
        db.Customers.Add(buyer);
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<IPostingEngine>().PostAsync(new JournalRequest
        {
            CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 4, 1), TransactionCurrency = Scenario.Eur, Narrative = "Euro float",
            Lines =
            [
                new JournalLineRequest { AccountCode = "1120", Side = JournalSide.Debit, TransactionAmount = 500m, ExchangeRate = 1.10m },
                new JournalLineRequest { AccountCode = "4000", Side = JournalSide.Credit, TransactionAmount = 500m, ExchangeRate = 1.10m },
            ],
        })).IsSuccess.Should().BeTrue();
        (await scope.ServiceProvider.GetRequiredService<ISalesLedgerService>().PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = companyId, CustomerId = buyer.Id, DocumentNumber = "CFC-AR1", DocumentDate = new DateOnly(2026, 4, 2), Currency = Scenario.Eur, NetAmount = 200m, SalesAccountCode = "4000",
            ExchangeRate = 1.10m,
        })).IsSuccess.Should().BeTrue();

        ICashForecastService forecasts = scope.ServiceProvider.GetRequiredService<ICashForecastService>();
        CashForecast euros = await forecasts.ForecastInCurrencyAsync(companyId, "eur", new DateOnly(2026, 4, 6), 8, 0m);
        euros.OpeningBalance.Should().Be(500m, "the euro bank holds 500 euros");
        euros.Lines.Should().ContainSingle(l => l.Source == CashFlowSource.Receivables && l.Reference == "CFC-AR1" && l.Amount == 200m);
        (await forecasts.ForecastAsync(companyId, new DateOnly(2026, 4, 6), 8, 0m)).OpeningBalance.Should().Be(550m, "the full forecast is in base: 500 euros at 1.10");
    }
}
