using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class FinancialSnapshotReportTests
{
    private readonly AccountingFixture _fixture;

    public FinancialSnapshotReportTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Snapshot_summarises_balance_sheet_and_ytd_profit()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);

            // A clean cash sale: Dr Bank 1000 / Cr Sales 1000.
            IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
            var posted = await engine.PostAsync(new JournalRequest
            {
                CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 6, 15), TransactionCurrency = Scenario.Usd,
                DocumentType = "Sale", Narrative = "Cash sale",
                Lines =
                [
                    new JournalLineRequest { AccountCode = "1100", Side = JournalSide.Debit, TransactionAmount = 1_000m },
                    new JournalLineRequest { AccountCode = "4000", Side = JournalSide.Credit, TransactionAmount = 1_000m },
                ],
            });
            posted.IsSuccess.Should().BeTrue(posted.IsFailure ? posted.Error.ToString() : null);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            FinancialSnapshotReport snap = await gl.FinancialSnapshotAsync(companyId, new DateOnly(2026, 6, 30));

            snap.Rows.Single(r => r.Metric == "Total Assets").Amount.Should().Be(1_000m);
            snap.Rows.Single(r => r.Metric == "Income (YTD)").Amount.Should().Be(1_000m);
            snap.Rows.Single(r => r.Metric == "Net Profit (YTD)").Amount.Should().Be(1_000m);
            snap.Rows.Single(r => r.Metric == "Total Equity").Amount.Should().Be(1_000m); // current-year earnings

            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            var parameters = new Dictionary<string, object?> { ["AsAtDate"] = new DateOnly(2026, 6, 30) };
            string html = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "GL-SNAP", parameters, ReportFormat.Html)).Value.Content);
            html.Should().Contain("Net Profit (YTD)").And.Contain("1,000.00");
        }
    }
}
