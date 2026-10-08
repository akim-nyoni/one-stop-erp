using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers the departmental income statement rendered as a catalogue report (GL-DEPT-IS).</summary>
[Collection(AccountingCollection.Name)]
public sealed class DepartmentalIncomeStatementReportTests
{
    private readonly AccountingFixture _fixture;

    public DepartmentalIncomeStatementReportTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Departmental_income_statement_renders_through_the_report_runner()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            await Scenario.DimensionsAsync(db, companyId, "NTH");
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
            await engine.PostAsync(new JournalRequest
            {
                CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 3, 10), TransactionCurrency = Scenario.Usd,
                EnforceManualJournalRules = false, DocumentType = "Journal", DocumentNumber = "N-SALE",
                Lines =
                [
                    new JournalLineRequest { AccountCode = "1000", Side = JournalSide.Debit, TransactionAmount = 1000m },
                    new JournalLineRequest { AccountCode = "4000", Side = JournalSide.Credit, TransactionAmount = 1000m, Department = "NTH" },
                ],
            });
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            Dictionary<string, object?> parameters = new() { ["Department"] = "NTH", ["FromDate"] = new DateOnly(2026, 3, 1), ["ToDate"] = new DateOnly(2026, 3, 31) };
            string html = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "GL-DEPT-IS", parameters, ReportFormat.Html)).Value.Content);

            html.Should().Contain("Departmental Income Statement");
            html.Should().Contain("Sales").And.Contain("1,000.00");
            html.Should().Contain("Net profit");
        }
    }
}
