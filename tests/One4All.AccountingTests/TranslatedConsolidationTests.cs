using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers consolidation across base currencies (IAS 21 current-rate method): a USD parent and a EUR subsidiary are
/// presented in USD - the subsidiary's bank at the 1.20 closing rate, its capital and sales at the 1.10 rate of their
/// posting dates - with the 150 difference as the translation reserve; presented in EUR the parent is translated
/// through the inverse rates; and a date with no closing rate is refused.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class TranslatedConsolidationTests
{
    private readonly AccountingFixture _fixture;

    public TranslatedConsolidationTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_foreign_subsidiary_is_translated_with_a_translation_reserve()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long parent;
        long subsidiary;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            parent = await Scenario.SeedAsync(db);
            subsidiary = await Scenario.SeedAsync(db, baseCurrency: Scenario.Eur);

            IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
            (await engine.PostAsync(Journal(parent, Scenario.Usd, new DateOnly(2026, 3, 10), "1100", "4000", 2_000m))).IsSuccess.Should().BeTrue();
            (await engine.PostAsync(Journal(subsidiary, Scenario.Eur, new DateOnly(2026, 3, 10), "1100", "3200", 1_000m))).IsSuccess.Should().BeTrue(); // capital
            (await engine.PostAsync(Journal(subsidiary, Scenario.Eur, new DateOnly(2026, 4, 15), "1100", "4000", 500m))).IsSuccess.Should().BeTrue();   // sales
        }

        DateOnly june30 = new(2026, 6, 30);
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IConsolidationService consolidation = scope.ServiceProvider.GetRequiredService<IConsolidationService>();

            // The plain consolidation still refuses mixed base currencies.
            (await consolidation.ConsolidatedTrialBalanceAsync([parent, subsidiary], june30)).Error.Code.Should().Be("Consolidation.MixedCurrency");

            // In USD: bank 2,000 + EUR 1,500 × 1.20; capital EUR 1,000 × 1.10; sales 2,000 + EUR 500 × 1.10; reserve 150.
            Result<TranslatedTrialBalance> usd = await consolidation.TranslatedTrialBalanceAsync([parent, subsidiary], Scenario.Usd, june30);
            usd.IsSuccess.Should().BeTrue(usd.IsFailure ? usd.Error.ToString() : null);
            usd.Value.IsBalanced.Should().BeTrue();
            Row(usd.Value, "1100").Should().BeEquivalentTo(new { Debit = 3_800m, Credit = 0m });
            Row(usd.Value, "3200").Should().BeEquivalentTo(new { Debit = 0m, Credit = 1_100m });
            Row(usd.Value, "4000").Should().BeEquivalentTo(new { Debit = 0m, Credit = 2_550m });
            Row(usd.Value, TranslatedTrialBalance.TranslationReserveCode).Should().BeEquivalentTo(new { Debit = 0m, Credit = 150m });
            usd.Value.TranslationReserve.Should().Be(150m);
            usd.Value.Companies.Single(c => c.CompanyId == subsidiary).Should().BeEquivalentTo(new { BaseCurrency = "EUR", ClosingRate = 1.20m, TranslationDifference = 150m });
            usd.Value.Companies.Single(c => c.CompanyId == parent).Should().BeEquivalentTo(new { ClosingRate = 1m, TranslationDifference = 0m });

            // In EUR the parent is translated through the inverse quotations: bank 2,000 / 1.20, sales 2,000 / 1.10.
            TranslatedTrialBalance eur = (await consolidation.TranslatedTrialBalanceAsync([parent, subsidiary], Scenario.Eur, june30)).Value;
            eur.IsBalanced.Should().BeTrue();
            Row(eur, "1100").Debit.Should().Be(3_166.67m);
            Row(eur, "4000").Credit.Should().Be(2_318.18m);
            Row(eur, TranslatedTrialBalance.TranslationReserveCode).Debit.Should().Be(151.51m);

            // At the end of March no closing rate is in force.
            (await consolidation.TranslatedTrialBalanceAsync([parent, subsidiary], Scenario.Usd, new DateOnly(2026, 3, 31))).Error.Code.Should().Be("Consolidation.NoRate");
        }
    }

    private static ConsolidatedTrialBalanceRow Row(TranslatedTrialBalance tb, string code) => tb.Rows.Single(r => r.AccountCode == code);

    private static JournalRequest Journal(long companyId, CurrencyCode currency, DateOnly date, string debit, string credit, decimal amount) => new()
    {
        CompanyId = companyId, SourceModule = "GL", PostingDate = date, TransactionCurrency = currency, EnforceManualJournalRules = false,
        Lines =
        [
            new JournalLineRequest { AccountCode = debit, Side = JournalSide.Debit, TransactionAmount = amount },
            new JournalLineRequest { AccountCode = credit, Side = JournalSide.Credit, TransactionAmount = amount },
        ],
    };
}
