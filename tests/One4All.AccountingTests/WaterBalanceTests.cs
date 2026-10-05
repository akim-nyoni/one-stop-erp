using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Municipal;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Municipal;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the water balance and meter exceptions (brief §14 I 157–158): bulk water into a zone against what its meters billed, and meters
/// that are unread, estimated or flagged as tampered.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class WaterBalanceTests
{
    private readonly AccountingFixture _fixture;

    public WaterBalanceTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Water_losses_and_meter_exceptions_are_reported()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        Tariff water = new(companyId, "WB-WATER", "Water", ServiceType.Water, TariffType.PerUnit, new DateOnly(2026, 1, 1), "4000") { PerUnitRate = 1m };
        Customer owner = new(companyId, "WB-OWN", "Owner", Scenario.Usd);
        db.AddRange(water, owner);
        await db.SaveChangesAsync();

        IMeterReadingService meters = scope.ServiceProvider.GetRequiredService<IMeterReadingService>();
        long a = (await meters.InstallAsync(companyId, $"WB-A-{companyId}", 5, 1m, 0m, new DateOnly(2026, 1, 1))).Value;
        long b = (await meters.InstallAsync(companyId, $"WB-B-{companyId}", 5, 1m, 0m, new DateOnly(2026, 1, 1))).Value;
        long c = (await meters.InstallAsync(companyId, $"WB-C-{companyId}", 5, 1m, 0m, new DateOnly(2025, 10, 1))).Value;
        Property one = new(companyId, "WB-1", "Ward 1", owner.Id) { Zone = "Z1" }, two = new(companyId, "WB-2", "Ward 1", owner.Id) { Zone = "Z1" };
        one.AddService(ServiceType.Water, water.Id, new DateOnly(2026, 1, 1)).MeterId = a;
        two.AddService(ServiceType.Water, water.Id, new DateOnly(2026, 1, 1)).MeterId = b;
        db.Properties.AddRange(one, two);
        db.BulkSupplyReadings.Add(new BulkSupplyReading(companyId, "Z1", new DateOnly(2026, 2, 1), new DateOnly(2026, 3, 31), 150m, "Reservoir R1"));
        await db.SaveChangesAsync();

        (await meters.CaptureAsync(companyId, a, new DateOnly(2026, 2, 1), 30m, false)).IsSuccess.Should().BeTrue();
        (await meters.CaptureAsync(companyId, a, new DateOnly(2026, 3, 1), 60m, tamperSuspected: true)).IsSuccess.Should().BeTrue();
        (await meters.CaptureAsync(companyId, b, new DateOnly(2026, 2, 1), 20m, false)).IsSuccess.Should().BeTrue();
        (await meters.EstimateAsync(companyId, b, new DateOnly(2026, 3, 1))).IsSuccess.Should().BeTrue();

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        async Task<string> Render(string code)
        {
            Dictionary<string, object?> parameters = new() { ["FromDate"] = new DateOnly(2026, 2, 1), ["ToDate"] = new DateOnly(2026, 3, 31), ["AsAtDate"] = new DateOnly(2026, 3, 15), ["Days"] = 45 };
            var rendered = await runner.RenderAsync(companyId, code, parameters, ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? $"{code}: {rendered.Error.Message}" : null);
            return Encoding.UTF8.GetString(rendered.Value.Content);
        }

        string balance = await Render("M157");
        balance.Should().StartWith("Zone,").And.Contain("Z1,150.00,");
        string exceptions = await Render("M158");
        exceptions.Should().Contain($"WB-A-{companyId},WB-1,2026-02-01,2026-02-01,0,Tamper suspected") // the tampered reading is held, not accepted
            .And.Contain($"WB-B-{companyId},WB-2,2026-03-01,2026-02-01,").And.Contain("Running on estimates")
            .And.Contain($"WB-C-{companyId},—,").And.Contain("Unread for 165 days");
    }
}
