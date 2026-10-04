using One4All.Application.Municipal;
using One4All.Core.Common;

namespace One4All.Application.Abstractions.Municipal;

/// <summary>A stop on a route sheet.</summary>
/// <param name="Sequence">Its place in the walking order.</param>
/// <param name="Serial">The meter.</param>
/// <param name="StandNumber">The stand it serves, if linked.</param>
/// <param name="LastReading">The last register value.</param>
/// <param name="LastReadingDate">When it was last read.</param>
public sealed record RouteStop(int Sequence, string Serial, string? StandNumber, decimal LastReading, DateOnly? LastReadingDate);

/// <summary>The outcome of importing a readings file.</summary>
/// <param name="Captured">Readings accepted.</param>
/// <param name="Rejected">Readings recorded but rejected by validation (negative, tamper).</param>
/// <param name="Errors">Lines not captured at all: unreadable, unknown meter, refused.</param>
public sealed record ReadingImportResult(int Captured, int Rejected, IReadOnlyList<MeterReadingCsvError> Errors);

/// <summary>
/// Reading routes and readers (brief §4.9): routes of meters in walking order for a reader, the route sheet to take out,
/// and the import of readings brought back from handhelds or a mobile app, each captured through the validated reading
/// capture.
/// </summary>
public interface IReadingRouteService
{
    /// <summary>Creates a route of active meters, each on no other route.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="code">The route code (unique).</param>
    /// <param name="name">The route name.</param>
    /// <param name="readerName">The meter reader.</param>
    /// <param name="meterIds">The meters in walking order.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The route, or why it was refused.</returns>
    Task<Result<long>> CreateRouteAsync(long companyId, string code, string name, string readerName, IReadOnlyList<long> meterIds, CancellationToken ct = default);

    /// <summary>The route sheet: each meter in walking order with its stand and last reading.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="code">The route.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The stops, or why there are none.</returns>
    Task<Result<IReadOnlyList<RouteStop>>> RouteSheetAsync(long companyId, string code, CancellationToken ct = default);

    /// <summary>Imports a readings file line by line; bad lines are reported and the rest captured.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="csv">The file's text (SERIAL, DATE, READING and optionally TAMPER).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What was captured, rejected and refused.</returns>
    Task<ReadingImportResult> ImportReadingsAsync(long companyId, string csv, CancellationToken ct = default);
}
