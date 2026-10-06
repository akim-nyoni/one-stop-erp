using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Core.Reporting;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using One4All.Reporting;
using One4All.Reporting.Engine;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the viewer's layout format through the audited runner: the journal daybook comes back laid out with each journal number linked
/// to its journal, and printing it twice records a print and a numbered reprint.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ReportViewerLayoutTests
{
    private readonly AccountingFixture _fixture;

    public ReportViewerLayoutTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task The_daybook_lays_out_with_links_and_prints_are_counted()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        string journal = (await scope.ServiceProvider.GetRequiredService<IPostingEngine>().PostAsync(new JournalRequest
        {
            CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 3, 15), TransactionCurrency = Scenario.Usd, DocumentType = "Accrual", DocumentNumber = "LAY-1",
            Narrative = "Layout test", Lines =
            [
                new JournalLineRequest { AccountCode = "6100", Side = JournalSide.Debit, TransactionAmount = 70m },
                new JournalLineRequest { AccountCode = "2410", Side = JournalSide.Credit, TransactionAmount = 70m },
            ],
        })).Value.JournalNumber;

        IAuditedReportRunner runner = scope.ServiceProvider.GetRequiredService<IAuditedReportRunner>();
        Dictionary<string, object?> parameters = new() { ["FromDate"] = new DateOnly(2026, 3, 1), ["ToDate"] = new DateOnly(2026, 3, 31) };
        AuditedReport view = (await runner.RunAsync(companyId, SystemReports.JournalDaybookCode, parameters, ReportFormat.Layout, ReportRunPurpose.View, null)).Value;
        view.Document.ContentType.Should().Be("application/vnd.one4all.layout+json");
        RenderModel layout = ReportLayout.Deserialize(view.Document.Content);
        layout.Body.SelectMany(b => b.Cells).Should().Contain(c => c.DrillTarget == "Journal" && c.DrillKey == journal && c.Text == journal);
        ReportLayout.Paginate(layout).Should().NotBeEmpty();

        (await runner.RunAsync(companyId, SystemReports.JournalDaybookCode, parameters, ReportFormat.Layout, ReportRunPurpose.Print, null)).Value.Should()
            .Match<AuditedReport>(r => r.Purpose == ReportRunPurpose.Print);
        (await runner.RunAsync(companyId, SystemReports.JournalDaybookCode, parameters, ReportFormat.Layout, ReportRunPurpose.Print, null)).Value.Should()
            .Match<AuditedReport>(r => r.Purpose == ReportRunPurpose.Reprint && r.CopyNumber == 2);
    }
}
