using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.JobCosting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Payroll;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers contract controls (brief §14 K 191, 193, 196): an approved variation moves value and budget, a rejected one does not and
/// neither can be decided twice; BOQ lines are measured forward only; timesheets cost labour into the job and refuse more than 24
/// hours a day; JC191, JC193 and JC196 read them back.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ContractControlTests
{
    private readonly AccountingFixture _fixture;

    public ContractControlTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Variations_boq_and_timesheets_are_controlled_and_reported()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        Customer client = new(companyId, "CCCLI", "Client", Scenario.Usd);
        Employee ann = new(companyId, "JC-E1", "Ann Mason", 3_000m, new DateOnly(2025, 1, 1)), ben = new(companyId, "JC-E2", "Ben Fixer", 3_000m, new DateOnly(2025, 1, 1));
        db.AddRange(client, ann, ben);
        await db.SaveChangesAsync();
        long contract = (await scope.ServiceProvider.GetRequiredService<IJobCostingService>().CreateContractAsync(companyId, "CC-1", "Depot", client.Id, 100_000m, 80_000m, 10m, "4300")).Value;

        IContractControlService control = scope.ServiceProvider.GetRequiredService<IContractControlService>();
        long vo1 = (await control.SubmitVariationAsync(companyId, contract, "vo-1", "Extra room", 10_000m, 6_000m, new DateOnly(2026, 3, 1))).Value;
        (await control.SubmitVariationAsync(companyId, contract, "VO-1", "Again", 1m, 1m, new DateOnly(2026, 3, 1))).Error.Code.Should().Be("Job.VariationExists");
        long vo2 = (await control.SubmitVariationAsync(companyId, contract, "VO-2", "Canopy", 5_000m, 3_000m, new DateOnly(2026, 3, 2))).Value;
        long vo3 = (await control.SubmitVariationAsync(companyId, contract, "VO-3", "Paving", 2_000m, 1_000m, new DateOnly(2026, 3, 10))).Value;
        (await control.DecideVariationAsync(companyId, vo1, true, new DateOnly(2026, 3, 5), 2, "Signed by client")).IsSuccess.Should().BeTrue();
        (await control.DecideVariationAsync(companyId, vo2, false, new DateOnly(2026, 3, 6), 2, null)).IsSuccess.Should().BeTrue();
        (await control.DecideVariationAsync(companyId, vo1, false, new DateOnly(2026, 3, 7), 2, null)).Error.Code.Should().Be("Job.VariationDecided");
        (await control.DecideVariationAsync(companyId, vo3, true, new DateOnly(2026, 3, 9), 2, null)).Error.Code.Should().Be("Job.DecisionBeforeSubmission");
        var revised = await db.JobContracts.AsNoTracking().SingleAsync(c => c.Id == contract);
        (revised.ContractValue, revised.BudgetCost).Should().Be((110_000m, 86_000m));

        long excavation = (await control.AddBoqItemAsync(companyId, contract, "b1", "Excavation", "m3", 100m, 20m)).Value;
        (await control.AddBoqItemAsync(companyId, contract, "B2", "Concrete", "m3", 40m, 150m)).IsSuccess.Should().BeTrue();
        (await control.AddBoqItemAsync(companyId, contract, "B2", "Concrete", "m3", 40m, 150m)).Error.Code.Should().Be("Job.BoqExists");
        (await control.MeasureAsync(companyId, excavation, 60m, new DateOnly(2026, 3, 15))).IsSuccess.Should().BeTrue();
        (await control.MeasureAsync(companyId, excavation, 110m, new DateOnly(2026, 3, 31))).IsSuccess.Should().BeTrue();
        (await control.MeasureAsync(companyId, excavation, 90m, new DateOnly(2026, 3, 20))).Error.Code.Should().Be("Job.MeasureOutOfOrder");

        (await control.RecordTimeAsync(companyId, contract, ann.Id, new DateOnly(2026, 3, 2), 8m, 10m)).IsSuccess.Should().BeTrue();
        (await control.RecordTimeAsync(companyId, contract, ann.Id, new DateOnly(2026, 3, 3), 6m, 10m)).IsSuccess.Should().BeTrue();
        (await control.RecordTimeAsync(companyId, contract, ben.Id, new DateOnly(2026, 3, 2), 8m, 12m)).IsSuccess.Should().BeTrue();
        (await control.RecordTimeAsync(companyId, contract, ann.Id, new DateOnly(2026, 3, 2), 20m, 10m)).Error.Code.Should().Be("Job.TimeOverbooked");
        (await db.JobContracts.AsNoTracking().Include(c => c.Costs).SingleAsync(c => c.Id == contract)).ActualCost.Should().Be(236m);

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        async Task<string> Render(string code)
        {
            var rendered = await runner.RenderAsync(companyId, code, new Dictionary<string, object?> { ["FromDate"] = new DateOnly(2026, 3, 1), ["ToDate"] = new DateOnly(2026, 3, 31) }, ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? $"{code}: {rendered.Error.Message}" : null);
            return Encoding.UTF8.GetString(rendered.Value.Content);
        }

        (await Render("JC191")).Should().Contain("CC-1,VO-1,Extra room,2026-03-01,Approved,2026-03-05,\"6,000.00\",10000").And.Contain("CC-1,VO-2,Canopy,2026-03-02,Rejected,2026-03-06,\"3,000.00\",5000")
            .And.Contain("CC-1,VO-3,Paving,2026-03-10,Submitted,—,\"1,000.00\",2000");
        (await Render("JC193")).Should().Contain("CC-1,JC-E1 Ann Mason,Labour,2,14,10.00,140").And.Contain("CC-1,JC-E2 Ben Fixer,Labour,1,8,12.00,96");
        (await Render("JC196")).Should().Contain("CC-1,B1,Excavation,m3,100,110,10,110.0%,2200").And.Contain("CC-1,B2,Concrete,m3,40,0,-40,0.0%,0");
    }
}
