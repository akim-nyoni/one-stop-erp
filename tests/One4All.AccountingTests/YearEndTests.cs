using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Common;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class YearEndTests
{
    private readonly AccountingFixture _fixture;

    public YearEndTests(AccountingFixture fixture) => _fixture = fixture;

    private static JournalLineRequest L(string account, JournalSide side, decimal amount) =>
        new() { AccountCode = account, Side = side, TransactionAmount = amount };

    [SkippableFact]
    public async Task Year_end_rolls_profit_to_retained_earnings_and_opens_next_year()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long fiscalYearId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            fiscalYearId = await db.FiscalYears.Where(y => y.CompanyId == companyId).Select(y => y.Id).FirstAsync();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
            // Sales 100 + VAT 15, receipt, COGS 60 -> net profit 40.
            await engine.PostAsync(new JournalRequest { CompanyId = companyId, SourceModule = "AR", PostingDate = new DateOnly(2026, 3, 10), TransactionCurrency = Scenario.Usd, Lines = [L("1200", JournalSide.Debit, 115m), L("4000", JournalSide.Credit, 100m), L("2200", JournalSide.Credit, 15m)] });
            await engine.PostAsync(new JournalRequest { CompanyId = companyId, SourceModule = "CB", PostingDate = new DateOnly(2026, 3, 20), TransactionCurrency = Scenario.Usd, Lines = [L("1100", JournalSide.Debit, 115m), L("1200", JournalSide.Credit, 115m)] });
            await engine.PostAsync(new JournalRequest { CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 3, 25), TransactionCurrency = Scenario.Usd, EnforceManualJournalRules = true, Lines = [L("5000", JournalSide.Debit, 60m), L("1100", JournalSide.Credit, 60m)] });
        }

        // Run year-end.
        YearEndResult result;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IYearEndService yearEnd = scope.ServiceProvider.GetRequiredService<IYearEndService>();
            Result<YearEndResult> run = await yearEnd.RunAsync(companyId, fiscalYearId, "3200");
            run.IsSuccess.Should().BeTrue(run.IsFailure ? run.Error.ToString() : null);
            result = run.Value;
        }

        result.NetProfitClosed.Should().Be(40m);
        result.NextYearId.Should().BeGreaterThan(0);

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IGeneralLedgerReports reports = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();

            // After close, the year's P&L nets to zero (income/expense zeroed by the closing journal).
            IncomeStatement pl = await reports.IncomeStatementAsync(companyId, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
            pl.NetProfit.Should().Be(0m);

            // Retained earnings now carries the profit; the balance sheet still balances.
            BalanceSheet bs = await reports.BalanceSheetAsync(companyId, new DateOnly(2026, 12, 31));
            bs.IsBalanced.Should().BeTrue();
            bs.Equity.Should().Contain(l => l.AccountCode == "3200" && l.Amount == 40m);

            // The next fiscal year exists.
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            (await db.FiscalYears.AnyAsync(y => y.CompanyId == companyId && y.Code == "2027")).Should().BeTrue();
        }

        // The closed year is locked: a further posting into it is rejected.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
            Result<PostingReceipt> late = await engine.PostAsync(new JournalRequest
            {
                CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 6, 1), TransactionCurrency = Scenario.Usd,
                EnforceManualJournalRules = true, Lines = [L("1000", JournalSide.Debit, 5m), L("4000", JournalSide.Credit, 5m)],
            });
            late.IsFailure.Should().BeTrue();
            late.Error.Code.Should().Be("Posting.PeriodNotOpen");
        }
    }
}
