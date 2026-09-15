using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Common;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class ReportRenderingTests
{
    private readonly AccountingFixture _fixture;

    public ReportRenderingTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Trial_balance_renders_to_html_and_pdf_and_csv()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            companyId = await Scenario.SeedAsync(scope.ServiceProvider.GetRequiredService<One4AllDbContext>());
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
            await engine.PostAsync(new JournalRequest
            {
                CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 3, 15),
                TransactionCurrency = Scenario.Usd, EnforceManualJournalRules = true,
                Lines =
                [
                    new JournalLineRequest { AccountCode = "1000", Side = JournalSide.Debit, TransactionAmount = 100m },
                    new JournalLineRequest { AccountCode = "4000", Side = JournalSide.Credit, TransactionAmount = 100m },
                ],
            });
        }

        await using AsyncServiceScope render = provider.CreateAsyncScope();
        IFinancialReportRenderer renderer = render.ServiceProvider.GetRequiredService<IFinancialReportRenderer>();

        Result<RenderedDocument> html = await renderer.RenderTrialBalanceAsync(companyId, new DateOnly(2026, 3, 31), ReportFormat.Html);
        html.IsSuccess.Should().BeTrue();
        string htmlText = Encoding.UTF8.GetString(html.Value.Content);
        htmlText.Should().Contain("Trial Balance as at 2026-03-31");
        htmlText.Should().Contain("Cash on Hand");
        htmlText.Should().Contain("100.00");

        Result<RenderedDocument> pdf = await renderer.RenderTrialBalanceAsync(companyId, new DateOnly(2026, 3, 31), ReportFormat.Pdf);
        pdf.Value.ContentType.Should().Be("application/pdf");
        Encoding.ASCII.GetString(pdf.Value.Content, 0, 5).Should().Be("%PDF-");

        Result<RenderedDocument> csv = await renderer.RenderTrialBalanceAsync(companyId, new DateOnly(2026, 3, 31), ReportFormat.Csv);
        Encoding.UTF8.GetString(csv.Value.Content).Should().Contain("AccountCode,AccountName,Debit,Credit");
    }

    [SkippableFact]
    public async Task Report_catalogue_seeder_persists_system_definitions()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        await new ReportCatalogueSeeder(db).SeedAsync();

        (await db.ReportDefinitions.AnyAsync(r => r.Code == "GL-TB")).Should().BeTrue();

        // Second run is idempotent.
        int second = await new ReportCatalogueSeeder(db).SeedAsync();
        second.Should().Be(0);
    }
}
