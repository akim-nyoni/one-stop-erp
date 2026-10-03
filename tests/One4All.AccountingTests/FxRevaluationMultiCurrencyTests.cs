using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers GL revaluation across two foreign currencies in one run (EUR bank gaining, CHF bank losing): previously
/// refused as "mixed currencies" with no way to run per currency. Each currency gets its own journal and reversal.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class FxRevaluationMultiCurrencyTests
{
    private static readonly CurrencyCode Chf = CurrencyCode.Parse("CHF");

    private readonly AccountingFixture _fixture;

    public FxRevaluationMultiCurrencyTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Each_foreign_currency_is_revalued_in_its_own_journal()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);

            // CHF: booked at 1.05, closing 1.00 on 30 June.
            if (!await db.Currencies.AnyAsync(c => c.Code == Chf))
            {
                db.Currencies.Add(new Currency(Chf, "Swiss franc", "CHF"));
                db.ExchangeRates.Add(new ExchangeRate(Chf, Scenario.Usd, ExchangeRateType.Daily, new DateOnly(2026, 1, 1), 1.05m));
                db.ExchangeRates.Add(new ExchangeRate(Chf, Scenario.Usd, ExchangeRateType.ClosingRate, new DateOnly(2026, 6, 30), 1.00m));
            }

            db.Accounts.Add(new LedgerAccount(companyId, "1160", "Bank (CHF)", AccountType.Asset) { CurrencyRestriction = Chf });
            await db.SaveChangesAsync();

            IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
            (await engine.PostAsync(Deposit(companyId, "1150", Scenario.Eur, 100m))).IsSuccess.Should().BeTrue(); // USD 110
            (await engine.PostAsync(Deposit(companyId, "1160", Chf, 200m))).IsSuccess.Should().BeTrue();          // USD 210
        }

        DateOnly june30 = new(2026, 6, 30);
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            Result<FxRevaluationResult> run = await scope.ServiceProvider.GetRequiredService<IFxRevaluationService>().RevalueAsync(companyId, june30);
            run.IsSuccess.Should().BeTrue(run.IsFailure ? run.Error.ToString() : null);

            run.Value.Accounts.Should().BeEquivalentTo(
            [
                new { AccountCode = "1150", Currency = "EUR", BaseBefore = 110m, BaseAfter = 120m, Difference = 10m },
                new { AccountCode = "1160", Currency = "CHF", BaseBefore = 210m, BaseAfter = 200m, Difference = -10m },
            ]);
            run.Value.JournalNumbers.Should().HaveCount(2);
            run.Value.ReversalJournalNumbers.Should().HaveCount(2);
            run.Value.NetDifference.Should().Be(0m);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IConsolidationService consolidation = scope.ServiceProvider.GetRequiredService<IConsolidationService>();
            ConsolidatedTrialBalance atEnd = (await consolidation.ConsolidatedTrialBalanceAsync([companyId], june30)).Value;
            atEnd.IsBalanced.Should().BeTrue();
            atEnd.Rows.Single(r => r.AccountCode == "1150").Debit.Should().Be(120m);
            atEnd.Rows.Single(r => r.AccountCode == "1160").Debit.Should().Be(200m);
            atEnd.Rows.Single(r => r.AccountCode == "7100").Credit.Should().Be(10m);
            atEnd.Rows.Single(r => r.AccountCode == "7200").Debit.Should().Be(10m);

            // Reversed the next day: both banks back at their booked base.
            ConsolidatedTrialBalance nextDay = (await consolidation.ConsolidatedTrialBalanceAsync([companyId], june30.AddDays(1))).Value;
            nextDay.Rows.Single(r => r.AccountCode == "1150").Debit.Should().Be(110m);
            nextDay.Rows.Single(r => r.AccountCode == "1160").Debit.Should().Be(210m);
        }
    }

    private static JournalRequest Deposit(long companyId, string bank, CurrencyCode currency, decimal amount) => new()
    {
        CompanyId = companyId, SourceModule = "CB", PostingDate = new DateOnly(2026, 6, 1), TransactionCurrency = currency,
        Lines =
        [
            new JournalLineRequest { AccountCode = bank, Side = JournalSide.Debit, TransactionAmount = amount },
            new JournalLineRequest { AccountCode = "4000", Side = JournalSide.Credit, TransactionAmount = amount },
        ],
    };
}
