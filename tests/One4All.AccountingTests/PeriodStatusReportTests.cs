using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class PeriodStatusReportTests
{
    private readonly AccountingFixture _fixture;

    public PeriodStatusReportTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Period_status_lists_periods_with_ledger_open_state()
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
            IReportDataProvider vertical = scope.ServiceProvider.GetServices<IReportDataProvider>().First(p => p.CanHandle("PeriodStatus"));
            var rows = await vertical.GetRowsAsync(companyId, "PeriodStatus", new Dictionary<string, object?>());
            rows.Should().NotBeEmpty();
            // The Scenario opens the ledgers for its periods, so at least one period is GL-open.
            rows.Should().Contain(r => (string)r["GL"]! == "Open");

            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            string html = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "SYS-PERIOD", new Dictionary<string, object?>(), ReportFormat.Html)).Value.Content);
            html.Should().Contain("Open");
        }
    }
}
