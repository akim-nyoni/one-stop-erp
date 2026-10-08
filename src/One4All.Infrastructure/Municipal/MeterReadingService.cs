using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Municipal;
using One4All.Application.Municipal;
using One4All.Core.Common;
using One4All.Core.Municipal;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Municipal;

/// <summary>Meter installation, validated and estimated readings, replacement and reading reports.</summary>
public sealed class MeterReadingService : IMeterReadingService
{
    // Accepted actual readings used for the daily consumption rate: the last few, so the rate follows recent use.
    private const int HistoryPoints = 7;

    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public MeterReadingService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public Task<Result<long>> InstallAsync(long companyId, string serial, int digits, decimal multiplier, decimal startReading, DateOnly date, CancellationToken ct = default)
        => _db.AtomicAsync(() => InstallCoreAsync(companyId, serial, digits, multiplier, startReading, date, 0m, ct), ct);

    private async Task<Result<long>> InstallCoreAsync(long companyId, string serial, int digits, decimal multiplier, decimal startReading, DateOnly date, decimal unitsCredit, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(serial) || digits < 0 || digits > 12 || multiplier <= 0m || startReading < 0m)
        {
            return Error.Validation("Mun.MeterDetails", "A meter needs a serial, 0 to 12 dials, a positive multiplier and a start reading of zero or more.");
        }

        string normalised = serial.Trim().ToUpperInvariant();
        if (await _db.Set<Meter>().AnyAsync(m => m.CompanyId == companyId && m.Serial == normalised, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Mun.SerialInUse", $"Meter {normalised} is already on the register.");
        }

        Meter meter = new(companyId, normalised, multiplier) { Digits = digits, InstalledOn = date };
        meter.Start(startReading, date, unitsCredit);
        _db.Set<Meter>().Add(meter);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        _db.Set<MeterReading>().Add(new MeterReading(companyId, meter.Id, date, startReading, startReading, 0m, 0m, MeterReadingKind.Actual, ReadingFlags.None, accepted: true));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return meter.Id;
    }

    /// <inheritdoc />
    public Task<Result<ReadingOutcome>> CaptureAsync(long companyId, long meterId, DateOnly date, decimal reading, bool tamperSuspected, ReadingThresholds? thresholds = null, CancellationToken ct = default)
        => _db.AtomicAsync(() => CaptureCoreAsync(companyId, meterId, date, reading, tamperSuspected, thresholds ?? ReadingThresholds.Default, ct), ct);

    private async Task<Result<ReadingOutcome>> CaptureCoreAsync(long companyId, long meterId, DateOnly date, decimal reading, bool tamperSuspected, ReadingThresholds thresholds, CancellationToken ct)
    {
        Result<Meter> found = await FindActiveAsync(companyId, meterId, date, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        Meter meter = found.Value;
        int days = meter.LastActualDate is DateOnly last ? date.DayNumber - last.DayNumber : 0;
        decimal expected = MeterReadingRules.Expected(await HistoryPointsAsync(meter.Id, ct).ConfigureAwait(false), days);
        ReadingAssessment assessment = MeterReadingRules.Assess(
            new MeterState(meter.LastActualReading, meter.EstimatedSinceActual, meter.UnitsCredit, meter.Digits, meter.Multiplier), reading, expected, thresholds, tamperSuspected);

        MeterReading record = new(companyId, meter.Id, date, reading, meter.LastReading, assessment.RawConsumption, assessment.BilledConsumption, MeterReadingKind.Actual, assessment.Flags, assessment.Accepted);
        _db.Set<MeterReading>().Add(record);
        if (assessment.Accepted)
        {
            meter.ApplyActual(reading, date, assessment.UnitsCreditAfter);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new ReadingOutcome(record.Id, reading, assessment.RawConsumption, assessment.BilledConsumption, assessment.Flags, assessment.Accepted);
    }

    /// <inheritdoc />
    public Task<Result<ReadingOutcome>> EstimateAsync(long companyId, long meterId, DateOnly date, CancellationToken ct = default)
        => _db.AtomicAsync(() => EstimateCoreAsync(companyId, meterId, date, ct), ct);

    private async Task<Result<ReadingOutcome>> EstimateCoreAsync(long companyId, long meterId, DateOnly date, CancellationToken ct)
    {
        Result<Meter> found = await FindActiveAsync(companyId, meterId, date, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        Meter meter = found.Value;
        int days = meter.LastReadingDate is DateOnly last ? date.DayNumber - last.DayNumber : 0;
        decimal units = MeterReadingRules.Expected(await HistoryPointsAsync(meter.Id, ct).ConfigureAwait(false), days);
        if (units <= 0m)
        {
            return Error.Rule("Mun.NoHistory", $"Meter {meter.Serial} has too little reading history to estimate from.");
        }

        decimal estimated = MeterReadingRules.EstimatedReading(meter.LastReading, units, meter.Digits, meter.Multiplier);
        MeterReading record = new(companyId, meter.Id, date, estimated, meter.LastReading, units, units, MeterReadingKind.Estimated, ReadingFlags.None, accepted: true);
        _db.Set<MeterReading>().Add(record);
        meter.ApplyEstimate(estimated, date, units);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new ReadingOutcome(record.Id, estimated, units, units, ReadingFlags.None, true);
    }

    /// <inheritdoc />
    public Task<Result<MeterReplacement>> ReplaceAsync(long companyId, long oldMeterId, DateOnly date, decimal finalReading, string newSerial, int newDigits, decimal newMultiplier, decimal newStartReading, CancellationToken ct = default)
        => _db.AtomicAsync(() => ReplaceCoreAsync(companyId, oldMeterId, date, finalReading, newSerial, newDigits, newMultiplier, newStartReading, ct), ct);

    private async Task<Result<MeterReplacement>> ReplaceCoreAsync(long companyId, long oldMeterId, DateOnly date, decimal finalReading, string newSerial, int newDigits, decimal newMultiplier, decimal newStartReading, CancellationToken ct)
    {
        Result<ReadingOutcome> final = await CaptureCoreAsync(companyId, oldMeterId, date, finalReading, tamperSuspected: false, ReadingThresholds.Default, ct).ConfigureAwait(false);
        if (final.IsFailure)
        {
            return final.Error;
        }

        if (!final.Value.Accepted)
        {
            return Error.Rule("Mun.FinalReadingRejected", $"The final reading was rejected ({final.Value.Flags}); correct it before replacing the meter.");
        }

        Meter old = await _db.Set<Meter>().FirstAsync(m => m.Id == oldMeterId, ct).ConfigureAwait(false);
        Result<long> installed = await InstallCoreAsync(companyId, newSerial, newDigits, newMultiplier, newStartReading, date, old.UnitsCredit, ct).ConfigureAwait(false);
        if (installed.IsFailure)
        {
            return installed.Error;
        }

        old.Remove(date);
        List<PropertyServiceLink> links = await _db.Set<PropertyServiceLink>().Where(l => l.MeterId == oldMeterId).ToListAsync(ct).ConfigureAwait(false);
        foreach (PropertyServiceLink link in links)
        {
            link.MeterId = installed.Value;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new MeterReplacement(final.Value, installed.Value, links.Count);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReadingException>> ExceptionsAsync(long companyId, DateOnly start, DateOnly end, CancellationToken ct = default)
        => await (
            from r in _db.Set<MeterReading>().AsNoTracking()
            join m in _db.Set<Meter>().IgnoreQueryFilters([Persistence.One4AllDbContext.SoftDeleteFilter]).AsNoTracking() on r.MeterId equals m.Id
            where r.CompanyId == companyId && r.Flags != ReadingFlags.None && r.ReadingDate >= start && r.ReadingDate <= end
            orderby r.ReadingDate, r.Id
            select new ReadingException(m.Serial, r.ReadingDate, r.Kind, r.Reading, r.Consumption, r.Flags, r.Accepted))
            .ToListAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConsumptionLine>> HistoryAsync(long companyId, long meterId, CancellationToken ct = default)
        => await _db.Set<MeterReading>().AsNoTracking().Where(r => r.CompanyId == companyId && r.MeterId == meterId).OrderBy(r => r.ReadingDate).ThenBy(r => r.Id)
            .Select(r => new ConsumptionLine(r.ReadingDate, r.Kind, r.Reading, r.Consumption, r.BilledConsumption, r.Flags, r.Accepted))
            .ToListAsync(ct).ConfigureAwait(false);

    private async Task<Result<Meter>> FindActiveAsync(long companyId, long meterId, DateOnly date, CancellationToken ct)
    {
        Meter? meter = await _db.Set<Meter>().FirstOrDefaultAsync(m => m.Id == meterId && m.CompanyId == companyId, ct).ConfigureAwait(false);
        if (meter is null)
        {
            return Error.NotFound("Mun.MeterNotFound", $"Meter {meterId} not found.");
        }

        if (!meter.IsActive)
        {
            return Error.Rule("Mun.MeterInactive", $"Meter {meter.Serial} is not in service.");
        }

        if (meter.LastReadingDate is DateOnly last && date <= last)
        {
            return Error.Validation("Mun.ReadingDate", $"Meter {meter.Serial} was last read on {last:yyyy-MM-dd}; a new reading must be later.");
        }

        return meter;
    }

    private async Task<List<ConsumptionPoint>> HistoryPointsAsync(long meterId, CancellationToken ct)
    {
        var rows = await _db.Set<MeterReading>().AsNoTracking()
            .Where(r => r.MeterId == meterId && r.Kind == MeterReadingKind.Actual && r.Accepted)
            .OrderByDescending(r => r.ReadingDate).ThenByDescending(r => r.Id).Take(HistoryPoints)
            .Select(r => new { r.ReadingDate, r.Consumption }).ToListAsync(ct).ConfigureAwait(false);
        rows.Reverse();
        return rows.Select(r => new ConsumptionPoint(r.ReadingDate, r.Consumption)).ToList();
    }
}
