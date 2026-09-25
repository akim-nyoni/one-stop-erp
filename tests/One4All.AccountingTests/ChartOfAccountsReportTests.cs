using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class ChartOfAccountsReportTests
{
    private readonly AccountingFixture _fixture;

    public ChartOfAccountsReportTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Chart_of_accounts_lists_accounts_with_type_and_control_flag()
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
            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            ChartOfAccountsReport report = await gl.ChartOfAccountsAsync(companyId);

            report.Rows.Should().Contain(r => r.AccountCode == "1100" && r.Type == "Asset");
            // The AR control account (1200) is flagged as a control account.
            report.Rows.Should().Contain(r => r.IsControl);
            // Ordered by code ascending.
            report.Rows.Select(r => r.AccountCode).Should().BeInAscendingOrder(StringComparer.Ordinal);

            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            string html = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "GL-COA", new Dictionary<string, object?>(), ReportFormat.Html)).Value.Content);
            html.Should().Contain("1100").And.Contain("Bank").And.Contain("Sales");
        }
    }
}
