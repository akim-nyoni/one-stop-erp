using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Core.Accounting;
using One4All.Core.Banking;
using One4All.Core.Common;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Thirteenth audit #8 (brief §3.3): a period-end revaluation is a valuation entry, so it passes a bank reconciliation signed off to that date; only monetary
/// accounts are revalued; and the company's exchange-rate policy picks the rate type for journals posted without an explicit rate.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class FxPolicyAndLockTests
{
    private static readonly CurrencyCode Chf = CurrencyCode.Parse("CHF");
    private readonly AccountingFixture _fixture;

    public FxPolicyAndLockTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Revaluation_skips_non_monetary_accounts_and_passes_a_signed_off_bank_reconciliation()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await ChfAsync(db);
        db.Accounts.Add(new LedgerAccount(companyId, "1162", "Bank (CHF)", AccountType.Asset) { CurrencyRestriction = Chf });
        db.Accounts.Add(new LedgerAccount(companyId, "1482", "Stock held in CHF", AccountType.Asset) { CurrencyRestriction = Chf, IsMonetary = false });
        BankAccount bank = new(companyId, $"CHF-{companyId}", "CHF bank", Chf, "1162");
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();

        IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
        (await engine.PostAsync(Journal(companyId, "1162", 200m))).IsSuccess.Should().BeTrue();
        (await engine.PostAsync(Journal(companyId, "1482", 100m))).IsSuccess.Should().BeTrue();
        DateOnly june30 = new(2026, 6, 30);
        bank.SignOff(june30, 1);
        await db.SaveChangesAsync();

        // An ordinary entry on the reconciled bank is refused; the revaluation, a valuation entry, is not.
        (await engine.PostAsync(Journal(companyId, "1162", 1m))).Error.Code.Should().Be("Posting.BankReconciled");
        Result<FxRevaluationResult> run = await scope.ServiceProvider.GetRequiredService<IFxRevaluationService>().RevalueAsync(companyId, june30);
        run.IsSuccess.Should().BeTrue(run.IsFailure ? run.Error.ToString() : null);
        run.Value.Accounts.Select(a => a.AccountCode).Should().Contain("1162").And.NotContain("1482", "stock is not a monetary item");
        run.Value.Accounts.Single(a => a.AccountCode == "1162").Difference.Should().Be(-10m, "CHF 200 booked at 1.05 is worth 200 at the closing rate of 1.00");
    }

    [SkippableFact]
    public async Task The_company_rate_policy_picks_the_rate_type_for_journals_without_an_explicit_rate()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await ChfAsync(db);
        db.Accounts.Add(new LedgerAccount(companyId, "1163", "Bank (CHF)", AccountType.Asset) { CurrencyRestriction = Chf });
        db.ExchangeRatePolicies.Add(new ExchangeRatePolicy(companyId, "GL", ExchangeRateType.Spot));
        await db.SaveChangesAsync();

        IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
        PostingReceipt posted = (await engine.PostAsync(Journal(companyId, "1163", 100m))).Value;
        (await db.Set<JournalLine>().AsNoTracking().Where(l => l.JournalEntryId == posted.JournalId && l.AccountCode == "1163").Select(l => l.BaseDebit).SingleAsync())
            .Should().Be(120m, "the GL policy says spot (1.20), not the daily rate (1.05)");
    }

    private static async Task ChfAsync(One4AllDbContext db)
    {
        if (!await db.Currencies.AnyAsync(c => c.Code == Chf))
        {
            db.Currencies.Add(new Currency(Chf, "Swiss franc", "CHF"));
        }

        foreach ((ExchangeRateType type, DateOnly from, decimal rate) in new[] { (ExchangeRateType.Daily, new DateOnly(2026, 1, 1), 1.05m), (ExchangeRateType.ClosingRate, new DateOnly(2026, 6, 30), 1.00m), (ExchangeRateType.Spot, new DateOnly(2026, 1, 1), 1.20m) })
        {
            if (!await db.ExchangeRates.AnyAsync(r => r.FromCurrency == Chf && r.ToCurrency == Scenario.Usd && r.RateType == type && r.EffectiveFrom == from))
            {
                db.ExchangeRates.Add(new ExchangeRate(Chf, Scenario.Usd, type, from, rate));
            }
        }

        await db.SaveChangesAsync();
    }

    private static JournalRequest Journal(long companyId, string account, decimal amount) => new()
    {
        CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 6, 1), TransactionCurrency = Chf, DocumentNumber = $"FPL-{account}-{amount}",
        Lines =
        [
            new JournalLineRequest { AccountCode = account, Side = JournalSide.Debit, TransactionAmount = amount },
            new JournalLineRequest { AccountCode = "1000", Side = JournalSide.Credit, TransactionAmount = amount },
        ],
    };
}
