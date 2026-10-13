using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Municipal;
using One4All.Core.Common;
using One4All.Core.Municipal;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Municipal;

/// <summary>Keeps stands' locations and meters' details.</summary>
public sealed class PropertyRegisterService : IPropertyRegisterService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public PropertyRegisterService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result> SetLocationAsync(long companyId, string standNumber, string? suburb, decimal? latitude, decimal? longitude, CancellationToken ct = default)
    {
        string stand = (standNumber ?? string.Empty).Trim().ToUpperInvariant();
        Property? property = await _db.Properties.FirstOrDefaultAsync(p => p.CompanyId == companyId && p.StandNumber.ToUpper() == stand, ct).ConfigureAwait(false);
        if (property is null)
        {
            return Result.Failure(Error.NotFound("Mun.PropertyNotFound", $"Stand {stand} not found."));
        }

        if (latitude.HasValue != longitude.HasValue || latitude is < -90m or > 90m || longitude is < -180m or > 180m)
        {
            return Result.Failure(Error.Validation("Mun.Gps", "A GPS position is a latitude from −90 to 90 and a longitude from −180 to 180, both given."));
        }

        if (suburb is not null)
        {
            property.Suburb = string.IsNullOrWhiteSpace(suburb) ? null : suburb.Trim();
        }

        if (latitude is decimal lat)
        {
            (property.Latitude, property.Longitude) = (decimal.Round(lat, 6), decimal.Round(longitude!.Value, 6));
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> SetMeterDetailsAsync(long companyId, string serial, string? meterType, int? sizeMm, string? location, CancellationToken ct = default)
    {
        string number = (serial ?? string.Empty).Trim();
        Meter? meter = await _db.Set<Meter>().FirstOrDefaultAsync(m => m.CompanyId == companyId && m.Serial == number, ct).ConfigureAwait(false);
        if (meter is null)
        {
            return Result.Failure(Error.NotFound("Mun.MeterNotFound", $"Meter {number} not found."));
        }

        if (sizeMm is < 1 or > 2000)
        {
            return Result.Failure(Error.Validation("Mun.MeterSize", "A meter size is from 1 to 2000 mm."));
        }

        if (meterType is not null)
        {
            meter.MeterType = string.IsNullOrWhiteSpace(meterType) ? null : meterType.Trim();
        }

        if (location is not null)
        {
            meter.Location = string.IsNullOrWhiteSpace(location) ? null : location.Trim();
        }

        meter.SizeMm = sizeMm ?? meter.SizeMm;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<Application.Municipal.MeterConsumption>>> ConsumptionAsync(long companyId, string standNumber, DateOnly asAt, int months = 12, CancellationToken ct = default)
    {
        string stand = (standNumber ?? string.Empty).Trim().ToUpperInvariant();
        Property? property = await _db.Properties.AsNoTracking().Include(p => p.Services)
            .FirstOrDefaultAsync(p => p.CompanyId == companyId && p.StandNumber.ToUpper() == stand, ct).ConfigureAwait(false);
        if (property is null)
        {
            return Error.NotFound("Mun.PropertyNotFound", $"No stand {stand}.");
        }

        months = Math.Clamp(months, 1, 36);
        DateOnly last = new(asAt.Year, asAt.Month, 1);
        DateOnly first = last.AddMonths(1 - months);
        DateOnly end = last.AddMonths(1);
        List<(long MeterId, string Service)> meters = [.. property.Services.Where(s => s.MeterId is not null).Select(s => (s.MeterId!.Value, s.ServiceType.ToString())).Distinct()];
        List<long> ids = [.. meters.Select(m => m.MeterId)];
        Dictionary<long, string> serials = await _db.Set<Meter>().AsNoTracking().IgnoreQueryFilters([One4AllDbContext.SoftDeleteFilter])
            .Where(m => ids.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.Serial, ct).ConfigureAwait(false);
        var readings = await _db.MeterReadings.AsNoTracking()
            .Where(r => r.CompanyId == companyId && ids.Contains(r.MeterId) && r.Accepted && r.ReadingDate >= first && r.ReadingDate < end)
            .Select(r => new { r.MeterId, r.ReadingDate, r.Consumption }).ToListAsync(ct).ConfigureAwait(false);

        List<Application.Municipal.MeterConsumption> result = [];
        foreach ((long meterId, string service) in meters)
        {
            List<Application.Municipal.MonthlyConsumption> series = [];
            for (DateOnly month = first; month < end; month = month.AddMonths(1))
            {
                DateOnly next = month.AddMonths(1);
                series.Add(new Application.Municipal.MonthlyConsumption(month,
                    readings.Where(r => r.MeterId == meterId && r.ReadingDate >= month && r.ReadingDate < next).Sum(r => r.Consumption)));
            }

            result.Add(new Application.Municipal.MeterConsumption(serials.GetValueOrDefault(meterId, meterId.ToString(System.Globalization.CultureInfo.InvariantCulture)), service, series));
        }

        return result;
    }
}
