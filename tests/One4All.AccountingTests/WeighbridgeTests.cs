using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.JobCosting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Inventory;
using One4All.Core.JobCosting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using One4All.Reporting;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers weighbridge operations (brief §14 L): outbound loads on stored tare and on a manual weight (refused without a second
/// person), an overload, haulage by zone with a minimum, stock issued from the stockpile, an open inbound ticket that cannot complete
/// with no net load, a concrete batch delivered over the bridge; WB198–205 read them back.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class WeighbridgeTests
{
    private readonly AccountingFixture _fixture;

    public WeighbridgeTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Weighbridge_tickets_drive_stock_haulage_and_reports()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        Customer builder = new(companyId, "WBUILD", "Builder", Scenario.Usd);
        db.Customers.Add(builder);
        Item sand = new(companyId, "WSAND", "Sand") { CostOfSalesAccountCode = "5000" }, cement = new(companyId, "WCEM", "Cement"), concrete = new(companyId, "WCONC", "Concrete");
        Warehouse pile = new(companyId, "WPILE", "Sand pile"), plant = new(companyId, "WPLANT", "Batching plant");
        db.AddRange(sand, cement, concrete, pile, plant,
            new WeighbridgeVehicle(companyId, "abc123") { Haulier = "Haul Co", StoredTareKg = 10_000m, MaxGrossKg = 30_000m },
            new HaulageZone(companyId, "Z1", "Town", 0m, 10m, 2m, 30m), new HaulageZone(companyId, "Z2", "Country", 10m, 50m, 4m, 50m));
        await db.SaveChangesAsync();
        IWeighbridgeService register = scope.ServiceProvider.GetRequiredService<IWeighbridgeService>();
        (await register.SaveVehicleAsync(companyId, "xyz789", "Other Haul", 9_000m, 28_000m)).IsSuccess.Should().BeTrue();
        (await register.SaveVehicleAsync(companyId, "XYZ789", "Other Haul", 30_000m, 28_000m)).Error.Code.Should().Be("Wb.Vehicle");
        (await register.SaveVehicleAsync(companyId, "ABC123", "Haul Co", 10_500m, 30_000m, active: false)).IsSuccess.Should().BeTrue("an existing vehicle is updated in place");
        (await db.WeighbridgeVehicles.AsNoTracking().Where(v => v.CompanyId == companyId).ToListAsync()).Should().HaveCount(2);
        (await register.SaveVehicleAsync(companyId, "ABC123", "Haul Co", 10_000m, 30_000m)).IsSuccess.Should().BeTrue("back in use with the original tare for the rest of the test");
        IInventoryService inventory = scope.ServiceProvider.GetRequiredService<IInventoryService>();
        foreach ((Item item, Warehouse warehouse, decimal quantity, decimal cost) in new[] { (sand, pile, 100m, 5m), (cement, plant, 10m, 10m) })
        {
            (await inventory.ReceiveAsync(new ReceiveStockRequest
            {
                CompanyId = companyId, ItemId = item.Id, WarehouseId = warehouse.Id, Date = new DateOnly(2026, 3, 1), Quantity = quantity, UnitCost = cost, CreditAccountCode = "1100",
            })).IsSuccess.Should().BeTrue();
        }

        BillOfMaterials mix = new(companyId, "conc-mix", concrete.Id, 1m);
        mix.AddLine(cement.Id, 1.5m);
        db.BillsOfMaterials.Add(mix);
        await db.SaveChangesAsync();
        AssemblyResult batch = (await scope.ServiceProvider.GetRequiredService<IAssemblyService>().AssembleAsync(new AssemblyRequest
        {
            CompanyId = companyId, BillOfMaterialsId = mix.Id, WarehouseId = plant.Id, Date = new DateOnly(2026, 3, 11), PlannedOutput = 4m, Usage = [new ComponentUsage(cement.Id, 6.5m)],
            ClearingAccountCode = "2600",
        })).Value;

        IWeighbridgeService bridge = scope.ServiceProvider.GetRequiredService<IWeighbridgeService>();
        FirstWeighRequest Out(string vehicle, decimal? weight, string? site, decimal distance, bool manual = false, long? authoriser = null) => new()
        {
            CompanyId = companyId, Direction = WeighDirection.Outbound, Vehicle = vehicle, ItemId = sand.Id, CubicMetresPerTonne = 0.6m, CustomerId = builder.Id, Driver = vehicle == "ABC123" ? "John" : "Mary",
            Site = site, WarehouseId = pile.Id, DistanceKm = distance, Date = new DateOnly(2026, 3, 10), WeightKg = weight, OperatorUserId = 1, IsManual = manual,
            ManualReason = manual ? "Scale offline" : null, AuthorisedByUserId = authoriser,
        };
        SecondWeighRequest Second(long ticket, decimal weight) => new() { CompanyId = companyId, TicketId = ticket, WeightKg = weight, OperatorUserId = 1 };

        (long first, string firstNumber) = (await bridge.FirstWeighAsync(Out("ABC123", null, "Site A", 15m))).Value;
        WeighbridgeCompletion loaded = (await bridge.SecondWeighAsync(Second(first, 32_000m))).Value;
        loaded.Should().Be(new WeighbridgeCompletion(22m, 13.2m, 88m, Overloaded: true));
        (await bridge.SecondWeighAsync(Second(first, 32_000m))).Error.Code.Should().Be("Wb.TicketComplete");

        (await bridge.FirstWeighAsync(Out("XYZ9", 9_000m, "Plot 2", 5m, manual: true))).Error.Code.Should().Be("Wb.ManualAuthorisation");
        (await bridge.FirstWeighAsync(Out("XYZ9", 9_000m, "Plot 2", 5m, manual: true, authoriser: 1))).Error.Code.Should().Be("Wb.ManualAuthorisation");
        (long manualTicket, _) = (await bridge.FirstWeighAsync(Out("XYZ9", 9_000m, "Plot 2", 5m, manual: true, authoriser: 2))).Value;
        (await bridge.SecondWeighAsync(Second(manualTicket, 14_000m))).Value.HaulageCharge.Should().Be(30m); // 5 t × 2 = 10, minimum 30

        (long inbound, _) = (await bridge.FirstWeighAsync(new FirstWeighRequest
        {
            CompanyId = companyId, Direction = WeighDirection.Inbound, Vehicle = "TRK7", ItemId = sand.Id, CubicMetresPerTonne = 0.6m, Date = new DateOnly(2026, 3, 10), WeightKg = 25_000m,
        })).Value;
        (await bridge.SecondWeighAsync(Second(inbound, 26_000m))).Error.Code.Should().Be("Wb.NoNetWeight");
        (await db.WeighbridgeTickets.AsNoTracking().SingleAsync(t => t.Id == inbound)).IsComplete.Should().BeFalse();

        (long concreteLoad, _) = (await bridge.FirstWeighAsync(new FirstWeighRequest
        {
            CompanyId = companyId, Direction = WeighDirection.Outbound, Vehicle = "MIX1", ItemId = concrete.Id, CubicMetresPerTonne = 0.4m, CustomerId = builder.Id, Site = "Site A",
            BatchRunNumber = batch.RunNumber, Date = new DateOnly(2026, 3, 11), WeightKg = 10_000m,
        })).Value;
        (await bridge.SecondWeighAsync(Second(concreteLoad, 20_000m))).Value.NetCubicMetres.Should().Be(4m);

        (await db.StockBalances.AsNoTracking().SingleAsync(b => b.ItemId == sand.Id && b.WarehouseId == pile.Id)).QuantityOnHand.Should().Be(73m);

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        async Task<string> Render(string code)
        {
            Dictionary<string, object?> parameters = new() { ["FromDate"] = new DateOnly(2026, 1, 1), ["ToDate"] = new DateOnly(2026, 12, 31) };
            var rendered = await runner.RenderAsync(companyId, code, parameters, ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? $"{code}: {rendered.Error.Message}" : null);
            return Encoding.UTF8.GetString(rendered.Value.Content);
        }

        (await Render("WB198")).Should().Contain($"{firstNumber},2026-03-10,ABC123,John,Builder,Sand,").And.Contain(",32000,10000,0,13.2,22").And.Contain(",TRK7,,,Sand,");
        (await Render("WB199")).Should().Contain("2026-03-10,Sand,Builder,ABC123,John,1,13.2,22").And.Contain("2026-03-10,Sand,Builder,XYZ9,Mary,1,3,5");
        string vehicles = await Render("WB200");
        vehicles.Should().Contain("ABC123,Haul Co,1,0,0,0,1,22").And.Contain("TRK7,—,0,—,—,1,0,0");
        (await Render("WB201")).Should().Contain("Builder,Site A,2,32,17.2,88").And.Contain("Builder,Plot 2,1,5,3,30");
        (await Render("WB202")).Should().Contain(",XYZ9,").And.Contain("Scale offline,5");
        (await Render("WB203")).Should().Contain("WSAND Sand,WPILE,0,0,100,27,0,73");
        (await Render("WB204")).Should().Contain($"{batch.RunNumber},2026-03-11,CONC-MIX,4,4,1,65.00,5");
        (await Render("WB205")).Should().Contain("Z1,Town,1,5,5,1.2,30").And.Contain("Z2,Country,1,22,15,0.2667,88");

        (await Render("SYS241")).Should().Contain(",Weighbridge,Manual weight,").And.Contain(",Scale offline");

        foreach (string code in WeighbridgeReports.All().Select(r => r.Code))
        {
            await Render(code);
        }
    }
}
