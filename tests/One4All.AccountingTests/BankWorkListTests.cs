using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Banking;
using One4All.Core.Accounting;
using One4All.Core.Banking;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the reconciliation work list: statement lines carry their match, unpresented GL lines carry ids,
/// a manual match through those ids moves a line across, and unmatching restores it.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class BankWorkListTests
{
    private readonly AccountingFixture _fixture;

    public BankWorkListTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Work_list_exposes_ids_for_matching_and_unmatching()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long bankAccountId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            BankAccount bank = new(companyId, "WL-BANK", "Current", Scenario.Usd, "1100");
            db.Set<BankAccount>().Add(bank);
            await db.SaveChangesAsync();
            bankAccountId = bank.Id;

            IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
            (await engine.PostAsync(Journal(companyId, new DateOnly(2026, 3, 2), "WL-DEP", 500m))).IsSuccess.Should().BeTrue();
            (await engine.PostAsync(Journal(companyId, new DateOnly(2026, 3, 4), "WL-CHQ", -120m))).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IBankReconciliationService recon = scope.ServiceProvider.GetRequiredService<IBankReconciliationService>();
            (await recon.ImportStatementLinesAsync(companyId, bankAccountId,
            [
                new StatementLineImport(new DateOnly(2026, 3, 3), "Deposit", 500m, "WL-DEP"),
                new StatementLineImport(new DateOnly(2026, 3, 20), "Cheque presented late", -120m, "WL-CHQ"), // outside the auto-match window: needs a manual match
                new StatementLineImport(new DateOnly(2026, 3, 20), "Bank fee", -1m, "WL-FEE"),
            ])).IsSuccess.Should().BeTrue();
            (await recon.AutoMatchAsync(companyId, bankAccountId)).Value.Should().Be(1);

            BankWorkList list = (await recon.WorkListAsync(companyId, bankAccountId, new DateOnly(2026, 3, 31))).Value;
            list.StatementLines.Should().HaveCount(3);
            StatementWorkItem deposit = list.StatementLines.Single(s => s.Reference == "WL-DEP");
            deposit.MatchedJournalLineId.Should().NotBeNull();
            StatementWorkItem cheque = list.StatementLines.Single(s => s.Reference == "WL-CHQ");
            cheque.MatchedJournalLineId.Should().BeNull();
            list.UnpresentedGlLines.Should().ContainSingle(g => g.DocumentNumber == "WL-CHQ" && g.Amount == -120m);
            GlWorkItem chequePosting = list.UnpresentedGlLines.Single();

            // A line of another amount cannot be matched to the cheque; the operator matches the cheque by hand through the ids the work list gave them.
            StatementWorkItem fee = list.StatementLines.Single(s => s.Reference == "WL-FEE");
            (await recon.MatchLineAsync(companyId, bankAccountId, fee.Id, chequePosting.Id)).Error.Code.Should().Be("Bank.AmountMismatch");
            (await recon.MatchLineAsync(companyId, bankAccountId, cheque.Id, chequePosting.Id)).IsSuccess.Should().BeTrue();
            BankWorkList after = (await recon.WorkListAsync(companyId, bankAccountId, new DateOnly(2026, 3, 31))).Value;
            after.UnpresentedGlLines.Should().BeEmpty();
            after.StatementLines.Single(s => s.Id == cheque.Id).MatchedJournalLineId.Should().Be(chequePosting.Id);

            // And unmatches it again.
            (await recon.UnmatchLineAsync(companyId, cheque.Id)).IsSuccess.Should().BeTrue();
            BankWorkList restored = (await recon.WorkListAsync(companyId, bankAccountId, new DateOnly(2026, 3, 31))).Value;
            restored.UnpresentedGlLines.Should().ContainSingle(g => g.Id == chequePosting.Id);

            (await recon.WorkListAsync(companyId, 999_999, new DateOnly(2026, 3, 31))).Error.Code.Should().Be("Cb.BankNotFound");
        }
    }

    private static JournalRequest Journal(long companyId, DateOnly date, string number, decimal bankAmount) => new()
    {
        CompanyId = companyId, SourceModule = "CB", PostingDate = date, TransactionCurrency = Scenario.Usd, EnforceManualJournalRules = false,
        DocumentType = "Cashbook", DocumentNumber = number,
        Lines = bankAmount >= 0m
            ? [new JournalLineRequest { AccountCode = "1100", Side = JournalSide.Debit, TransactionAmount = bankAmount }, new JournalLineRequest { AccountCode = "4000", Side = JournalSide.Credit, TransactionAmount = bankAmount }]
            : [new JournalLineRequest { AccountCode = "6800", Side = JournalSide.Debit, TransactionAmount = -bankAmount }, new JournalLineRequest { AccountCode = "1100", Side = JournalSide.Credit, TransactionAmount = -bankAmount }],
    };
}
