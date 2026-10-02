using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers releasing deferred journals: a deferred journal is numbered but absent from the ledger, release
/// posts it (balances move, status Posted), a posted journal cannot be released again, the batch release
/// takes everything up to a date oldest first, and the period-close checklist clears.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class DeferredPostingReleaseTests
{
    private readonly AccountingFixture _fixture;

    public DeferredPostingReleaseTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Deferred_journals_stay_off_the_ledger_until_released()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
        }

        long firstId;
        long secondId;
        long laterId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
            firstId = (await engine.PostAsync(Journal(companyId, new DateOnly(2026, 3, 5), "DEF-1", 100m, deferred: true))).Value.JournalId;
            secondId = (await engine.PostAsync(Journal(companyId, new DateOnly(2026, 3, 20), "DEF-2", 50m, deferred: true))).Value.JournalId;
            laterId = (await engine.PostAsync(Journal(companyId, new DateOnly(2026, 4, 2), "DEF-3", 25m, deferred: true))).Value.JournalId;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            (await db.Journals.Where(j => j.Id == firstId).Select(j => j.Status).SingleAsync()).Should().Be(JournalStatus.Deferred);
            (await db.Journals.Where(j => j.Id == firstId).Select(j => j.JournalNumber).SingleAsync()).Should().NotBeNullOrEmpty();

            // Nothing on the ledger yet; the checklist flags three unposted journals as at April.
            (await Cash(scope, companyId, new DateOnly(2026, 4, 30))).Should().Be(0m);
            (await scope.ServiceProvider.GetRequiredService<IPeriodCloseService>().ChecklistAsync(companyId, new DateOnly(2026, 4, 30)))
                .Checks.Single(c => c.Code == "UnpostedJournals").Count.Should().Be(3);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();

            var released = await engine.ReleaseDeferredAsync(companyId, firstId);
            released.IsSuccess.Should().BeTrue(released.IsFailure ? released.Error.ToString() : null);
            released.Value.JournalId.Should().Be(firstId);
            released.Value.Deferred.Should().BeFalse();

            (await engine.ReleaseDeferredAsync(companyId, firstId)).Error.Code.Should().Be("Posting.NotDeferred");
            (await engine.ReleaseDeferredAsync(companyId, 999_999)).Error.Code.Should().Be("Gl.JournalNotFound");

            (await Cash(scope, companyId, new DateOnly(2026, 3, 31))).Should().Be(100m);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            // Batch release up to 31 March takes DEF-2 only (DEF-1 is already posted, DEF-3 is April).
            IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
            DeferredReleaseResult batch = await engine.ReleaseAllDeferredAsync(companyId, new DateOnly(2026, 3, 31));
            batch.Released.Should().ContainSingle();
            batch.Failed.Should().BeEmpty();

            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            (await db.Journals.Where(j => j.Id == secondId).Select(j => j.Status).SingleAsync()).Should().Be(JournalStatus.Posted);
            (await db.Journals.Where(j => j.Id == laterId).Select(j => j.Status).SingleAsync()).Should().Be(JournalStatus.Deferred);

            (await Cash(scope, companyId, new DateOnly(2026, 3, 31))).Should().Be(150m);
            (await scope.ServiceProvider.GetRequiredService<IPeriodCloseService>().ChecklistAsync(companyId, new DateOnly(2026, 3, 31)))
                .Checks.Single(c => c.Code == "UnpostedJournals").Count.Should().Be(0);
        }
    }

    private static JournalRequest Journal(long companyId, DateOnly date, string number, decimal amount, bool deferred) => new()
    {
        CompanyId = companyId, SourceModule = "GL", PostingDate = date, TransactionCurrency = Scenario.Usd, EnforceManualJournalRules = false,
        DocumentType = "Journal", DocumentNumber = number, Deferred = deferred,
        Lines = [new JournalLineRequest { AccountCode = "1000", Side = JournalSide.Debit, TransactionAmount = amount }, new JournalLineRequest { AccountCode = "4000", Side = JournalSide.Credit, TransactionAmount = amount }],
    };

    private static async Task<decimal> Cash(AsyncServiceScope scope, long companyId, DateOnly asAt)
    {
        ConsolidatedTrialBalance tb = (await scope.ServiceProvider.GetRequiredService<IConsolidationService>().ConsolidatedTrialBalanceAsync([companyId], asAt)).Value;
        ConsolidatedTrialBalanceRow? row = tb.Rows.FirstOrDefault(r => r.AccountCode == "1000");
        return row is null ? 0m : row.Debit - row.Credit;
    }
}
