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
/// Covers auto-matching through the service: a same-amount posting months away is not matched inside the
/// default window, a reference ties the right posting even when another is closer, and a widened window
/// picks up the stray.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class BankAutoMatchWindowTests
{
    private readonly AccountingFixture _fixture;

    public BankAutoMatchWindowTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Auto_match_respects_the_date_window_and_prefers_references()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long bankAccountId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            BankAccount bank = new(companyId, "AM-BANK", "Current", Scenario.Usd, "1100");
            db.Set<BankAccount>().Add(bank);
            await db.SaveChangesAsync();
            bankAccountId = bank.Id;

            IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
            // Two 300 payments out: CHQ-7 on 3 March, CHQ-8 on 12 March; and a 900 receipt back in January.
            (await engine.PostAsync(Journal(companyId, new DateOnly(2026, 3, 3), "CHQ-7", -300m))).IsSuccess.Should().BeTrue();
            (await engine.PostAsync(Journal(companyId, new DateOnly(2026, 3, 12), "CHQ-8", -300m))).IsSuccess.Should().BeTrue();
            (await engine.PostAsync(Journal(companyId, new DateOnly(2026, 1, 5), "DEP-1", 900m))).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IBankReconciliationService recon = scope.ServiceProvider.GetRequiredService<IBankReconciliationService>();
            (await recon.ImportStatementLinesAsync(companyId, bankAccountId,
            [
                new StatementLineImport(new DateOnly(2026, 3, 9), "Cheque 7 cleared", -300m, "CHQ-7"), // 6 days after CHQ-7 but only 3 before CHQ-8: the reference must decide
                new StatementLineImport(new DateOnly(2026, 3, 11), "Cheque 8 cleared", -300m, "CHQ-8"), // 1 day before CHQ-8
                new StatementLineImport(new DateOnly(2026, 6, 1), "Deposit", 900m, null), // the January posting is far outside 7 days
            ])).IsSuccess.Should().BeTrue();

            (await recon.AutoMatchAsync(companyId, bankAccountId)).Value.Should().Be(2);

            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            var matched = await (
                from s in db.Set<BankStatementLine>()
                join l in db.Set<JournalLine>() on s.MatchedJournalLineId equals l.Id
                join j in db.Journals on l.JournalEntryId equals j.Id
                where s.BankAccountId == bankAccountId
                select new { s.Reference, j.DocumentNumber })
                .ToListAsync();
            matched.Should().HaveCount(2);
            matched.Should().OnlyContain(m => m.Reference == m.DocumentNumber); // each cheque to its own posting

            // Widening the window (unlimited) picks up the January deposit for the June line.
            (await recon.AutoMatchAsync(companyId, bankAccountId, dateToleranceDays: -1)).Value.Should().Be(1);
            (await db.Set<BankStatementLine>().CountAsync(s => s.BankAccountId == bankAccountId && !s.IsReconciled)).Should().Be(0);
        }
    }

    private static JournalRequest Journal(long companyId, DateOnly date, string number, decimal bankAmount) => new()
    {
        CompanyId = companyId, SourceModule = "CB", PostingDate = date, TransactionCurrency = Scenario.Usd, EnforceManualJournalRules = false,
        DocumentType = "Cheque", DocumentNumber = number,
        Lines = bankAmount >= 0m
            ? [new JournalLineRequest { AccountCode = "1100", Side = JournalSide.Debit, TransactionAmount = bankAmount }, new JournalLineRequest { AccountCode = "4000", Side = JournalSide.Credit, TransactionAmount = bankAmount }]
            : [new JournalLineRequest { AccountCode = "6800", Side = JournalSide.Debit, TransactionAmount = -bankAmount }, new JournalLineRequest { AccountCode = "1100", Side = JournalSide.Credit, TransactionAmount = -bankAmount }],
    };
}
