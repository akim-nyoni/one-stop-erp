using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Housing;
using One4All.Core.Common;
using One4All.Core.Housing;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Housing;

/// <summary>The stand, parcel and scheme register details.</summary>
public sealed class HousingRegisterDetailsService : IHousingRegisterDetailsService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public HousingRegisterDetailsService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result> SetStandDetailsAsync(long companyId, string standNumber, StandDetails details, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(details);
        HousingStand? stand = await StandAsync(companyId, standNumber, ct).ConfigureAwait(false);
        if (stand is null)
        {
            return Result.Failure(Error.NotFound("Hse.StandNotFound", $"Stand {standNumber} not found."));
        }

        stand.StreetAddress = Clean(details.StreetAddress);
        stand.Ward = Clean(details.Ward);
        stand.Suburb = Clean(details.Suburb);
        stand.Zone = Clean(details.Zone);
        stand.IntendedUse = Clean(details.IntendedUse);
        stand.Zoning = Clean(details.Zoning);
        stand.ServicingPhase = Clean(details.ServicingPhase);
        stand.HasWater = details.HasWater;
        stand.HasSewer = details.HasSewer;
        stand.HasRoad = details.HasRoad;
        stand.HasElectricity = details.HasElectricity;
        stand.HasStormWater = details.HasStormWater;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> AddStandRemarkAsync(long companyId, string standNumber, string text, string recordedBy, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Result.Failure(Error.Validation("Hse.Remark", "A remark needs some text."));
        }

        HousingStand? stand = await StandAsync(companyId, standNumber, ct).ConfigureAwait(false);
        if (stand is null)
        {
            return Result.Failure(Error.NotFound("Hse.StandNotFound", $"Stand {standNumber} not found."));
        }

        _db.Set<StandRemark>().Add(new StandRemark(companyId, stand.Id, text.Trim(), string.IsNullOrWhiteSpace(recordedBy) ? "system" : recordedBy.Trim(), DateTimeOffset.UtcNow));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<(DateTimeOffset At, string RecordedBy, string Text)>> StandRemarksAsync(long companyId, string standNumber, CancellationToken ct = default)
    {
        HousingStand? stand = await StandAsync(companyId, standNumber, ct).ConfigureAwait(false);
        return stand is null ? [] : [.. (await _db.Set<StandRemark>().AsNoTracking().Where(r => r.StandId == stand.Id).OrderBy(r => r.At).ThenBy(r => r.Id).ToListAsync(ct).ConfigureAwait(false))
            .Select(r => (r.At, r.RecordedBy, r.Text))];
    }

    /// <inheritdoc />
    public async Task<Result> SetParcelDetailsAsync(long companyId, string parcelNumber, ParcelDetails details, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(details);
        string key = (parcelNumber ?? string.Empty).Trim();
        LandParcel? parcel = await _db.Set<LandParcel>().FirstOrDefaultAsync(p => p.CompanyId == companyId && p.ParcelNumber == key, ct).ConfigureAwait(false);
        if (parcel is null)
        {
            return Result.Failure(Error.NotFound("Hse.ParcelNotFound", $"Parcel {key} not found."));
        }

        parcel.Locality = Clean(details.Locality);
        parcel.Ward = Clean(details.Ward);
        parcel.LandUse = Clean(details.LandUse);
        parcel.Tenure = details.Tenure;
        parcel.Improvements = Clean(details.Improvements);
        parcel.Occupation = details.Occupation;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> SetSchemeDetailsAsync(long companyId, string schemeCode, SchemeDetails details, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(details);
        if (details.StartDate is DateOnly start && details.CompletionDate is DateOnly end && end < start)
        {
            return Result.Failure(Error.Validation("Hse.SchemeDates", "A scheme cannot be completed before it starts."));
        }

        if (details.AcceptanceDeposit < 0m)
        {
            return Result.Failure(Error.Validation("Hse.SchemeDeposit", "The acceptance deposit is zero or more."));
        }

        string key = (schemeCode ?? string.Empty).Trim().ToUpperInvariant();
        HousingScheme? scheme = await _db.Set<HousingScheme>().FirstOrDefaultAsync(s => s.CompanyId == companyId && s.Code == key, ct).ConfigureAwait(false);
        if (scheme is null)
        {
            return Result.Failure(Error.NotFound("Hse.SchemeNotFound", $"Scheme {key} not found."));
        }

        scheme.FundingSource = details.FundingSource;
        scheme.StartDate = details.StartDate;
        scheme.CompletionDate = details.CompletionDate;
        scheme.AcceptanceDeposit = details.AcceptanceDeposit is > 0m ? details.AcceptanceDeposit : null;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    private Task<HousingStand?> StandAsync(long companyId, string standNumber, CancellationToken ct)
    {
        string key = (standNumber ?? string.Empty).Trim().ToUpperInvariant();
        return _db.HousingStands.FirstOrDefaultAsync(s => s.CompanyId == companyId && s.StandNumber == key, ct);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
