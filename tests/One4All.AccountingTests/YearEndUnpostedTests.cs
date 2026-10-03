using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Core.Common;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the year-end guard: a deferred journal dated in the year blocks the close (it would be left out of the
/// closing profit and could never be released into the permanently closed periods); once released, the year closes
/// and its profit includes the journal.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class YearEndUnpostedTests
{
    private readonly AccountingFixture _fixture;

    public YearEndUnpostedTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_deferred_journal_blocks_year_end_until_released()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long fiscalYearId;
        long deferredId;
        string deferredNumber;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            fiscalYearId = await db.FiscalYears.Where(y => y.CompanyId == companyId).Select(y => y.Id).FirstAsync();

            IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
            (await engine.PostAsync(Journal(companyId, 100m, deferred: false))).IsSuccess.Should().BeTrue();
            PostingReceipt deferred = (await engine.PostAsync(Journal(companyId, 30m, deferred: true))).Value;
            (deferredId, deferredNumber) = (deferred.JournalId, deferred.JournalNumber);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IYearEndService yearEnd = scope.ServiceProvider.GetRequiredService<IYearEndService>();
            Result<YearEndResult> blocked = await yearEnd.RunAsync(companyId, fiscalYearId, "3200");
            blocked.Error.Code.Should().Be("YearEnd.UnpostedJournals");
            blocked.Error.Message.Should().Contain(deferredNumber);

            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            (await db.FiscalYears.AsNoTracking().SingleAsync(y => y.Id == fiscalYearId)).IsClosed.Should().BeFalse();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<IPostingEngine>().ReleaseDeferredAsync(companyId, deferredId)).IsSuccess.Should().BeTrue();
            Result<YearEndResult> closed = await scope.ServiceProvider.GetRequiredService<IYearEndService>().RunAsync(companyId, fiscalYearId, "3200");
            closed.IsSuccess.Should().BeTrue(closed.IsFailure ? closed.Error.ToString() : null);
            closed.Value.NetProfitClosed.Should().Be(130m);
        }
    }

    private static JournalRequest Journal(long companyId, decimal amount, bool deferred) => new()
    {
        CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 11, 30), TransactionCurrency = Scenario.Usd,
        EnforceManualJournalRules = false, Deferred = deferred,
        Lines =
        [
            new JournalLineRequest { AccountCode = "1100", Side = JournalSide.Debit, TransactionAmount = amount },
            new JournalLineRequest { AccountCode = "4000", Side = JournalSide.Credit, TransactionAmount = amount },
        ],
    };
}
