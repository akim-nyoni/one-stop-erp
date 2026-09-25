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
public sealed class TrialBalanceMovementReportTests
{
    private readonly AccountingFixture _fixture;

    public TrialBalanceMovementReportTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Movement_report_shows_opening_period_movement_and_closing()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);

            IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
            // Brought forward in March: Dr 6100 700 / Cr 2410 700.
            (await engine.PostAsync(Journal(companyId, new DateOnly(2026, 3, 20), 700m))).IsSuccess.Should().BeTrue();
            // Movement in April: a further Dr 6100 300 / Cr 2410 300.
            (await engine.PostAsync(Journal(companyId, new DateOnly(2026, 4, 10), 300m))).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceMovementReport report = await gl.TrialBalanceMovementAsync(companyId, new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30));

            TrialBalanceMovementRow expense = report.Rows.Single(r => r.AccountCode == "6100");
            expense.Opening.Should().Be(700m);
            expense.Debit.Should().Be(300m);
            expense.Credit.Should().Be(0m);
            expense.Closing.Should().Be(1_000m);

            TrialBalanceMovementRow payable = report.Rows.Single(r => r.AccountCode == "2410");
            payable.Opening.Should().Be(-700m);
            payable.Credit.Should().Be(300m);
            payable.Closing.Should().Be(-1_000m);

            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            var parameters = new Dictionary<string, object?> { ["FromDate"] = new DateOnly(2026, 4, 1), ["ToDate"] = new DateOnly(2026, 4, 30) };
            string html = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "GL-TBMOV", parameters, ReportFormat.Html)).Value.Content);
            html.Should().Contain("6100").And.Contain("700.00").And.Contain("1,000.00");
        }
    }

    private static JournalRequest Journal(long companyId, DateOnly date, decimal amount) => new()
    {
        CompanyId = companyId, SourceModule = "GL", PostingDate = date, TransactionCurrency = Scenario.Usd,
        DocumentType = "Accrual", Narrative = "Movement test",
        Lines =
        [
            new JournalLineRequest { AccountCode = "6100", Side = JournalSide.Debit, TransactionAmount = amount },
            new JournalLineRequest { AccountCode = "2410", Side = JournalSide.Credit, TransactionAmount = amount },
        ],
    };
}
