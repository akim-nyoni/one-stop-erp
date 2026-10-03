using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
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
/// Covers how a posting finds its exchange rate (brief §3.3): a currency quoted only the other way round (USD→ZAR) is
/// converted with the inverse quote; one quoted only against a third currency (GBP→EUR, with EUR→USD) is triangulated
/// through it; and a currency with no route at all is refused.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ExchangeRateRouteTests
{
    private static readonly CurrencyCode Zar = CurrencyCode.Parse("ZAR");
    private static readonly CurrencyCode Gbp = CurrencyCode.Parse("GBP");
    private static readonly CurrencyCode Jpy = CurrencyCode.Parse("JPY");

    private readonly AccountingFixture _fixture;

    public ExchangeRateRouteTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Rates_are_found_inverse_or_triangulated()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long customerId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            foreach ((CurrencyCode code, string name) in new[] { (Zar, "Rand"), (Gbp, "Pound"), (Jpy, "Yen") })
            {
                if (!await db.Currencies.AnyAsync(c => c.Code == code))
                {
                    db.Currencies.Add(new Currency(code, name, code.Value));
                }
            }

            // USD→ZAR is published (the inverse of what a ZAR posting needs); GBP is only quoted against EUR.
            if (!await db.ExchangeRates.AnyAsync(r => r.FromCurrency == Scenario.Usd && r.ToCurrency == Zar))
            {
                db.ExchangeRates.Add(new ExchangeRate(Scenario.Usd, Zar, ExchangeRateType.Daily, new DateOnly(2026, 1, 1), 18m));
                db.ExchangeRates.Add(new ExchangeRate(Gbp, Scenario.Eur, ExchangeRateType.Daily, new DateOnly(2026, 1, 1), 1.15m));
            }

            Customer customer = new(companyId, "XR-GBP", "Sterling Customer", Gbp);
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            customerId = customer.Id;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();

            // ZAR 1,800 at 1/18 → USD 100.
            Result<PostingReceipt> zar = await engine.PostAsync(Journal(companyId, Zar, 1_800m));
            zar.IsSuccess.Should().BeTrue(zar.IsFailure ? zar.Error.ToString() : null);
            zar.Value.TotalBase.Should().Be(100m);

            // GBP 100 at 1.15 × 1.10 → USD 126.50, both through the engine and through the sales ledger.
            Result<PostingReceipt> gbp = await engine.PostAsync(Journal(companyId, Gbp, 100m));
            gbp.IsSuccess.Should().BeTrue(gbp.IsFailure ? gbp.Error.ToString() : null);
            gbp.Value.TotalBase.Should().Be(126.50m);

            Result<TradingPostingResult> invoice = await scope.ServiceProvider.GetRequiredService<ISalesLedgerService>().PostInvoiceAsync(new SalesInvoiceRequest
            {
                CompanyId = companyId, CustomerId = customerId, DocumentNumber = "XR-I1", DocumentDate = new DateOnly(2026, 3, 10), Currency = Gbp, NetAmount = 200m, SalesAccountCode = "4000",
            });
            invoice.IsSuccess.Should().BeTrue(invoice.IsFailure ? invoice.Error.ToString() : null);
            invoice.Value.BaseAmount.Should().Be(253m);

            // No quote leads to JPY at all.
            (await engine.PostAsync(Journal(companyId, Jpy, 1_000m))).Error.Code.Should().Be("Posting.NoExchangeRate");
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ConsolidatedTrialBalance tb = (await scope.ServiceProvider.GetRequiredService<IConsolidationService>().ConsolidatedTrialBalanceAsync([companyId], new DateOnly(2026, 3, 31))).Value;
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Single(r => r.AccountCode == "1000").Debit.Should().Be(226.50m);
            tb.Rows.Single(r => r.AccountCode == "1200").Debit.Should().Be(253m);
        }
    }

    private static JournalRequest Journal(long companyId, CurrencyCode currency, decimal amount) => new()
    {
        CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 3, 10), TransactionCurrency = currency, EnforceManualJournalRules = false,
        Lines =
        [
            new JournalLineRequest { AccountCode = "1000", Side = JournalSide.Debit, TransactionAmount = amount },
            new JournalLineRequest { AccountCode = "4000", Side = JournalSide.Credit, TransactionAmount = amount },
        ],
    };
}
