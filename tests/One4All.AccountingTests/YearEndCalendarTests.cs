using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.MasterData;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers a July–June year with an adjustment period: adjustments reach the 13th period only when asked; the year-end closes into it
/// and opens the next July–June year (not a calendar year) with its own adjustment period; the year-end can be reversed — the profit
/// comes back — and run again, until it is hard-locked, after which every period is permanently closed.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class YearEndCalendarTests
{
    private readonly AccountingFixture _fixture;

    public YearEndCalendarTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_july_june_year_closes_reverses_and_locks()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);

        long yearId = (await scope.ServiceProvider.GetRequiredService<IMasterDataService>()
            .CreateFiscalYearAsync(companyId, "2027/28", new DateOnly(2027, 7, 1), FiscalCalendar.Monthly, 12, adjustmentPeriod: true)).Value;
        (await scope.ServiceProvider.GetRequiredService<IPeriodService>().OpenYearAsync(companyId, yearId, ["GL", "AR"])).IsSuccess.Should().BeTrue();
        FiscalYear created = await db.FiscalYears.AsNoTracking().Include(y => y.Periods).SingleAsync(y => y.Id == yearId);
        (created.EndDate, created.Periods.Count, created.Periods.Single(p => p.IsAdjustment).PeriodNumber).Should().Be((new DateOnly(2028, 6, 30), 13, 13));

        IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
        static JournalLineRequest L(string account, JournalSide side, decimal amount) => new() { AccountCode = account, Side = side, TransactionAmount = amount };
        Task<Result<PostingReceipt>> Post(DateOnly date, decimal amount, bool adjust = false) => engine.PostAsync(new JournalRequest
        {
            CompanyId = companyId, SourceModule = "GL", PostingDate = date, TransactionCurrency = Scenario.Usd, ToAdjustmentPeriod = adjust,
            Lines = [L("1100", JournalSide.Debit, amount), L("4000", JournalSide.Credit, amount)],
        });
        DateOnly lastDay = new(2028, 6, 30);
        (await Post(new DateOnly(2027, 9, 1), 1_000m)).IsSuccess.Should().BeTrue();
        long normal = (await Post(lastDay, 50m)).Value.JournalId;
        long adjustment = (await Post(lastDay, 25m, adjust: true)).Value.JournalId;
        (await Post(new DateOnly(2028, 6, 29), 5m, adjust: true)).Error.Code.Should().Be("Posting.NoAdjustmentPeriod");
        Dictionary<long, long> periodOf = await db.Journals.AsNoTracking().Where(j => j.Id == normal || j.Id == adjustment).ToDictionaryAsync(j => j.Id, j => j.FiscalPeriodId);
        FiscalPeriod june = created.Periods.Single(p => p.PeriodNumber == 12), adjustments = created.Periods.Single(p => p.IsAdjustment);
        (periodOf[normal], periodOf[adjustment]).Should().Be((june.Id, adjustments.Id));

        // Year-end: the closing journal goes to the adjustment period, the next year runs July to June.
        IYearEndService yearEnd = scope.ServiceProvider.GetRequiredService<IYearEndService>();
        YearEndResult closed = (await yearEnd.RunAsync(companyId, yearId, "3200")).Value;
        closed.NetProfitClosed.Should().Be(1_075m);
        JournalEntry closing = await db.Journals.AsNoTracking().SingleAsync(j => j.CompanyId == companyId && j.JournalNumber == closed.ClosingJournalNumber);
        closing.FiscalPeriodId.Should().Be(adjustments.Id);
        FiscalYear next = await db.FiscalYears.AsNoTracking().Include(y => y.Periods).SingleAsync(y => y.Id == closed.NextYearId);
        (next.Code, next.StartDate, next.EndDate, next.Periods.Count(p => !p.IsAdjustment), next.Periods.Count(p => p.IsAdjustment)).Should().Be(("2028/29", new DateOnly(2028, 7, 1), new DateOnly(2029, 6, 30), 12, 1));
        (await yearEnd.RunAsync(companyId, yearId, "3200")).Error.Code.Should().Be("YearEnd.AlreadyClosed");
        (await Post(new DateOnly(2028, 1, 10), 5m)).Error.Code.Should().Be("Posting.PeriodNotOpen");

        // Reverse: the profit is back in the year; then close again.
        IGeneralLedgerReports reports = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
        (await reports.IncomeStatementAsync(companyId, new DateOnly(2027, 7, 1), lastDay)).NetProfit.Should().Be(0m);
        (await yearEnd.ReverseAsync(companyId, yearId, " ")).Error.Code.Should().Be("YearEnd.ReasonRequired");
        string reversal = (await yearEnd.ReverseAsync(companyId, yearId, "Audit adjustment to come")).Value;
        reversal.Should().NotBe("(none)");
        (await reports.IncomeStatementAsync(companyId, new DateOnly(2027, 7, 1), lastDay)).NetProfit.Should().Be(1_075m);
        (await db.FiscalYears.AsNoTracking().SingleAsync(y => y.Id == yearId)).IsClosed.Should().BeFalse();
        (await Post(lastDay, 10m, adjust: true)).IsSuccess.Should().BeTrue();
        YearEndResult again = (await yearEnd.RunAsync(companyId, yearId, "3200")).Value;
        (again.NetProfitClosed, again.NextYearId).Should().Be((1_085m, closed.NextYearId));

        // Hard lock: final.
        (await yearEnd.HardLockAsync(companyId, closed.NextYearId)).Error.Code.Should().Be("YearEnd.NotClosed");
        (await yearEnd.HardLockAsync(companyId, yearId)).IsSuccess.Should().BeTrue();
        (await yearEnd.ReverseAsync(companyId, yearId, "Too late")).Error.Code.Should().Be("YearEnd.HardLocked");
        (await yearEnd.RunAsync(companyId, yearId, "3200")).Error.Code.Should().Be("YearEnd.HardLocked");
        FiscalYear locked = await db.FiscalYears.AsNoTracking().Include(y => y.Periods).ThenInclude(p => p.ModuleStatuses).SingleAsync(y => y.Id == yearId);
        locked.IsHardLocked.Should().BeTrue();
        locked.Periods.Should().OnlyContain(p => p.StatusFor("GL") == PeriodStatus.PermanentlyClosed);
    }
}
