using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers the trial balance by type rendered as a catalogue report (GL-TBTYPE).</summary>
[Collection(AccountingCollection.Name)]
public sealed class TrialBalanceByTypeReportTests
{
    private readonly AccountingFixture _fixture;

    public TrialBalanceByTypeReportTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Summary_trial_balance_renders_through_the_report_runner()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
            await engine.PostAsync(new JournalRequest
            {
                CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 3, 10), TransactionCurrency = Scenario.Usd,
                EnforceManualJournalRules = false, DocumentType = "Journal", DocumentNumber = "TBTR-1",
                Lines = [new JournalLineRequest { AccountCode = "1000", Side = JournalSide.Debit, TransactionAmount = 900m }, new JournalLineRequest { AccountCode = "4000", Side = JournalSide.Credit, TransactionAmount = 900m }],
            });
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            Dictionary<string, object?> parameters = new() { ["AsAtDate"] = new DateOnly(2026, 3, 31) };
            string html = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "GL-TBTYPE", parameters, ReportFormat.Html)).Value.Content);

            html.Should().Contain("Trial Balance by Type");
            html.Should().Contain("Asset").And.Contain("Income").And.Contain("900.00");
        }
    }
}
