using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Core.Accounting;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the ledger guards in the database itself: a journal line cannot carry a negative amount or be a debit and a credit at once, and a
/// journal cannot become posted while its lines do not balance in base currency — even when written straight to the database.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class LedgerDatabaseGuardTests
{
    private readonly AccountingFixture _fixture;

    public LedgerDatabaseGuardTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task The_database_refuses_bad_signs_and_unbalanced_posted_journals()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        long held = (await scope.ServiceProvider.GetRequiredService<IPostingEngine>().PostAsync(new JournalRequest
        {
            CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 3, 10), TransactionCurrency = Scenario.Usd, EnforceManualJournalRules = false, DocumentType = "Journal",
            DocumentNumber = "DBG-1", Narrative = "Held", Deferred = true,
            Lines =
            [
                new JournalLineRequest { AccountCode = "6100", Side = JournalSide.Debit, TransactionAmount = 50m },
                new JournalLineRequest { AccountCode = "1100", Side = JournalSide.Credit, TransactionAmount = 50m },
            ],
        })).Value.JournalId;
        long debitLine = await db.Set<JournalLine>().Where(l => l.JournalEntryId == held && l.BaseDebit > 0m).Select(l => l.Id).SingleAsync();

        async Task Refused(FormattableString sql, string expected)
        {
            Func<Task> act = () => db.Database.ExecuteSqlInterpolatedAsync(sql);
            (await act.Should().ThrowAsync<Npgsql.PostgresException>()).Which.Message.Should().Contain(expected);
        }

        await Refused($"UPDATE gl.journal_line SET base_debit = -50 WHERE id = {debitLine}", "ck_journal_line_amounts_not_negative");
        await Refused($"UPDATE gl.journal_line SET base_credit = 50, transaction_credit = 50 WHERE id = {debitLine}", "ck_journal_line_one_side");

        // Unbalance the held journal (still allowed while unposted), then try to post it straight in the database.
        (await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE gl.journal_line SET base_debit = 60, transaction_debit = 60 WHERE id = {debitLine}")).Should().Be(1);
        await Refused($"UPDATE gl.journal SET status = 2 WHERE id = {held}", "does not balance");
        (await db.Journals.AsNoTracking().Where(j => j.Id == held).Select(j => j.Status).SingleAsync()).Should().Be(JournalStatus.Deferred);
    }
}
