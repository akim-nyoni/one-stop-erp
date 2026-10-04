using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Municipal;
using One4All.Core.Municipal;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers reading routes and imports: a route of two meters in walking order for a reader (a duplicate code, a meter
/// already routed and an unknown meter are refused); the route sheet lists them in order with the stand and last
/// reading; an imported file captures a good reading, records a negative and a tampered one as rejected, and reports an
/// unknown meter and an unreadable date by line.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ReadingRouteTests
{
    private readonly AccountingFixture _fixture;

    public ReadingRouteTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Routes_give_sheets_and_imported_readings_are_captured_line_by_line()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Tariff water = new(companyId, "WAT-RT", "Water", ServiceType.Water, TariffType.PerUnit, new DateOnly(2026, 1, 1), "4000") { PerUnitRate = 1m };
        db.Tariffs.Add(water);
        Customer owner = new(companyId, "RT-OWN", "Route Owner", Scenario.Usd);
        db.Customers.Add(owner);
        await db.SaveChangesAsync();

        IMeterReadingService meters = scope.ServiceProvider.GetRequiredService<IMeterReadingService>();
        DateOnly installed = new(2026, 1, 1);
        string s1 = $"RT{companyId}-1", s2 = $"RT{companyId}-2", s3 = $"RT{companyId}-3";
        long m1 = (await meters.InstallAsync(companyId, s1, 0, 1m, 100m, installed)).Value;
        long m2 = (await meters.InstallAsync(companyId, s2, 0, 1m, 500m, installed)).Value;
        long m3 = (await meters.InstallAsync(companyId, s3, 0, 1m, 0m, installed)).Value;
        Property stand = new(companyId, "R1", "Ward 13", owner.Id);
        stand.AddService(ServiceType.Water, water.Id, installed).MeterId = m1;
        db.Properties.Add(stand);
        await db.SaveChangesAsync();

        IReadingRouteService routes = scope.ServiceProvider.GetRequiredService<IReadingRouteService>();
        (await routes.CreateRouteAsync(companyId, "north", "North Loop", "T. Ncube", [m2, m1])).IsSuccess.Should().BeTrue();
        (await routes.CreateRouteAsync(companyId, "NORTH", "Again", "T. Ncube", [m3])).Error.Code.Should().Be("Mun.RouteExists");
        (await routes.CreateRouteAsync(companyId, "SOUTH", "South Loop", "P. Dube", [m3, m1])).Error.Code.Should().Be("Mun.MeterOnRoute");
        (await routes.CreateRouteAsync(companyId, "SOUTH", "South Loop", "P. Dube", [m3, -1])).Error.Code.Should().Be("Mun.MeterNotFound");

        (await routes.RouteSheetAsync(companyId, "north")).Value.Should().Equal(
            new RouteStop(1, s2, null, 500m, installed),
            new RouteStop(2, s1, "R1", 100m, installed));

        string csv = string.Join("\n",
            "Serial,Date,Reading,Tamper",
            $"{s1},2026-02-01,130,",
            $"{s2},2026-02-01,490,",
            $"{s3},01/02/2026,20,Y",
            "NOPE,2026-02-01,5,",
            $"{s1},2026-02-xx,140,");
        ReadingImportResult imported = await routes.ImportReadingsAsync(companyId, csv);
        imported.Captured.Should().Be(1);
        imported.Rejected.Should().Be(2);
        imported.Errors.Select(e => e.LineNumber).Should().Equal(5, 6);
        (await routes.RouteSheetAsync(companyId, "NORTH")).Value.Single(s => s.Serial == s1).LastReading.Should().Be(130m);
    }
}
