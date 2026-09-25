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
public sealed class JournalDaybookReportTests
{
    private readonly AccountingFixture _fixture;

    public JournalDaybookReportTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Daybook_lists_posted_journals_in_range_with_totals()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);

            IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
            var inMarch = await engine.PostAsync(new JournalRequest
            {
                CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 3, 15), TransactionCurrency = Scenario.Usd,
                DocumentType = "Accrual", DocumentNumber = "ACC-1", Narrative = "March salary accrual",
                Lines =
                [
                    new JournalLineRequest { AccountCode = "6100", Side = JournalSide.Debit, TransactionAmount = 700m },
                    new JournalLineRequest { AccountCode = "2410", Side = JournalSide.Credit, TransactionAmount = 700m },
                ],
            });
            inMarch.IsSuccess.Should().BeTrue(inMarch.IsFailure ? inMarch.Error.ToString() : null);

            // Outside the reporting window (April) — must be excluded.
            var inApril = await engine.PostAsync(new JournalRequest
            {
                CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 4, 2), TransactionCurrency = Scenario.Usd,
                DocumentType = "Accrual", DocumentNumber = "ACC-2", Narrative = "April only accrual",
                Lines =
                [
                    new JournalLineRequest { AccountCode = "6100", Side = JournalSide.Debit, TransactionAmount = 999m },
                    new JournalLineRequest { AccountCode = "2410", Side = JournalSide.Credit, TransactionAmount = 999m },
                ],
            });
            inApril.IsSuccess.Should().BeTrue(inApril.IsFailure ? inApril.Error.ToString() : null);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            JournalDaybookReport report = await gl.JournalDaybookAsync(companyId, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));
            report.Lines.Should().Contain(l => l.DocumentNumber == "ACC-1" && l.Amount == 700m && l.SourceModule == "GL");
            report.Lines.Should().NotContain(l => l.DocumentNumber == "ACC-2");

            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            var parameters = new Dictionary<string, object?> { ["FromDate"] = new DateOnly(2026, 3, 1), ["ToDate"] = new DateOnly(2026, 3, 31) };
            string html = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "GL-DAYBOOK", parameters, ReportFormat.Html)).Value.Content);
            html.Should().Contain("ACC-1").And.Contain("March salary accrual").And.Contain("700.00");
            html.Should().NotContain("April only accrual");
        }
    }
}
