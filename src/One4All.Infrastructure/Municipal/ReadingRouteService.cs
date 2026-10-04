using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Municipal;
using One4All.Application.Municipal;
using One4All.Core.Common;
using One4All.Core.Municipal;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Municipal;

/// <summary>Reading routes, route sheets and the import of readings through the validated capture.</summary>
public sealed class ReadingRouteService : IReadingRouteService
{
    private readonly One4AllDbContext _db;
    private readonly IMeterReadingService _readings;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="readings">The validated reading capture.</param>
    public ReadingRouteService(One4AllDbContext db, IMeterReadingService readings)
    {
        _db = db;
        _readings = readings;
    }

    /// <inheritdoc />
    public async Task<Result<long>> CreateRouteAsync(long companyId, string code, string name, string readerName, IReadOnlyList<long> meterIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(meterIds);
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(readerName) || meterIds.Count == 0)
        {
            return Error.Validation("Mun.RouteDetails", "A route needs a code, a name, a reader and at least one meter.");
        }

        if (meterIds.Distinct().Count() != meterIds.Count)
        {
            return Error.Validation("Mun.RouteDuplicate", "A meter is listed more than once.");
        }

        string routeCode = code.Trim().ToUpperInvariant();
        if (await _db.Set<ReadingRoute>().AnyAsync(r => r.CompanyId == companyId && r.Code == routeCode, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Mun.RouteExists", $"Route {routeCode} already exists.");
        }

        int known = await _db.Set<Meter>().CountAsync(m => m.CompanyId == companyId && m.IsActive && meterIds.Contains(m.Id), ct).ConfigureAwait(false);
        if (known != meterIds.Count)
        {
            return Error.NotFound("Mun.MeterNotFound", "A meter is not on the register or not in service.");
        }

        List<long> taken = await _db.Set<ReadingRouteStop>().Where(s => meterIds.Contains(s.MeterId)).Select(s => s.MeterId).ToListAsync(ct).ConfigureAwait(false);
        if (taken.Count > 0)
        {
            return Error.Conflict("Mun.MeterOnRoute", $"Meters already on a route: {string.Join(", ", taken)}.");
        }

        ReadingRoute route = new(companyId, routeCode, name.Trim(), readerName.Trim());
        foreach (long meterId in meterIds)
        {
            route.AddStop(meterId);
        }

        _db.Set<ReadingRoute>().Add(route);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return route.Id;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<RouteStop>>> RouteSheetAsync(long companyId, string code, CancellationToken ct = default)
    {
        string routeCode = (code ?? string.Empty).Trim().ToUpperInvariant();
        ReadingRoute? route = await _db.Set<ReadingRoute>().AsNoTracking().Include(r => r.Stops)
            .FirstOrDefaultAsync(r => r.CompanyId == companyId && r.Code == routeCode, ct).ConfigureAwait(false);
        if (route is null)
        {
            return Error.NotFound("Mun.RouteNotFound", $"Route {routeCode} not found.");
        }

        List<long> meterIds = [.. route.Stops.Select(s => s.MeterId)];
        Dictionary<long, Meter> meters = await _db.Set<Meter>().AsNoTracking().Where(m => meterIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, ct).ConfigureAwait(false);
        var stands = await (
            from l in _db.Set<PropertyServiceLink>().AsNoTracking()
            join p in _db.Properties.AsNoTracking() on l.PropertyId equals p.Id
            where l.MeterId != null && meterIds.Contains(l.MeterId.Value)
            select new { MeterId = l.MeterId!.Value, p.StandNumber }).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, string> standByMeter = stands.GroupBy(s => s.MeterId).ToDictionary(g => g.Key, g => g.First().StandNumber);

        List<RouteStop> sheet = [.. route.Stops.OrderBy(s => s.Sequence).Where(s => meters.ContainsKey(s.MeterId)).Select(s =>
        {
            Meter meter = meters[s.MeterId];
            return new RouteStop(s.Sequence, meter.Serial, standByMeter.GetValueOrDefault(s.MeterId), meter.LastReading, meter.LastReadingDate);
        })];
        return Result<IReadOnlyList<RouteStop>>.Success(sheet);
    }

    /// <inheritdoc />
    public async Task<ReadingImportResult> ImportReadingsAsync(long companyId, string csv, CancellationToken ct = default)
    {
        MeterReadingCsvResult parsed = MeterReadingCsvParser.Parse(csv);
        List<MeterReadingCsvError> errors = [.. parsed.Errors];
        List<string> serials = [.. parsed.Rows.Select(r => r.Serial).Distinct()];
        Dictionary<string, long> meterBySerial = await _db.Set<Meter>().AsNoTracking().Where(m => m.CompanyId == companyId && serials.Contains(m.Serial))
            .ToDictionaryAsync(m => m.Serial, m => m.Id, StringComparer.Ordinal, ct).ConfigureAwait(false);

        int captured = 0;
        int rejected = 0;
        foreach (MeterReadingCsvRow row in parsed.Rows.OrderBy(r => r.Date).ThenBy(r => r.LineNumber))
        {
            if (!meterBySerial.TryGetValue(row.Serial, out long meterId))
            {
                errors.Add(new MeterReadingCsvError(row.LineNumber, $"Meter {row.Serial} is not on the register."));
                continue;
            }

            // Each reading is its own unit: one refused line does not undo the others.
            Result<ReadingOutcome> outcome = await _readings.CaptureAsync(companyId, meterId, row.Date, row.Reading, row.TamperSuspected, null, ct).ConfigureAwait(false);
            if (outcome.IsFailure)
            {
                errors.Add(new MeterReadingCsvError(row.LineNumber, outcome.Error.Message));
            }
            else if (outcome.Value.Accepted)
            {
                captured++;
            }
            else
            {
                rejected++;
            }
        }

        return new ReadingImportResult(captured, rejected, [.. errors.OrderBy(e => e.LineNumber)]);
    }
}
