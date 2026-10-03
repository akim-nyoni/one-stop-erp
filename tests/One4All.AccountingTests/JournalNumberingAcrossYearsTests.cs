using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers journal numbering when postings alternate between two open fiscal years (late adjustments into the
/// prior year while the new year is already trading). Each year keeps its own counter, so no number is reissued
/// and no posting fails on the unique journal-number index.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class JournalNumberingAcrossYearsTests
{
    private readonly AccountingFixture _fixture;

    public JournalNumberingAcrossYearsTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Alternating_years_continue_each_years_own_numbering()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);

            FiscalYear next = new(companyId, "2027", new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31));
            for (int month = 1; month <= 12; month++)
            {
                DateOnly start = new(2027, month, 1);
                next.AddPeriod(month, start.ToString("MMMM"), start, start.AddMonths(1).AddDays(-1)).SetStatus("GL", PeriodStatus.Open);
            }

            db.FiscalYears.Add(next);
            await db.SaveChangesAsync();
        }

        DateOnly december = new(2026, 12, 15);
        DateOnly january = new(2027, 1, 10);
        List<string> numbers = [];
        foreach (DateOnly date in new[] { december, january, december, january, december })
        {
            // A fresh scope per posting, as separate user sessions would be.
            await using AsyncServiceScope scope = provider.CreateAsyncScope();
            Result<PostingReceipt> posted = await scope.ServiceProvider.GetRequiredService<IPostingEngine>().PostAsync(Journal(companyId, date));
            posted.IsSuccess.Should().BeTrue(posted.IsFailure ? posted.Error.ToString() : null);
            numbers.Add(posted.Value.JournalNumber);
        }

        numbers.Should().Equal("JNL-2026-000001", "JNL-2027-000001", "JNL-2026-000002", "JNL-2027-000002", "JNL-2026-000003");

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ConsolidatedTrialBalance tb = (await scope.ServiceProvider.GetRequiredService<IConsolidationService>().ConsolidatedTrialBalanceAsync([companyId], new DateOnly(2027, 1, 31))).Value;
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Single(r => r.AccountCode == "1000").Debit.Should().Be(50m);
        }
    }

    private static JournalRequest Journal(long companyId, DateOnly date) => new()
    {
        CompanyId = companyId, SourceModule = "GL", PostingDate = date, TransactionCurrency = Scenario.Usd,
        EnforceManualJournalRules = false, DocumentType = "Journal", DocumentNumber = "ADJ", Narrative = "Year-boundary adjustment",
        Lines =
        [
            new JournalLineRequest { AccountCode = "1000", Side = JournalSide.Debit, TransactionAmount = 10m },
            new JournalLineRequest { AccountCode = "4000", Side = JournalSide.Credit, TransactionAmount = 10m },
        ],
    };
}
