using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Banking;
using One4All.Core.Accounting;
using One4All.Core.Banking;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers the cash-position report: each active bank account's cash-book balance and the total.</summary>
[Collection(AccountingCollection.Name)]
public sealed class CashPositionTests
{
    private readonly AccountingFixture _fixture;

    public CashPositionTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Cash_position_lists_each_bank_balance_and_the_total()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long mainId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            BankAccount main = new(companyId, "MAIN", "Main Bank", Scenario.Usd, "1100");
            BankAccount safe = new(companyId, "SAFE", "Cash in Safe", Scenario.Usd, "1010");
            BankAccount closed = new(companyId, "OLD", "Closed Account", Scenario.Usd, "1050") { IsActive = false };
            db.BankAccounts.AddRange(main, safe, closed);
            await db.SaveChangesAsync();
            mainId = main.Id;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
            // Main bank +800 (sale), safe +200 (transfer in from main → net main 600, safe 200).
            await engine.PostAsync(Journal(companyId, new DateOnly(2026, 3, 5), "CP-1", ("1100", JournalSide.Debit, 800m), ("4000", JournalSide.Credit, 800m)));
            await engine.PostAsync(Journal(companyId, new DateOnly(2026, 3, 6), "CP-2", ("1010", JournalSide.Debit, 200m), ("1100", JournalSide.Credit, 200m)));
            // A movement after the as-at date must not count.
            await engine.PostAsync(Journal(companyId, new DateOnly(2026, 4, 2), "CP-3", ("1100", JournalSide.Debit, 999m), ("4000", JournalSide.Credit, 999m)));

            // One unreconciled statement line on the main account.
            IBankReconciliationService recon = scope.ServiceProvider.GetRequiredService<IBankReconciliationService>();
            await recon.ImportStatementLinesAsync(companyId, mainId, [new StatementLineImport(new DateOnly(2026, 3, 5), "Deposit", 800m, "DEP")]);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IBankReconciliationService recon = scope.ServiceProvider.GetRequiredService<IBankReconciliationService>();
            var result = await recon.CashPositionAsync(companyId, new DateOnly(2026, 3, 31));

            result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : null);
            CashPositionReport position = result.Value;

            position.Rows.Should().HaveCount(2); // the closed account is excluded
            position.Rows.Should().Contain(r => r.Code == "MAIN" && r.Balance == 600m && r.UnreconciledLines == 1);
            position.Rows.Should().Contain(r => r.Code == "SAFE" && r.Balance == 200m && r.UnreconciledLines == 0);
            position.TotalBalance.Should().Be(800m);
        }
    }

    private static JournalRequest Journal(long companyId, DateOnly date, string doc, params (string Account, JournalSide Side, decimal Amount)[] lines) => new()
    {
        CompanyId = companyId, SourceModule = "GL", PostingDate = date, TransactionCurrency = Scenario.Usd,
        EnforceManualJournalRules = false, DocumentType = "Journal", DocumentNumber = doc,
        Lines = lines.Select(l => new JournalLineRequest { AccountCode = l.Account, Side = l.Side, TransactionAmount = l.Amount }).ToList(),
    };
}
