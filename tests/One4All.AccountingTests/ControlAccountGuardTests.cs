using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the database guards on journal lines (brief §3.1): a manual journal cannot reach a control account even when
/// a line is changed directly in the database; system postings still reach control accounts; and a posted journal
/// cannot gain extra lines.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ControlAccountGuardTests
{
    private readonly AccountingFixture _fixture;

    public ControlAccountGuardTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Manual_journals_cannot_reach_control_accounts_and_posted_journals_cannot_grow()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();

        // The engine refuses a manual journal to the AR control account.
        (await engine.PostAsync(Manual(companyId, "1200", deferred: false))).Error.Code.Should().Be("Posting.ControlAccountManualJournal");

        // A manual journal on ordinary accounts is recorded as manual; redirecting a line to a control account in the
        // database is refused by the trigger (the journal is deferred, so the posted-journal guard does not apply).
        PostingReceipt deferred = (await engine.PostAsync(Manual(companyId, "6800", deferred: true))).Value;
        (await db.Journals.AsNoTracking().SingleAsync(j => j.Id == deferred.JournalId)).IsManual.Should().BeTrue();
        long lineId = await db.Set<JournalLine>().Where(l => l.JournalEntryId == deferred.JournalId && l.AccountCode == "6800").Select(l => l.Id).SingleAsync();
        Func<Task> redirect = () => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE gl.journal_line SET account_code = '1200' WHERE id = {lineId}");
        (await redirect.Should().ThrowAsync<Npgsql.PostgresException>()).Which.MessageText.Should().Contain("control account");

        // System postings still reach control accounts.
        Customer customer = new(companyId, "CAG-C", "Customer", Scenario.Usd);
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        Result<TradingPostingResult> invoice = await scope.ServiceProvider.GetRequiredService<ISalesLedgerService>().PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = "CAG-I", DocumentDate = new DateOnly(2026, 3, 10), Currency = Scenario.Usd, NetAmount = 100m, SalesAccountCode = "4000",
        });
        invoice.IsSuccess.Should().BeTrue();

        // A posted journal cannot gain a line, however it is written.
        long postedId = await db.Journals.Where(j => j.CompanyId == companyId && j.JournalNumber == invoice.Value.JournalNumber).Select(j => j.Id).SingleAsync();
        string columns = string.Join(", ", await db.Database
            .SqlQueryRaw<string>("SELECT column_name AS \"Value\" FROM information_schema.columns WHERE table_schema = 'gl' AND table_name = 'journal_line' AND column_name <> 'id' ORDER BY ordinal_position")
            .ToListAsync());
        Func<Task> extraLine = () => db.Database.ExecuteSqlRawAsync(
            $"INSERT INTO gl.journal_line ({columns}) SELECT {columns} FROM gl.journal_line WHERE journal_entry_id = {postedId} LIMIT 1");
        (await extraLine.Should().ThrowAsync<Npgsql.PostgresException>()).Which.MessageText.Should().Contain("cannot be added to posted journal");
    }

    private static JournalRequest Manual(long companyId, string debitAccount, bool deferred) => new()
    {
        CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 3, 10), TransactionCurrency = Scenario.Usd,
        EnforceManualJournalRules = true, Deferred = deferred, DocumentNumber = "MAN-1",
        Lines =
        [
            new JournalLineRequest { AccountCode = debitAccount, Side = JournalSide.Debit, TransactionAmount = 25m },
            new JournalLineRequest { AccountCode = "1000", Side = JournalSide.Credit, TransactionAmount = 25m },
        ],
    };
}
