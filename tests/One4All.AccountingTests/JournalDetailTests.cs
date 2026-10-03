using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the journal drill-down: every line with its account name and base / transaction amounts, debits
/// first, balanced totals, the reversal links in both directions, deferred journals visible, and an unknown
/// number returning null. Also checks a nominal-activity line drills to its own journal.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class JournalDetailTests
{
    private readonly AccountingFixture _fixture;

    public JournalDetailTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Journal_detail_shows_lines_totals_and_reversal_links()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        string originalNumber;
        string reversalNumber;
        string deferredNumber;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);

            IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
            // Listed credit-first on purpose: the detail must still show the debit first.
            var posted = await engine.PostAsync(new JournalRequest
            {
                CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 3, 15), TransactionCurrency = Scenario.Usd,
                EnforceManualJournalRules = false, DocumentType = "Journal", DocumentNumber = "JD-1", Reference = "REF-JD", Narrative = "Stationery purchase",
                Lines =
                [
                    new JournalLineRequest { AccountCode = "1000", Side = JournalSide.Credit, TransactionAmount = 75m, Narrative = "Paid from till" },
                    new JournalLineRequest { AccountCode = "6800", Side = JournalSide.Debit, TransactionAmount = 75m, Narrative = "Printer paper" },
                ],
            });
            posted.IsSuccess.Should().BeTrue(posted.IsFailure ? posted.Error.ToString() : null);
            originalNumber = posted.Value.JournalNumber;

            var reversal = await engine.ReverseAsync(companyId, posted.Value.JournalId, new DateOnly(2026, 3, 20), "Wrong account");
            reversal.IsSuccess.Should().BeTrue(reversal.IsFailure ? reversal.Error.ToString() : null);
            reversalNumber = reversal.Value.JournalNumber;

            var deferred = await engine.PostAsync(new JournalRequest
            {
                CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 3, 25), TransactionCurrency = Scenario.Usd,
                EnforceManualJournalRules = false, DocumentType = "Journal", DocumentNumber = "JD-2", Deferred = true,
                Lines =
                [
                    new JournalLineRequest { AccountCode = "6800", Side = JournalSide.Debit, TransactionAmount = 10m },
                    new JournalLineRequest { AccountCode = "1000", Side = JournalSide.Credit, TransactionAmount = 10m },
                ],
            });
            deferredNumber = deferred.Value.JournalNumber;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();

            JournalDetail? original = await gl.JournalDetailAsync(companyId, originalNumber);
            original.Should().NotBeNull();
            original!.Status.Should().Be("Posted");
            original.DocumentNumber.Should().Be("JD-1");
            original.Reference.Should().Be("REF-JD");
            original.ReversalOf.Should().BeNull();
            original.ReversedBy.Should().Be(reversalNumber);
            original.Lines.Should().HaveCount(2);
            original.Lines[0].AccountCode.Should().Be("6800"); // debit first
            original.Lines[0].AccountName.Should().Be("Office Supplies");
            original.Lines[0].Debit.Should().Be(75m);
            original.Lines[0].TransactionAmount.Should().Be(75m);
            original.Lines[0].Narrative.Should().Be("Printer paper");
            original.Lines[1].AccountCode.Should().Be("1000");
            original.Lines[1].Credit.Should().Be(75m);
            original.Lines[1].TransactionAmount.Should().Be(-75m);
            original.Lines[1].TransactionCurrency.Should().Be("USD");
            original.TotalDebit.Should().Be(original.TotalCredit);

            JournalDetail? reversal = await gl.JournalDetailAsync(companyId, reversalNumber);
            reversal!.ReversalOf.Should().Be(originalNumber);
            reversal.ReversedBy.Should().BeNull();
            reversal.Lines.Single(l => l.AccountCode == "1000").Debit.Should().Be(75m); // the exact inverse

            (await gl.JournalDetailAsync(companyId, deferredNumber))!.Status.Should().Be("Deferred");
            (await gl.JournalDetailAsync(companyId, "NO-SUCH-JOURNAL")).Should().BeNull();

            // Every line of the nominal activity for 6800 drills to a journal that exists.
            NominalActivityReport activity = await gl.NominalActivityAsync(companyId, "6800", new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));
            activity.Lines.Should().HaveCount(2); // the original and its reversal; the deferred journal is not on the ledger
            activity.ClosingBalance.Should().Be(0m);
            foreach (NominalActivityLine line in activity.Lines)
            {
                (await gl.JournalDetailAsync(companyId, line.JournalNumber)).Should().NotBeNull();
            }
        }
    }
}
