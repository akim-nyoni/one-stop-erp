using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.JobCosting;
using One4All.Core.JobCosting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the contract WBS: structure rules, actual cost from captured cost heads, committed cost from open subcontract value under the
/// subcontract's own cost head, and the site's estimate to complete.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class JobWbsTests
{
    private readonly AccountingFixture _fixture;

    public JobWbsTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Breakdown_forecasts_cost_to_complete_per_activity()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer client = new(companyId, "WBSCLI", "Client", Scenario.Usd);
        Supplier subbie = new(companyId, "WBSSUB", "Roofers Ltd", Scenario.Usd);
        db.AddRange(client, subbie);
        await db.SaveChangesAsync();
        IJobCostingService jobs = scope.ServiceProvider.GetRequiredService<IJobCostingService>();
        long contract = (await jobs.CreateContractAsync(companyId, "WBS-1", "Clinic", client.Id, 100_000m, 60_000m, 5m, "4000")).Value;

        IJobWbsService wbs = scope.ServiceProvider.GetRequiredService<IJobWbsService>();
        (await wbs.AddElementAsync(companyId, contract, "1", "Shell", 0m, null)).IsSuccess.Should().BeTrue();
        (await wbs.AddElementAsync(companyId, contract, "1.1", "Walls", 20_000m, "Masonry")).IsSuccess.Should().BeTrue();
        (await wbs.AddElementAsync(companyId, contract, "1.2", "Roof", 15_000m, "Roofing")).IsSuccess.Should().BeTrue();
        (await wbs.AddElementAsync(companyId, contract, "2.1", "Fit-out", 5_000m, "Fitout")).Error.Code.Should().Be("Job.WbsNoParent");
        (await wbs.AddElementAsync(companyId, contract, "1.1.1", "Bricks", 1_000m, "Bricks")).Error.Code.Should().Be("Job.WbsParentIsActivity");
        (await wbs.AddElementAsync(companyId, contract, "1.3", "Gutters", 1_000m, "masonry")).Error.Code.Should().Be("Job.WbsCostHeadTaken");
        (await wbs.AddElementAsync(companyId, contract, "1", "Again", 0m, null)).Error.Code.Should().Be("Job.WbsExists");

        (await jobs.CaptureCostAsync(companyId, contract, "masonry", JobCostSource.Labour, 12_000m, new DateOnly(2026, 4, 1), "T1")).IsSuccess.Should().BeTrue();
        (await jobs.CaptureCostAsync(companyId, contract, "Prelims", JobCostSource.Labour, 800m, new DateOnly(2026, 4, 1), "T2")).IsSuccess.Should().BeTrue();
        (await scope.ServiceProvider.GetRequiredService<ISubcontractService>().LetAsync(companyId, contract, subbie.Id, "WBS-SC1", "Roof sheets", 14_000m, 5m, "Roofing")).IsSuccess.Should().BeTrue();
        (await wbs.SetEstimateAsync(companyId, contract, "1.1", 10_000m, new DateOnly(2026, 4, 2))).IsSuccess.Should().BeTrue();
        (await wbs.SetEstimateAsync(companyId, contract, "1", 1m, new DateOnly(2026, 4, 2))).Error.Code.Should().Be("Job.WbsEstimateInvalid");

        WbsBreakdown breakdown = (await wbs.BreakdownAsync(companyId, contract)).Value;
        breakdown.ContractBudget.Should().Be(60_000m);
        WbsLineOf(breakdown, "1.1").ForecastFinalCost.Should().Be(22_000m);
        WbsLineOf(breakdown, "1.2").Committed.Should().Be(14_000m);
        WbsLineOf(breakdown, "1.2").EstimateToComplete.Should().Be(1_000m);
        WbsLineOf(breakdown, "1").Variance.Should().Be(-2_000m);
        WbsLineOf(breakdown, string.Empty).Actual.Should().Be(800m);
        breakdown.Total.ForecastFinalCost.Should().Be(37_800m);

        // JC186 shows the same per cost head: budget, committed (the roof subcontract), cost to date and what is left.
        await scope.ServiceProvider.GetRequiredService<One4All.Infrastructure.Persistence.Seeding.ReportCatalogueSeeder>().SeedAsync();
        var report = await scope.ServiceProvider.GetRequiredService<One4All.Application.Abstractions.Reporting.IReportRunner>()
            .RenderAsync(companyId, "JC186", new Dictionary<string, object?>(), One4All.Application.Abstractions.Reporting.ReportFormat.Csv);
        report.IsSuccess.Should().BeTrue(report.IsFailure ? report.Error.Message : null);
        System.Text.Encoding.UTF8.GetString(report.Value.Content).Should().Contain("WBS-1,Clinic,Masonry,Labour,\"20,000.00\",0.00,\"12,000.00\",93.8%,8000")
            .And.Contain("WBS-1,Clinic,Roofing,-,\"15,000.00\",\"14,000.00\",0.00,0.0%,1000");
    }

    private static Application.JobCosting.WbsLine WbsLineOf(WbsBreakdown breakdown, string code) => breakdown.Lines.Single(l => l.Code == code);
}
