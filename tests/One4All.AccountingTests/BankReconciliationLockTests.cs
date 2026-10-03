using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Banking;
using One4All.Core.Banking;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers signing off a bank reconciliation: a reconciled March is signed off to 31 March; afterwards an entry to the
/// bank dated in March, a statement line in March, and undoing a March match are all refused, while April stays open; a
/// second sign-off to the same date is refused, and April cannot be signed off while a statement line is missing from
/// the ledger.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class BankReconciliationLockTests
{
    private readonly AccountingFixture _fixture;

    public BankReconciliationLockTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_signed_off_period_is_locked()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        BankAccount bank = new(companyId, "LOCK-BANK", "Locked Current Account", Scenario.Usd, "1100");
        db.Set<BankAccount>().Add(bank);
        await db.SaveChangesAsync();

        IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
        Task<One4All.Core.Common.Result<PostingReceipt>> Deposit(DateOnly date, decimal amount) => engine.PostAsync(new JournalRequest
        {
            CompanyId = companyId, SourceModule = "CB", PostingDate = date, TransactionCurrency = Scenario.Usd,
            Lines =
            [
                new JournalLineRequest { AccountCode = "1100", Side = JournalSide.Debit, TransactionAmount = amount },
                new JournalLineRequest { AccountCode = "4000", Side = JournalSide.Credit, TransactionAmount = amount },
            ],
        });

        (await Deposit(new DateOnly(2026, 3, 5), 250m)).IsSuccess.Should().BeTrue();
        IBankReconciliationService banking = scope.ServiceProvider.GetRequiredService<IBankReconciliationService>();
        (await banking.ImportStatementLinesAsync(companyId, bank.Id, [new StatementLineImport(new DateOnly(2026, 3, 5), "Deposit", 250m, null)])).IsSuccess.Should().BeTrue();
        (await banking.AutoMatchAsync(companyId, bank.Id)).Value.Should().Be(1);

        DateOnly march = new(2026, 3, 31);
        (await banking.SignOffAsync(companyId, bank.Id, march, 7)).Value.Difference.Should().Be(0m);
        (await db.Set<BankAccount>().AsNoTracking().SingleAsync(b => b.Id == bank.Id)).ReconciledTo.Should().Be(march);

        (await Deposit(new DateOnly(2026, 3, 20), 40m)).Error.Code.Should().Be("Posting.BankReconciled");
        (await Deposit(new DateOnly(2026, 4, 2), 40m)).IsSuccess.Should().BeTrue();
        (await banking.ImportStatementLinesAsync(companyId, bank.Id, [new StatementLineImport(new DateOnly(2026, 3, 30), "Late charge", -5m, null)])).Error.Code.Should().Be("Cb.ReconciledLocked");
        long matched = await db.Set<BankStatementLine>().Where(l => l.BankAccountId == bank.Id && l.IsReconciled).Select(l => l.Id).SingleAsync();
        (await banking.UnmatchLineAsync(companyId, matched)).Error.Code.Should().Be("Cb.ReconciledLocked");
        (await banking.SignOffAsync(companyId, bank.Id, march, 7)).Error.Code.Should().Be("Cb.AlreadyReconciled");

        (await banking.ImportStatementLinesAsync(companyId, bank.Id, [new StatementLineImport(new DateOnly(2026, 4, 3), "Bank charges", -10m, null)])).IsSuccess.Should().BeTrue();
        (await banking.SignOffAsync(companyId, bank.Id, new DateOnly(2026, 4, 30), 7)).Error.Code.Should().Be("Cb.NotReconciled");
    }
}
