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
/// Covers the database guard on posted journals: editing an amount or account on a posted line, deleting a
/// posted line or journal, or rewriting a posted journal's narrative or date is refused by trigger — while a
/// deferred journal can still be released and a posted one can still be reversed.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PostedJournalImmutabilityTests
{
    private readonly AccountingFixture _fixture;

    public PostedJournalImmutabilityTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Posted_journals_cannot_be_edited_or_deleted_in_the_database()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long postedId;
        long deferredId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
            postedId = (await engine.PostAsync(Journal(companyId, "IMM-1", 300m, deferred: false))).Value.JournalId;
            deferredId = (await engine.PostAsync(Journal(companyId, "IMM-2", 40m, deferred: true))).Value.JournalId;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            long lineId = await db.Set<JournalLine>().Where(l => l.JournalEntryId == postedId && l.AccountCode == "1000").Select(l => l.Id).SingleAsync();

            await Refused(db, $"UPDATE gl.journal_line SET base_debit = 3000, transaction_debit = 3000 WHERE id = {lineId}", "cannot be altered");
            await Refused(db, $"UPDATE gl.journal_line SET account_code = '6800' WHERE id = {lineId}", "cannot be altered");
            await Refused(db, $"DELETE FROM gl.journal_line WHERE id = {lineId}", "cannot be deleted");
            await Refused(db, $"UPDATE gl.journal SET narrative = 'nothing to see here' WHERE id = {postedId}", "cannot be altered");
            await Refused(db, $"UPDATE gl.journal SET posting_date = '2026-01-01' WHERE id = {postedId}", "cannot be altered");
            await Refused(db, $"UPDATE gl.journal SET status = 0 WHERE id = {postedId}", "cannot be altered");
            await Refused(db, $"DELETE FROM gl.journal WHERE id = {postedId}", "cannot be deleted");

            // A deferred (not yet posted) journal is still editable before release.
            (await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE gl.journal SET narrative = 'corrected before release' WHERE id = {deferredId}")).Should().Be(1);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            // The legitimate state changes still work: release the deferred journal, reverse the posted one.
            IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
            (await engine.ReleaseDeferredAsync(companyId, deferredId)).IsSuccess.Should().BeTrue();
            var reversal = await engine.ReverseAsync(companyId, postedId, new DateOnly(2026, 3, 20), "Posted in error");
            reversal.IsSuccess.Should().BeTrue(reversal.IsFailure ? reversal.Error.ToString() : null);

            // Once released, the formerly deferred journal is protected too.
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            await Refused(db, $"UPDATE gl.journal SET narrative = 'too late' WHERE id = {deferredId}", "cannot be altered");

            ConsolidatedTrialBalance tb = (await scope.ServiceProvider.GetRequiredService<IConsolidationService>().ConsolidatedTrialBalanceAsync([companyId], new DateOnly(2026, 3, 31))).Value;
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Single(r => r.AccountCode == "1000").Debit.Should().Be(40m); // 300 reversed, 40 released
        }
    }

    private static async Task Refused(One4AllDbContext db, FormattableString sql, string expected)
    {
        Func<Task> act = () => db.Database.ExecuteSqlInterpolatedAsync(sql);
        (await act.Should().ThrowAsync<Npgsql.PostgresException>()).Which.MessageText.Should().Contain(expected);
    }

    private static JournalRequest Journal(long companyId, string number, decimal amount, bool deferred) => new()
    {
        CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 3, 10), TransactionCurrency = Scenario.Usd,
        EnforceManualJournalRules = false, DocumentType = "Journal", DocumentNumber = number, Narrative = "Original narrative", Deferred = deferred,
        Lines =
        [
            new JournalLineRequest { AccountCode = "1000", Side = JournalSide.Debit, TransactionAmount = amount },
            new JournalLineRequest { AccountCode = "4000", Side = JournalSide.Credit, TransactionAmount = amount },
        ],
    };
}
