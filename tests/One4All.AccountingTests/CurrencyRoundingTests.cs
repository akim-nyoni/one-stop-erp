using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Core.Accounting;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Postings keep to each currency's decimals: too many decimals refused, converted amounts rounded to the base currency and the gap taken up.</summary>
[Collection(AccountingCollection.Name)]
public sealed class CurrencyRoundingTests
{
    private readonly AccountingFixture _fixture;

    public CurrencyRoundingTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Amounts_keep_to_their_currency_decimals()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
        JournalLineRequest Line(string account, JournalSide side, decimal amount, decimal? rate = null) => new()
        {
            AccountCode = account, Side = side, TransactionAmount = amount, ExchangeRate = rate,
        };

        // A US dollar amount keyed by hand has two decimals.
        (await engine.PostAsync(new JournalRequest
        {
            CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 3, 10), TransactionCurrency = Scenario.Usd, EnforceManualJournalRules = true,
            Lines = [Line("6800", JournalSide.Debit, 10.005m), Line("1100", JournalSide.Credit, 10.005m)],
        })).Error.Code.Should().Be("Posting.CurrencyPrecision");

        // 100 euros split three ways converts to 36.19 + 36.19 + 36.20 = 108.58 against 108.57: the cent is taken up on the largest converted line.
        PostingReceipt receipt = (await engine.PostAsync(new JournalRequest
        {
            CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 3, 10), TransactionCurrency = Scenario.Eur,
            Lines = [Line("6800", JournalSide.Debit, 33.33m, 1.0857m), Line("6800", JournalSide.Debit, 33.33m, 1.0857m), Line("6800", JournalSide.Debit, 33.34m, 1.0857m),
                Line("1100", JournalSide.Credit, 100m, 1.0857m)],
        })).Value;
        List<JournalLine> lines = await db.Journals.AsNoTracking().Where(j => j.CompanyId == companyId && j.JournalNumber == receipt.JournalNumber).SelectMany(j => j.Lines).ToListAsync();
        lines.Sum(l => l.BaseDebit).Should().Be(lines.Sum(l => l.BaseCredit));
        lines.Should().OnlyContain(l => decimal.Round(l.BaseDebit + l.BaseCredit, 2) == l.BaseDebit + l.BaseCredit, "base amounts are in whole cents");
    }
}
