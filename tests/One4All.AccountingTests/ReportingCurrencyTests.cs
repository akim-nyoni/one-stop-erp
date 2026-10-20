using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the reporting currency kept on every posting: base postings at the day's base→reporting rate, foreign postings through base,
/// each journal balancing in the reporting currency, a reversal repeating the original amounts whatever the later rate, the period
/// balances and the GL2R trial balance carrying them, and a posting refused when there is no rate.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ReportingCurrencyTests
{
    private static readonly CurrencyCode Zwg = CurrencyCode.Parse("ZWG");

    private readonly AccountingFixture _fixture;

    public ReportingCurrencyTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Every_posting_keeps_the_reporting_currency()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        if (!await db.Currencies.AnyAsync(c => c.Code == Zwg))
        {
            db.Currencies.Add(new Currency(Zwg, "Zimbabwe Gold", "ZiG"));
        }

        foreach ((DateOnly date, decimal rate) in new[] { (new DateOnly(2026, 1, 1), 26.5m), (new DateOnly(2026, 3, 1), 27m) })
        {
            if (!await db.ExchangeRates.AnyAsync(r => r.FromCurrency == Scenario.Usd && r.ToCurrency == Zwg && r.RateType == ExchangeRateType.Daily && r.EffectiveFrom == date))
            {
                db.ExchangeRates.Add(new ExchangeRate(Scenario.Usd, Zwg, ExchangeRateType.Daily, date, rate));
            }
        }

        Company company = await db.Companies.SingleAsync(c => c.Id == companyId);
        company.SetReportingCurrency(Zwg);
        await db.SaveChangesAsync();

        IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
        static JournalLineRequest L(string account, JournalSide side, decimal amount) => new() { AccountCode = account, Side = side, TransactionAmount = amount };
        Task<Result<PostingReceipt>> Post(DateOnly date, CurrencyCode currency, params JournalLineRequest[] lines) => engine.PostAsync(new JournalRequest
        {
            CompanyId = companyId, SourceModule = "GL", PostingDate = date, TransactionCurrency = currency, Lines = lines,
        });
        async Task<List<JournalLine>> Lines(long journalId) => await db.Set<JournalLine>().AsNoTracking().Where(l => l.JournalEntryId == journalId).OrderBy(l => l.Id).ToListAsync();

        // In base: at February's 26.5.
        PostingReceipt sale = (await Post(new DateOnly(2026, 2, 10), Scenario.Usd, L("1100", JournalSide.Debit, 100m), L("4000", JournalSide.Credit, 33.33m), L("4000", JournalSide.Credit, 66.67m))).Value;
        List<JournalLine> saleLines = await Lines(sale.JournalId);
        saleLines.Select(l => (l.ReportingDebit, l.ReportingCredit)).Should().Equal((2650m, 0m), (0m, 883.2450m), (0m, 1766.7550m));
        saleLines.Sum(l => l.ReportingDebit - l.ReportingCredit).Should().Be(0m);

        // In euros: through base (10 EUR = 11 USD) at March's 27.
        PostingReceipt euros = (await Post(new DateOnly(2026, 3, 5), Scenario.Eur, L("1100", JournalSide.Debit, 10m), L("4000", JournalSide.Credit, 10m))).Value;
        (await Lines(euros.JournalId)).Select(l => l.ReportingDebit + l.ReportingCredit).Should().Equal(297m, 297m);

        // A reversal in March repeats February's amounts, not March's rate.
        PostingReceipt reversal = (await engine.ReverseAsync(companyId, sale.JournalId, new DateOnly(2026, 3, 6), "Wrong customer")).Value;
        (await Lines(reversal.JournalId)).Select(l => (l.ReportingDebit, l.ReportingCredit)).Should().Equal((0m, 2650m), (883.2450m, 0m), (1766.7550m, 0m));

        // Period balances and the GL2R trial balance carry it.
        (await db.Set<GlPeriodBalance>().AsNoTracking().Where(b => b.CompanyId == companyId && b.AccountCode == "1100").SumAsync(b => b.ReportingDebit - b.ReportingCredit)).Should().Be(297m);
        var report = await scope.ServiceProvider.GetRequiredService<IReportRunner>().RenderAsync(companyId, "GL2R", new Dictionary<string, object?> { ["AsAtDate"] = new DateOnly(2026, 3, 31) }, ReportFormat.Csv);
        string csv = Encoding.UTF8.GetString(report.Value.Content);
        csv.Should().Contain("1100,").And.Contain("11.00,ZWG,297").And.Contain("TOTAL,Difference (should be nil),0.00,ZWG,0");

        // Statements in the reporting currency add the stored amounts (each at its own day's rate), whatever single rate is passed.
        TrialBalanceReport inZwg = await scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>().TrialBalanceAsync(companyId, new DateOnly(2026, 3, 31),
            new LedgerReportOptions(false, "ZWG", 99m));
        inZwg.Rows.Where(r => r.AccountCode == "1100").Sum(r => r.Debit - r.Credit).Should().Be(297m);

        // No rate, no posting.
        company.SetReportingCurrency(CurrencyCode.Parse("JPY"));
        await db.SaveChangesAsync();
        (await Post(new DateOnly(2026, 3, 7), Scenario.Usd, L("1100", JournalSide.Debit, 5m), L("4000", JournalSide.Credit, 5m))).Error.Code.Should().Be("Posting.NoReportingRate");
    }
}
