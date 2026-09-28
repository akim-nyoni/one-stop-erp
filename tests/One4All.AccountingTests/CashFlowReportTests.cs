using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers the Statement of Cash Flows rendered as a catalogue report (GL-CF) through the runner.</summary>
[Collection(AccountingCollection.Name)]
public sealed class CashFlowReportTests
{
    private readonly AccountingFixture _fixture;

    public CashFlowReportTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Cash_flow_statement_renders_through_the_report_runner()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            var tags = new Dictionary<string, CashFlowCategory>
            {
                ["1000"] = CashFlowCategory.CashAndEquivalents,
                ["4000"] = CashFlowCategory.Operating,
                ["1600"] = CashFlowCategory.Investing,
            };
            foreach (LedgerAccount a in await db.Accounts.Where(a => a.CompanyId == companyId && tags.Keys.Contains(a.AccountCode)).ToListAsync())
            {
                a.SetCashFlowCategory(tags[a.AccountCode]);
            }

            await db.SaveChangesAsync();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
            DateOnly date = new(2026, 3, 10);
            await engine.PostAsync(new JournalRequest
            {
                CompanyId = companyId, SourceModule = "GL", PostingDate = date, TransactionCurrency = Scenario.Usd,
                EnforceManualJournalRules = false, DocumentType = "Journal", DocumentNumber = "CF-1", Narrative = "Cash sale",
                Lines = [new JournalLineRequest { AccountCode = "1000", Side = JournalSide.Debit, TransactionAmount = 800m }, new JournalLineRequest { AccountCode = "4000", Side = JournalSide.Credit, TransactionAmount = 800m }],
            });
            await engine.PostAsync(new JournalRequest
            {
                CompanyId = companyId, SourceModule = "GL", PostingDate = date, TransactionCurrency = Scenario.Usd,
                EnforceManualJournalRules = false, DocumentType = "Journal", DocumentNumber = "CF-2", Narrative = "Buy vehicle",
                Lines = [new JournalLineRequest { AccountCode = "1600", Side = JournalSide.Debit, TransactionAmount = 300m }, new JournalLineRequest { AccountCode = "1000", Side = JournalSide.Credit, TransactionAmount = 300m }],
            });
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            Dictionary<string, object?> parameters = new() { ["FromDate"] = new DateOnly(2026, 3, 1), ["ToDate"] = new DateOnly(2026, 3, 31) };
            string html = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "GL-CF", parameters, ReportFormat.Html)).Value.Content);

            html.Should().Contain("Statement of Cash Flows");
            html.Should().Contain("Operating activities").And.Contain("800.00");
            html.Should().Contain("Investing activities").And.Contain("-300.00");
            html.Should().Contain("Net change in cash").And.Contain("500.00");
        }
    }
}
