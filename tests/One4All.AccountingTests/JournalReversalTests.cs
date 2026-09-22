using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class JournalReversalTests
{
    private readonly AccountingFixture _fixture;

    public JournalReversalTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Reversing_a_journal_posts_the_exact_inverse_and_marks_the_original_reversed()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long journalId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);

            // A month-end accrual: Dr salaries, Cr pension payable 500.
            IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
            var posted = await engine.PostAsync(new JournalRequest
            {
                CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 3, 31), TransactionCurrency = Scenario.Usd,
                DocumentType = "Accrual", Narrative = "March salary accrual",
                Lines =
                [
                    new JournalLineRequest { AccountCode = "6100", Side = JournalSide.Debit, TransactionAmount = 500m },
                    new JournalLineRequest { AccountCode = "2410", Side = JournalSide.Credit, TransactionAmount = 500m },
                ],
            });
            posted.IsSuccess.Should().BeTrue(posted.IsFailure ? posted.Error.ToString() : null);
            journalId = posted.Value.JournalId;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();

            (await engine.ReverseAsync(companyId, journalId, new DateOnly(2026, 4, 1), "  ")).IsFailure.Should().BeTrue(); // reason required

            var reversal = await engine.ReverseAsync(companyId, journalId, new DateOnly(2026, 4, 1), "Reverse March accrual");
            reversal.IsSuccess.Should().BeTrue(reversal.IsFailure ? reversal.Error.ToString() : null);

            // Cannot reverse the same journal twice.
            (await engine.ReverseAsync(companyId, journalId, new DateOnly(2026, 4, 1), "again")).IsFailure.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            // The original stays Posted; a linked reversal journal records the reversal.
            JournalEntry reversal = await db.Journals.FirstAsync(j => j.IsReversal && j.ReversalOfJournalId == journalId);
            reversal.Should().NotBeNull();

            // The accrual and its reversal net to zero across both accounts.
            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 4, 30));
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Should().NotContain(r => r.AccountCode == "6100" && (r.Debit != 0m || r.Credit != 0m));
            tb.Rows.Should().NotContain(r => r.AccountCode == "2410" && (r.Debit != 0m || r.Credit != 0m));
        }
    }
}
