using One4All.Application.Housing;
using One4All.Core.Common;

namespace One4All.Application.Abstractions.Housing;

/// <summary>What a map layer shows.</summary>
public enum MapLayer
{
    /// <summary>Land parcels.</summary>
    Parcels = 1,

    /// <summary>Stands.</summary>
    Stands = 2,
}

/// <summary>
/// The map hook (brief §4.10.10): supplies the features of a layer so stand maps and scheme layouts can be plotted by any
/// map front-end later without a schema change. The register implementation reads the mapped boundaries; a PostGIS-backed
/// one can replace it.
/// </summary>
public interface IMapProvider
{
    /// <summary>The mapped features of a layer.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="layer">The layer.</param>
    /// <param name="schemeId">Only stands of this scheme (stands layer).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The features.</returns>
    Task<IReadOnlyList<MapFeature>> FeaturesAsync(long companyId, MapLayer layer, long? schemeId, CancellationToken ct = default);
}

/// <summary>A stand whose mapped area disagrees with its surveyed size.</summary>
/// <param name="StandNumber">The stand.</param>
/// <param name="SurveyedSquareMetres">The size on the register.</param>
/// <param name="MappedSquareMetres">The area of its mapped boundary.</param>
/// <param name="DifferencePercent">The difference, as a percentage of the surveyed size.</param>
public sealed record AreaDiscrepancy(string StandNumber, decimal SurveyedSquareMetres, decimal MappedSquareMetres, decimal DifferencePercent);

/// <summary>
/// The spatial register (brief §4.10.10 GIS hook): boundaries recorded on parcels and stands as validated WKT polygons
/// with centroid and area; stands whose mapped area departs from the surveyed size beyond a tolerance; and any layer as
/// GeoJSON.
/// </summary>
public interface ISpatialRegisterService
{
    /// <summary>Maps a stand's boundary.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="standId">The stand.</param>
    /// <param name="wkt">The boundary as a WKT polygon (longitude latitude).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The boundary as measured, or why it was refused.</returns>
    Task<Result<GeoBoundary>> MapStandAsync(long companyId, long standId, string wkt, CancellationToken ct = default);

    /// <summary>Maps a parcel's boundary.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="parcelId">The parcel.</param>
    /// <param name="wkt">The boundary as a WKT polygon (longitude latitude).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The boundary as measured, or why it was refused.</returns>
    Task<Result<GeoBoundary>> MapParcelAsync(long companyId, long parcelId, string wkt, CancellationToken ct = default);

    /// <summary>Mapped stands whose area differs from the surveyed size by more than the tolerance.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="tolerancePercent">The tolerance (%).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The discrepancies, largest first.</returns>
    Task<IReadOnlyList<AreaDiscrepancy>> DiscrepanciesAsync(long companyId, decimal tolerancePercent, CancellationToken ct = default);

    /// <summary>A layer as GeoJSON.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="layer">The layer.</param>
    /// <param name="schemeId">Only stands of this scheme (stands layer).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The GeoJSON FeatureCollection.</returns>
    Task<string> GeoJsonAsync(long companyId, MapLayer layer, long? schemeId, CancellationToken ct = default);
}
