using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers importing manual journals from CSV: with an approval threshold of 500, a 100 journal posts and an 800 journal
/// is held for approval; a file whose second journal hits a control account imports nothing at all; an unreadable file
/// is refused before anything posts.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class JournalImportTests
{
    private readonly AccountingFixture _fixture;

    public JournalImportTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Journal_files_import_all_or_nothing_with_approval_over_the_threshold()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Company company = await db.Companies.SingleAsync(c => c.Id == companyId);
        company.ManualJournalApprovalThreshold = 500m;
        await db.SaveChangesAsync();

        IJournalImportService import = scope.ServiceProvider.GetRequiredService<IJournalImportService>();
        const string good = """
            Reference,Date,Account,Debit,Credit,Narrative
            IMP-1,2026-03-10,6800,100,,Stationery
            IMP-1,2026-03-10,2300,,100,
            IMP-2,2026-03-11,6000,800,,Depreciation catch-up
            IMP-2,2026-03-11,2300,,800,
            """;
        IReadOnlyList<ImportedJournalResult> imported = (await import.ImportAsync(companyId, good, 7)).Value;
        imported.Select(r => (r.Reference, r.AwaitingApproval)).Should().Equal(("IMP-1", false), ("IMP-2", true));
        (await scope.ServiceProvider.GetRequiredService<IJournalApprovalService>().AwaitingApprovalAsync(companyId)).Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { JournalNumber = imported[1].JournalNumber, TotalBaseDebit = 800m, PreparedByUserId = 7L });

        const string control = """
            Reference,Date,Account,Debit,Credit
            BAD-1,2026-03-12,6800,10,
            BAD-1,2026-03-12,2300,,10
            BAD-2,2026-03-12,1200,10,
            BAD-2,2026-03-12,4000,,10
            """;
        (await import.ImportAsync(companyId, control, 7)).Error.Code.Should().Be("Posting.ControlAccountManualJournal");
        (await db.Journals.CountAsync(j => j.CompanyId == companyId && j.DocumentNumber == "BAD-1")).Should().Be(0);

        (await import.ImportAsync(companyId, "Reference,Date,Account,Debit,Credit\nX,2026-03-12,6800,10,\n", 7)).Error.Code.Should().Be("Gl.ImportParse");

        ConsolidatedTrialBalance tb = (await scope.ServiceProvider.GetRequiredService<IConsolidationService>().ConsolidatedTrialBalanceAsync([companyId], new DateOnly(2026, 3, 31))).Value;
        tb.IsBalanced.Should().BeTrue();
        tb.Rows.Single(r => r.AccountCode == "6800").Debit.Should().Be(100m);
        tb.Rows.Should().NotContain(r => r.AccountCode == "6000");
    }
}
