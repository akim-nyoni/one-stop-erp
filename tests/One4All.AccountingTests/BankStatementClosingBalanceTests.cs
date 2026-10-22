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
/// A reconciliation is signed off against the statement's closing balance: a statement line never imported leaves the imported lines short
/// of the bank's closing balance, so the sign-off is refused even though the books "reconcile" by calling the deposit unpresented; amounts
/// that differ cannot be matched; and an unpresented item older than 90 days is flagged, not refused.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class BankStatementClosingBalanceTests
{
    private readonly AccountingFixture _fixture;

    public BankStatementClosingBalanceTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Sign_off_proves_the_books_against_the_statement_closing_balance()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        BankAccount bank = new(companyId, "SCB-BANK", "Statement Current Account", Scenario.Usd, "1100");
        db.Set<BankAccount>().Add(bank);
        await db.SaveChangesAsync();

        IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
        async Task<long> Deposit(DateOnly date, decimal amount)
        {
            PostingReceipt receipt = (await engine.PostAsync(new JournalRequest
            {
                CompanyId = companyId, SourceModule = "CB", PostingDate = date, TransactionCurrency = Scenario.Usd,
                Lines =
                [
                    new JournalLineRequest { AccountCode = "1100", Side = JournalSide.Debit, TransactionAmount = amount },
                    new JournalLineRequest { AccountCode = "4000", Side = JournalSide.Credit, TransactionAmount = amount },
                ],
            })).Value;
            return await db.Set<Core.Accounting.JournalLine>().Where(l => l.JournalEntryId == receipt.JournalId && l.AccountCode == "1100").Select(l => l.Id).SingleAsync();
        }

        // The books: a stale January cheque deposit the bank never shows, and two May deposits. The bank's statement shows both May deposits and closes at 150.
        await Deposit(new DateOnly(2026, 1, 5), 30m);
        long first = await Deposit(new DateOnly(2026, 5, 4), 100m);
        long second = await Deposit(new DateOnly(2026, 5, 6), 50m);

        IBankReconciliationService banking = scope.ServiceProvider.GetRequiredService<IBankReconciliationService>();
        (await banking.ImportStatementLinesAsync(companyId, bank.Id, [new StatementLineImport(new DateOnly(2026, 5, 4), "Deposit", 100m, null)])).IsSuccess.Should().BeTrue();
        long firstLine = await db.Set<BankStatementLine>().Where(l => l.BankAccountId == bank.Id).Select(l => l.Id).SingleAsync();
        (await banking.MatchLineAsync(companyId, bank.Id, firstLine, first)).IsSuccess.Should().BeTrue();

        // Only the first line was imported: the books agree with the imported lines (the 50 looks unpresented), but not with the bank's statement.
        DateOnly may = new(2026, 5, 31);
        (await banking.GetReconciliationAsync(companyId, bank.Id, may)).Value.Difference.Should().Be(0m);
        (await banking.SignOffAsync(companyId, bank.Id, may, 150m, 900_801)).Error.Code.Should().Be("Cb.StatementBalanceMismatch");
        (await db.Set<BankAccount>().AsNoTracking().SingleAsync(b => b.Id == bank.Id)).ReconciledTo.Should().BeNull();

        // The missing line, imported at a cent short, cannot be matched to the 50.00 deposit.
        (await banking.ImportStatementLinesAsync(companyId, bank.Id, [new StatementLineImport(new DateOnly(2026, 5, 6), "Deposit", 49.99m, null)])).IsSuccess.Should().BeTrue();
        long wrongLine = await db.Set<BankStatementLine>().Where(l => l.BankAccountId == bank.Id && l.Amount == 49.99m).Select(l => l.Id).SingleAsync();
        (await banking.MatchLineAsync(companyId, bank.Id, wrongLine, second)).Error.Code.Should().Be("Bank.AmountMismatch");
        (await banking.SignOffAsync(companyId, bank.Id, may, 150m, 900_801)).Error.Code.Should().Be("Cb.StatementBalanceMismatch");

        // Imported correctly and matched, it signs off at the statement balance - with the January item flagged as stale.
        await db.Set<BankStatementLine>().Where(l => l.Id == wrongLine).ExecuteDeleteAsync();
        (await banking.ImportStatementLinesAsync(companyId, bank.Id, [new StatementLineImport(new DateOnly(2026, 5, 6), "Deposit", 50m, null)])).IsSuccess.Should().BeTrue();
        long secondLine = await db.Set<BankStatementLine>().Where(l => l.BankAccountId == bank.Id && l.Amount == 50m).Select(l => l.Id).SingleAsync();
        (await banking.MatchLineAsync(companyId, bank.Id, secondLine, second)).IsSuccess.Should().BeTrue();
        BankReconciliationReport signed = (await banking.SignOffAsync(companyId, bank.Id, may, 150m, 900_801)).Value;
        signed.StatementClosingBalance.Should().Be(150m);
        signed.StaleUnpresentedItems.Should().ContainSingle().Which.Amount.Should().Be(30m);
        BankAccount reconciled = await db.Set<BankAccount>().AsNoTracking().SingleAsync(b => b.Id == bank.Id);
        (reconciled.ReconciledTo, reconciled.ReconciledStatementBalance).Should().Be((may, 150m));
    }
}
