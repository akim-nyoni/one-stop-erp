using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers the trial balance summarised by account type.</summary>
[Collection(AccountingCollection.Name)]
public sealed class TrialBalanceByTypeTests
{
    private readonly AccountingFixture _fixture;

    public TrialBalanceByTypeTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Balances_are_summarised_and_grouped_by_type()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
            // Cash sale: Dr cash 1000 (Asset), Cr sales 1000 (Income).
            await engine.PostAsync(new JournalRequest
            {
                CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 3, 10), TransactionCurrency = Scenario.Usd,
                EnforceManualJournalRules = false, DocumentType = "Journal", DocumentNumber = "TBT-1",
                Lines = [new JournalLineRequest { AccountCode = "1000", Side = JournalSide.Debit, TransactionAmount = 1000m }, new JournalLineRequest { AccountCode = "4000", Side = JournalSide.Credit, TransactionAmount = 1000m }],
            });
            // Expense paid from bank: Dr office supplies 300 (Expense), Cr bank 300 (Asset).
            await engine.PostAsync(new JournalRequest
            {
                CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 3, 12), TransactionCurrency = Scenario.Usd,
                EnforceManualJournalRules = false, DocumentType = "Journal", DocumentNumber = "TBT-2",
                Lines = [new JournalLineRequest { AccountCode = "6800", Side = JournalSide.Debit, TransactionAmount = 300m }, new JournalLineRequest { AccountCode = "1100", Side = JournalSide.Credit, TransactionAmount = 300m }],
            });
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceByTypeReport tb = await gl.TrialBalanceByTypeAsync(companyId, new DateOnly(2026, 3, 31));

            tb.IsBalanced.Should().BeTrue();
            // Assets net: 1000 cash − 300 bank = 700 debit.
            tb.Rows.Should().Contain(r => r.Type == AccountType.Asset && r.Debit == 700m);
            tb.Rows.Should().Contain(r => r.Type == AccountType.Income && r.Credit == 1000m);
            tb.Rows.Should().Contain(r => r.Type == AccountType.Expense && r.Debit == 300m);
            tb.TotalDebit.Should().Be(1000m);  // 700 assets + 300 expense
            tb.TotalCredit.Should().Be(1000m); // 1000 income
        }
    }
}
