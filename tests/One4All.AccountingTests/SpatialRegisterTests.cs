using System.Globalization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Core.Housing;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the GIS hook: stand and parcel boundaries are recorded as validated WKT with centroid and area; a stand whose
/// mapped area departs from its surveyed size beyond the tolerance is reported; and layers come out as GeoJSON through the
/// map provider, filtered by scheme.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class SpatialRegisterTests
{
    private readonly AccountingFixture _fixture;

    public SpatialRegisterTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Boundaries_are_mapped_checked_against_the_survey_and_exported()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        LandParcel parcel = new(companyId, $"LP-GIS-{companyId}", "Mapped parcel", 5000m, 50000m);
        HousingScheme scheme = new(companyId, $"SCH-GIS-{companyId}", "Mapped Scheme", SchemeType.CouncilDelivered);
        db.LandParcels.Add(parcel);
        db.HousingSchemes.Add(scheme);
        await db.SaveChangesAsync();
        HousingStand surveyed = new(companyId, parcel.Id, scheme.Id, "GIS-1", 600m, 1000m);
        HousingStand misSurveyed = new(companyId, parcel.Id, scheme.Id, "GIS-2", 500m, 1000m);
        HousingStand elsewhere = new(companyId, parcel.Id, null, "GIS-3", 600m, 1000m);
        db.HousingStands.AddRange(surveyed, misSurveyed, elsewhere);
        await db.SaveChangesAsync();

        ISpatialRegisterService spatial = scope.ServiceProvider.GetRequiredService<ISpatialRegisterService>();
        (await spatial.MapStandAsync(companyId, surveyed.Id, "POLYGON((0 0, 1 0, 1 1))")).Error.Code.Should().Be("Hse.Boundary");
        decimal area = (await spatial.MapStandAsync(companyId, surveyed.Id, Rectangle(31.05))).Value.AreaSquareMetres;
        ((double)area).Should().BeApproximately(600, 1);
        (await spatial.MapStandAsync(companyId, misSurveyed.Id, Rectangle(31.0503))).IsSuccess.Should().BeTrue();
        (await spatial.MapStandAsync(companyId, elsewhere.Id, Rectangle(31.0506))).IsSuccess.Should().BeTrue();
        (await spatial.MapParcelAsync(companyId, parcel.Id, Rectangle(31.05, 100, 100))).IsSuccess.Should().BeTrue();

        HousingStand stored = await db.HousingStands.AsNoTracking().SingleAsync(s => s.Id == surveyed.Id);
        stored.Boundary!.CentroidLongitude.Should().BeApproximately(31.05 + (10 / (111_195.08 * Math.Cos(-17.8 * Math.PI / 180))), 1e-5);

        // 600 m² mapped against 500 surveyed: 20% out, beyond a 2% tolerance; the others agree.
        IReadOnlyList<AreaDiscrepancy> discrepancies = await spatial.DiscrepanciesAsync(companyId, 2m);
        discrepancies.Should().ContainSingle().Which.StandNumber.Should().Be("GIS-2");
        discrepancies[0].DifferencePercent.Should().BeApproximately(20m, 0.3m);

        string schemeLayer = await spatial.GeoJsonAsync(companyId, MapLayer.Stands, scheme.Id);
        schemeLayer.Should().Contain("\"label\":\"GIS-1\"").And.Contain("\"label\":\"GIS-2\"").And.NotContain("GIS-3").And.Contain("\"status\":\"Available\"");
        (await spatial.GeoJsonAsync(companyId, MapLayer.Parcels, null)).Should().Contain($"\"label\":\"{parcel.ParcelNumber}\"");
    }

    // A rectangle of the given size near 17.8° S.
    private static string Rectangle(double west, double widthMetres = 20, double heightMetres = 30)
    {
        const double south = -17.8;
        double dLat = heightMetres / 111_195.08;
        double dLon = widthMetres / (111_195.08 * Math.Cos((south + (dLat / 2)) * Math.PI / 180));
        string P(double lon, double lat) => $"{lon.ToString("R", CultureInfo.InvariantCulture)} {lat.ToString("R", CultureInfo.InvariantCulture)}";
        return $"POLYGON(({P(west, south)}, {P(west + dLon, south)}, {P(west + dLon, south + dLat)}, {P(west, south + dLat)}, {P(west, south)}))";
    }
}
