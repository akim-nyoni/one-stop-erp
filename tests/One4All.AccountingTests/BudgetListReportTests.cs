using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Budgeting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Budgeting;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class BudgetListReportTests
{
    private readonly AccountingFixture _fixture;

    public BudgetListReportTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Budget_listing_shows_versions_with_annual_total_and_active_flag()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long fiscalYearId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            fiscalYearId = await db.FiscalYears.Where(y => y.CompanyId == companyId).Select(y => y.Id).FirstAsync();

            IBudgetingService budgeting = scope.ServiceProvider.GetRequiredService<IBudgetingService>();
            // 1000 (P1) + 1500 (P2) = 2500 annual total.
            var created = await budgeting.CreateBudgetAsync(companyId, fiscalYearId, "2026 Original", BudgetType.OriginalApproved, "ADMIN",
                [new NewBudgetLine("6100", 1, 1000m), new NewBudgetLine("6100", 2, 1500m)]);
            created.IsSuccess.Should().BeTrue(created.IsFailure ? created.Error.ToString() : null);
            (await budgeting.ActivateAsync(companyId, created.Value)).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            string html = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "BUD-LIST", new Dictionary<string, object?>(), ReportFormat.Html)).Value.Content);
            html.Should().Contain("2026 Original").And.Contain("ADMIN").And.Contain("2,500.00").And.Contain("Yes");
        }
    }
}
