using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Housing;
using One4All.Core.Common;
using One4All.Core.Housing;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Housing;

/// <summary>Records and checks mapped boundaries, and serves layers through the map provider.</summary>
public sealed class SpatialRegisterService : ISpatialRegisterService
{
    private readonly One4AllDbContext _db;
    private readonly IMapProvider _map;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="map">The map provider.</param>
    public SpatialRegisterService(One4AllDbContext db, IMapProvider map)
    {
        _db = db;
        _map = map;
    }

    /// <inheritdoc />
    public async Task<Result<GeoBoundary>> MapStandAsync(long companyId, long standId, string wkt, CancellationToken ct = default)
    {
        HousingStand? stand = await _db.HousingStands.FirstOrDefaultAsync(s => s.Id == standId && s.CompanyId == companyId, ct).ConfigureAwait(false);
        if (stand is null)
        {
            return Error.NotFound("Hse.StandNotFound", $"Stand {standId} not found.");
        }

        return await MapAsync(wkt, stand.Map, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<GeoBoundary>> MapParcelAsync(long companyId, long parcelId, string wkt, CancellationToken ct = default)
    {
        LandParcel? parcel = await _db.LandParcels.FirstOrDefaultAsync(p => p.Id == parcelId && p.CompanyId == companyId, ct).ConfigureAwait(false);
        if (parcel is null)
        {
            return Error.NotFound("Hse.ParcelNotFound", $"Parcel {parcelId} not found.");
        }

        return await MapAsync(wkt, parcel.Map, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AreaDiscrepancy>> DiscrepanciesAsync(long companyId, decimal tolerancePercent, CancellationToken ct = default)
    {
        var mapped = await _db.HousingStands.AsNoTracking().Where(s => s.CompanyId == companyId && s.Boundary != null)
            .Select(s => new { s.StandNumber, s.SizeSquareMetres, Mapped = s.Boundary!.AreaSquareMetres }).ToListAsync(ct).ConfigureAwait(false);
        return [.. mapped
            .Select(s => new AreaDiscrepancy(s.StandNumber, s.SizeSquareMetres, s.Mapped, GeoPolygon.DiscrepancyPercent(s.Mapped, s.SizeSquareMetres)))
            .Where(d => Math.Abs(d.DifferencePercent) > tolerancePercent)
            .OrderByDescending(d => Math.Abs(d.DifferencePercent)).ThenBy(d => d.StandNumber, StringComparer.Ordinal)];
    }

    /// <inheritdoc />
    public async Task<string> GeoJsonAsync(long companyId, MapLayer layer, long? schemeId, CancellationToken ct = default)
        => GeoPolygon.ToGeoJson(await _map.FeaturesAsync(companyId, layer, schemeId, ct).ConfigureAwait(false));

    private async Task<Result<GeoBoundary>> MapAsync(string wkt, Action<MappedBoundary> record, CancellationToken ct)
    {
        string? problem = GeoPolygon.TryParse(wkt, out GeoBoundary? boundary);
        if (problem is not null)
        {
            return Error.Validation("Hse.Boundary", problem);
        }

        record(new MappedBoundary(wkt.Trim(), boundary!.Centroid.Longitude, boundary.Centroid.Latitude, boundary.AreaSquareMetres));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return boundary;
    }
}

/// <summary>The map provider over the register's own mapped boundaries.</summary>
public sealed class RegisterMapProvider : IMapProvider
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the provider.</summary>
    /// <param name="db">The database context.</param>
    public RegisterMapProvider(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<IReadOnlyList<MapFeature>> FeaturesAsync(long companyId, MapLayer layer, long? schemeId, CancellationToken ct = default)
    {
        if (layer == MapLayer.Parcels)
        {
            var parcels = await _db.LandParcels.AsNoTracking().Where(p => p.CompanyId == companyId && p.Boundary != null)
                .OrderBy(p => p.ParcelNumber).Select(p => new { p.Id, p.ParcelNumber, p.Status, p.AreaSquareMetres, p.Boundary!.Wkt }).ToListAsync(ct).ConfigureAwait(false);
            return [.. parcels.Select(p => new MapFeature(p.Id, p.ParcelNumber, p.Wkt, new Dictionary<string, string>
            {
                ["status"] = p.Status.ToString(),
                ["area_m2"] = p.AreaSquareMetres.ToString("0.##", CultureInfo.InvariantCulture),
            }))];
        }

        var stands = await _db.HousingStands.AsNoTracking().Where(s => s.CompanyId == companyId && s.Boundary != null && (schemeId == null || s.SchemeId == schemeId))
            .OrderBy(s => s.StandNumber).Select(s => new { s.Id, s.StandNumber, s.Status, s.SizeSquareMetres, s.Boundary!.Wkt }).ToListAsync(ct).ConfigureAwait(false);
        return [.. stands.Select(s => new MapFeature(s.Id, s.StandNumber, s.Wkt, new Dictionary<string, string>
        {
            ["status"] = s.Status.ToString(),
            ["size_m2"] = s.SizeSquareMetres.ToString("0.##", CultureInfo.InvariantCulture),
        }))];
    }
}
