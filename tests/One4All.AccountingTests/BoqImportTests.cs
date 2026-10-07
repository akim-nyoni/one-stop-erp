using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.JobCosting;
using One4All.Core.Payroll;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the BOQ import: a preview lists every line with its error without saving anything, an import with any error saves nothing, a clean file
/// (header row and quoted descriptions allowed) imports whole, and the BOQ budget by cost head sits beside the value measured and the cost captured.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class BoqImportTests
{
    private readonly AccountingFixture _fixture;

    public BoqImportTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Boq_files_are_previewed_imported_whole_and_budgeted_by_cost_head()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer client = new(companyId, "BQ-CLI", "Client", Scenario.Usd);
        Employee mason = new(companyId, "BQ-E1", "Mason", 3_000m, new DateOnly(2025, 1, 1));
        db.AddRange(client, mason);
        await db.SaveChangesAsync();
        long contract = (await scope.ServiceProvider.GetRequiredService<IJobCostingService>().CreateContractAsync(companyId, "BQ-1", "Clinic", client.Id, 50_000m, 40_000m, 5m, "4300")).Value;
        IContractControlService control = scope.ServiceProvider.GetRequiredService<IContractControlService>();
        (await control.AddBoqItemAsync(companyId, contract, "A1", "Site clearance", "m2", 500m, 2m)).IsSuccess.Should().BeTrue();

        const string bad = "Item,Description,Unit,Quantity,Rate,Head\n"
            + "B1,\"Excavation, trenches\",m3,100,20,Earthworks\n"
            + "A1,Clearance again,m2,10,2,\n"
            + "B2,Concrete,m3,-4,150,Concrete\n"
            + "B1,Excavation repeat,m3,1,20,Earthworks\n"
            + "B3,Blockwork\n";
        BoqImportPreview preview = (await control.PreviewBoqImportAsync(companyId, contract, bad)).Value;
        preview.Lines.Select(l => l.Error).Should().Equal(null, "Item A1 is already on the contract.", "Quantity '-4' must be a positive number.",
            "Item B1 appears more than once in the file.", "Needs item, description, unit, quantity and rate.");
        preview.Lines[0].Description.Should().Be("Excavation, trenches");
        (await control.ImportBoqAsync(companyId, contract, bad)).Error.Code.Should().Be("Job.BoqErrors");
        db.BoqItems.Count(b => b.JobContractId == contract).Should().Be(1);

        const string good = "Item,Description,Unit,Quantity,Rate,Head\nB1,\"Excavation, trenches\",m3,100,20,Earthworks\nB2,Concrete,m3,40,150,Concrete\nB3,Blockwork,m2,200,12.5,Concrete\n";
        (await control.PreviewBoqImportAsync(companyId, contract, good)).Value.TotalValue.Should().Be(10_500m);
        (await control.ImportBoqAsync(companyId, contract, good)).Value.Should().Be(3);

        // Measure half the excavation and book labour to Earthworks: the heads show budget, measured value and cost side by side.
        long b1 = db.BoqItems.Single(b => b.JobContractId == contract && b.ItemCode == "B1").Id;
        (await control.MeasureAsync(companyId, b1, 50m, new DateOnly(2026, 4, 1))).IsSuccess.Should().BeTrue();
        (await control.RecordTimeAsync(companyId, contract, mason.Id, new DateOnly(2026, 4, 1), 8m, 25m, "Earthworks")).IsSuccess.Should().BeTrue();
        IReadOnlyList<BoqCostHeadRow> heads = (await control.BoqByCostHeadAsync(companyId, contract)).Value;
        heads.Should().Equal(
            new BoqCostHeadRow("(none)", 1_000m, 0m, 0m),
            new BoqCostHeadRow("Concrete", 8_500m, 0m, 0m),
            new BoqCostHeadRow("Earthworks", 2_000m, 1_000m, 200m));
    }
}
