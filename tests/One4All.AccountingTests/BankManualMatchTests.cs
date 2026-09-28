using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Banking;
using One4All.Core.Accounting;
using One4All.Core.Banking;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers manual bank-statement matching: matching an unreconciled line to a specific cash-book posting,
/// un-matching it again, and the refusals (already reconciled, not a cash-book line).
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class BankManualMatchTests
{
    private readonly AccountingFixture _fixture;

    public BankManualMatchTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_statement_line_can_be_manually_matched_and_unmatched()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long bankAccountId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            BankAccount bank = new(companyId, "MAIN", "Main Bank", Scenario.Usd, "1100");
            db.BankAccounts.Add(bank);
            await db.SaveChangesAsync();
            bankAccountId = bank.Id;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
            await engine.PostAsync(new JournalRequest
            {
                CompanyId = companyId, SourceModule = "CB", PostingDate = new DateOnly(2026, 3, 2), TransactionCurrency = Scenario.Usd,
                DocumentType = "Journal", DocumentNumber = "CB-1",
                Lines = [new JournalLineRequest { AccountCode = "1100", Side = JournalSide.Debit, TransactionAmount = 300m }, new JournalLineRequest { AccountCode = "4000", Side = JournalSide.Credit, TransactionAmount = 300m }],
            });

            IBankReconciliationService recon = scope.ServiceProvider.GetRequiredService<IBankReconciliationService>();
            await recon.ImportStatementLinesAsync(companyId, bankAccountId, [new StatementLineImport(new DateOnly(2026, 3, 2), "Deposit", 300m, "DEP1")]);
        }

        long statementLineId;
        long journalLineId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            statementLineId = await db.Set<BankStatementLine>().Where(s => s.CompanyId == companyId && s.Reference == "DEP1").Select(s => s.Id).SingleAsync();
            journalLineId = await (
                from l in db.Set<JournalLine>()
                join j in db.Journals on l.JournalEntryId equals j.Id
                where j.CompanyId == companyId && l.AccountCode == "1100" && l.TransactionDebit == 300m
                select l.Id).SingleAsync();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IBankReconciliationService recon = scope.ServiceProvider.GetRequiredService<IBankReconciliationService>();

            (await recon.MatchLineAsync(companyId, bankAccountId, statementLineId, journalLineId)).IsSuccess.Should().BeTrue();

            // Matching it again is refused.
            (await recon.MatchLineAsync(companyId, bankAccountId, statementLineId, journalLineId)).Error.Code.Should().Be("Bank.AlreadyReconciled");
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            BankStatementLine line = await db.Set<BankStatementLine>().SingleAsync(s => s.Id == statementLineId);
            line.IsReconciled.Should().BeTrue();
            line.MatchedJournalLineId.Should().Be(journalLineId);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IBankReconciliationService recon = scope.ServiceProvider.GetRequiredService<IBankReconciliationService>();

            (await recon.UnmatchLineAsync(companyId, statementLineId)).IsSuccess.Should().BeTrue();

            // A journal line that is not a cash-book posting on the account is refused.
            (await recon.MatchLineAsync(companyId, bankAccountId, statementLineId, 999_999)).Error.Code.Should().Be("Bank.NotACashBookLine");
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            (await db.Set<BankStatementLine>().SingleAsync(s => s.Id == statementLineId)).IsReconciled.Should().BeFalse();
        }
    }
}
