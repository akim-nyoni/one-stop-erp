using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Inventory;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers bills of materials and assembly (brief §14 E 98–99): a run consumes components (standard or as keyed) through a clearing
/// account that nets to zero, receives the output at their cost, refuses stray usage and rolls back whole when stock is short;
/// INV98 rolls up the BOM cost and INV99 shows yield and usage variance.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class AssemblyTests
{
    private readonly AccountingFixture _fixture;

    public AssemblyTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Assembly_consumes_components_and_reports_yield()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        Item flour = new(companyId, "AFLOUR", "Flour"), sugar = new(companyId, "ASUGAR", "Sugar"), cake = new(companyId, "ACAKE", "Cake");
        Warehouse bakery = new(companyId, "ABAKE", "Bakery");
        db.AddRange(flour, sugar, cake, bakery);
        await db.SaveChangesAsync();
        BillOfMaterials bom = new(companyId, "cake-10", cake.Id, 10m);
        bom.AddLine(flour.Id, 5m, scrapPercent: 10m);
        bom.AddLine(sugar.Id, 1m);
        db.BillsOfMaterials.Add(bom);
        await db.SaveChangesAsync();

        IInventoryService inventory = scope.ServiceProvider.GetRequiredService<IInventoryService>();
        foreach ((Item item, decimal quantity, decimal cost) in new[] { (flour, 20m, 1m), (sugar, 5m, 4m) })
        {
            (await inventory.ReceiveAsync(new ReceiveStockRequest
            {
                CompanyId = companyId, ItemId = item.Id, WarehouseId = bakery.Id, Date = new DateOnly(2026, 3, 1), Quantity = quantity, UnitCost = cost, CreditAccountCode = "1100",
            })).IsSuccess.Should().BeTrue();
        }

        IAssemblyService assembly = scope.ServiceProvider.GetRequiredService<IAssemblyService>();
        AssemblyRequest Run(decimal planned, decimal? actual, params ComponentUsage[] usage) => new()
        {
            CompanyId = companyId, BillOfMaterialsId = bom.Id, WarehouseId = bakery.Id, Date = new DateOnly(2026, 3, 5), PlannedOutput = planned, ActualOutput = actual, Usage = usage,
            ClearingAccountCode = "2600",
        };
        (await assembly.AssembleAsync(Run(10m, 9m, new ComponentUsage(cake.Id, 1m)))).Error.Code.Should().Be("Inv.AssemblyUsage");
        AssemblyResult result = (await assembly.AssembleAsync(Run(10m, 9m, new ComponentUsage(flour.Id, 6m)))).Value;
        result.TotalCost.Should().Be(10m);
        result.UnitCost.Should().Be(1.1111m);
        (await assembly.AssembleAsync(Run(100m, null))).IsFailure.Should().BeTrue(); // not enough flour: nothing of the run survives
        (await db.AssemblyRuns.CountAsync(r => r.CompanyId == companyId)).Should().Be(1);

        (await db.StockBalances.AsNoTracking().SingleAsync(b => b.ItemId == cake.Id)).QuantityOnHand.Should().Be(9m);
        (await db.StockBalances.AsNoTracking().SingleAsync(b => b.ItemId == flour.Id)).QuantityOnHand.Should().Be(14m);
        decimal clearing = await db.Journals.AsNoTracking().Where(j => j.CompanyId == companyId).SelectMany(j => j.Lines).Where(l => l.AccountCode == "2600")
            .SumAsync(l => l.BaseDebit - l.BaseCredit);
        clearing.Should().Be(0m);

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        async Task<string> Render(string code)
        {
            Dictionary<string, object?> parameters = new() { ["FromDate"] = new DateOnly(2026, 1, 1), ["ToDate"] = new DateOnly(2026, 3, 31) };
            var rendered = await runner.RenderAsync(companyId, code, parameters, ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? $"{code}: {rendered.Error.Message}" : null);
            return Encoding.UTF8.GetString(rendered.Value.Content);
        }

        (await Render("INV98")).Should().Contain("CAKE-10,ACAKE Cake,AFLOUR Flour,5,10,0.55,1.00,0.55").And.Contain("CAKE-10,ACAKE Cake,ASUGAR Sugar,1,0,0.1,4.00,0.4")
            .And.Contain("Rolled-up cost per unit: 0.95 (batch of 10)");
        string yield = await Render("INV99");
        yield.Should().Contain($"{result.RunNumber},2026-03-05,ACAKE Cake,10 → 9 (90.0%),AFLOUR Flour,4.95,6,4.95,6.00,1.05")
            .And.Contain("ASUGAR Sugar,0.9,1,3.60,4.00,0.4");

        // Tests on the batch: a slump inside its tolerance passes, a weak 28-day cube fails and is listed for follow-up.
        IConcreteTestService tests = scope.ServiceProvider.GetRequiredService<IConcreteTestService>();
        (await tests.RecordAsync(companyId, new ConcreteTestEntry("NOPE", "S1", ConcreteTestKind.Slump, new DateOnly(2026, 3, 5), 80m, 75m, 25m))).Error.Code.Should().Be("Inv.BatchRunNotFound");
        (await tests.RecordAsync(companyId, new ConcreteTestEntry(result.RunNumber, "S1", ConcreteTestKind.Slump, new DateOnly(2026, 3, 4), 80m, 75m, 25m))).Error.Code.Should().Be("Inv.ConcreteTestDate");
        (await tests.RecordAsync(companyId, new ConcreteTestEntry(result.RunNumber, "S1", ConcreteTestKind.Slump, new DateOnly(2026, 3, 5), 80m, 75m, 25m))).Value.Passed.Should().BeTrue();
        (await tests.RecordAsync(companyId, new ConcreteTestEntry(result.RunNumber, "C1", ConcreteTestKind.Cube28Day, new DateOnly(2026, 4, 2), 21m, 25m))).Value.Passed.Should().BeFalse();
        (await tests.ForRunAsync(companyId, result.RunNumber)).Should().HaveCount(2);
        (await tests.FailuresAsync(companyId, new DateOnly(2026, 3, 1), new DateOnly(2026, 4, 30))).Should().ContainSingle().Which.SampleReference.Should().Be("C1");
    }
}
