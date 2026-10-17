using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Budgeting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Core.Budgeting;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Thirteenth audit #18 (brief §14 report 11): the management accounts by department and cost centre against budget, with variance, YTD and forecast.</summary>
[Collection(AccountingCollection.Name)]
public sealed class ManagementByCostCentreTests
{
    private readonly AccountingFixture _fixture;

    public ManagementByCostCentreTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Actuals_meet_the_active_budget_by_department_and_cost_centre()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        db.CostCentres.Add(new CostCentre(companyId, "HQ", "Head Office"));
        db.Departments.Add(new Department(companyId, "ADM", "Administration"));
        await db.SaveChangesAsync();
        long yearId = await db.FiscalYears.Where(y => y.CompanyId == companyId).OrderBy(y => y.StartDate).Select(y => y.Id).FirstAsync();
        DateOnly yearStart = await db.FiscalYears.Where(y => y.Id == yearId).Select(y => y.StartDate).FirstAsync();
        IBudgetingService budgeting = scope.ServiceProvider.GetRequiredService<IBudgetingService>();
        long budgetId = (await budgeting.CreateBudgetAsync(companyId, yearId, "MCC", BudgetType.OriginalApproved, "ADM",
            [new NewBudgetLine("6800", 1, 1_000m, "HQ"), new NewBudgetLine("6800", 2, 1_000m, "HQ"), new NewBudgetLine("6800", 3, 1_000m, "HQ")])).Value;
        (await budgeting.ActivateAsync(companyId, budgetId)).IsSuccess.Should().BeTrue();

        // 800 spent in the first month against a budget of 1,000.
        var posted = await scope.ServiceProvider.GetRequiredService<IPostingEngine>().PostAsync(new JournalRequest
        {
            CompanyId = companyId, SourceModule = "GL", PostingDate = yearStart.AddDays(9), TransactionCurrency = Scenario.Usd, DocumentNumber = "MCC-1",
            Lines =
            [
                new JournalLineRequest { AccountCode = "6800", Side = JournalSide.Debit, TransactionAmount = 800m, Department = "ADM", CostCentre = "HQ" },
                new JournalLineRequest { AccountCode = "1000", Side = JournalSide.Credit, TransactionAmount = 800m },
            ],
        });
        posted.IsSuccess.Should().BeTrue(posted.IsFailure ? posted.Error.ToString() : null);

        var rendered = await scope.ServiceProvider.GetRequiredService<IReportRunner>().RenderAsync(companyId, "GL11-CC",
            new Dictionary<string, object?> { ["FromDate"] = yearStart, ["ToDate"] = yearStart.AddMonths(1).AddDays(-1) }, ReportFormat.Csv);
        rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? rendered.Error.Message : null);
        string csv = Encoding.UTF8.GetString(rendered.Value.Content);
        string row = csv.Split('\n').Single(l => l.StartsWith("ADM,HQ,6800,", StringComparison.Ordinal)).TrimEnd('\r');
        decimal[] values = [.. row.Split(',').Skip(4).Where(v => !v.EndsWith('%')).Select(v => decimal.Parse(v, System.Globalization.CultureInfo.InvariantCulture))];
        values.Should().Equal(new[] { 800m, 1000m, 200m, 800m, 1000m, 3000m, 2800m }, "actual, budget, favourable variance, YTD actual and budget, full-year budget, forecast 800 + 2,000");
        row.Should().Contain(",+20.0%,");
    }
}
