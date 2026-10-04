using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Housing;
using One4All.Core.Common;
using One4All.Core.Housing;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Housing;

/// <summary>Survey stages, servicing certificates and land valuations (brief §14 H9, H12, H13).</summary>
public sealed class SchemeProgressService : ISchemeProgressService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public SchemeProgressService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result<int>> RecordSurveyAsync(long companyId, IReadOnlyList<string> standNumbers, SurveyStage stage, DateOnly date, string reference, CancellationToken ct = default)
    {
        List<string> numbers = [.. standNumbers.Select(n => n.Trim()).Where(n => n.Length > 0).Distinct(StringComparer.Ordinal)];
        if (numbers.Count == 0 || string.IsNullOrWhiteSpace(reference) || !Enum.IsDefined(stage))
        {
            return Error.Validation("Hse.SurveyDetails", "Name the stands, the stage reached and the surveyor's reference.");
        }

        var stands = await _db.HousingStands.Where(s => s.CompanyId == companyId && numbers.Contains(s.StandNumber)).Select(s => new { s.Id, s.StandNumber }).ToListAsync(ct).ConfigureAwait(false);
        List<string> missing = [.. numbers.Except(stands.Select(s => s.StandNumber), StringComparer.Ordinal)];
        if (missing.Count > 0)
        {
            return Error.NotFound("Hse.StandNotFound", $"Stand(s) not found: {string.Join(", ", missing)}.");
        }

        List<long> ids = [.. stands.Select(s => s.Id)];
        var latest = (await _db.Set<StandSurveyStep>().AsNoTracking().Where(s => ids.Contains(s.StandId)).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(s => s.StandId).ToDictionary(g => g.Key, g => g.OrderBy(s => s.Stage).Last());
        List<string> refused = [.. stands.Where(s => latest.TryGetValue(s.Id, out StandSurveyStep? last) && (!SchemeProgressRules.CanAdvance(last.Stage, stage) || date < last.Date))
            .Select(s => s.StandNumber).Order(StringComparer.Ordinal)];
        if (refused.Count > 0)
        {
            return Error.Rule("Hse.SurveyStage", $"Already at or past {stage}, or surveyed later than {date:yyyy-MM-dd}: {string.Join(", ", refused)}.");
        }

        _db.Set<StandSurveyStep>().AddRange(stands.Select(s => new StandSurveyStep(companyId, s.Id, stage, date, reference.Trim())));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return stands.Count;
    }

    /// <inheritdoc />
    public async Task<Result<long>> RecordServicingAsync(ServicingCertificate certificate, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(certificate.Service) || certificate.PercentComplete is < 0m or > 100m || certificate.StandsReached < 0 || string.IsNullOrWhiteSpace(certificate.Reference))
        {
            return Error.Validation("Hse.ServicingDetails", "A certificate needs the service, a percentage from 0 to 100, the stands reached and its reference.");
        }

        HousingScheme? scheme = await _db.HousingSchemes.AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == certificate.CompanyId && s.Code == certificate.SchemeCode.Trim(), ct).ConfigureAwait(false);
        if (scheme is null)
        {
            return Error.NotFound("Hse.SchemeNotFound", $"Scheme {certificate.SchemeCode} not found.");
        }

        int stands = await _db.HousingStands.CountAsync(s => s.SchemeId == scheme.Id, ct).ConfigureAwait(false);
        if (certificate.StandsReached > stands)
        {
            return Error.Validation("Hse.ServicingStands", $"Scheme {scheme.Code} has {stands} stands.");
        }

        SchemeServicingProgress? last = await _db.Set<SchemeServicingProgress>().AsNoTracking().Where(p => p.SchemeId == scheme.Id && p.Service == certificate.Service)
            .OrderByDescending(p => p.Date).ThenByDescending(p => p.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (last is not null && (certificate.Date < last.Date || certificate.PercentComplete < last.PercentComplete || certificate.StandsReached < last.StandsReached))
        {
            return Error.Rule("Hse.ServicingBackwards", $"{certificate.Service} in {scheme.Code} was certified {last.PercentComplete:0.#}% reaching {last.StandsReached} stands on {last.Date:yyyy-MM-dd}; progress cannot go backwards.");
        }

        SchemeServicingProgress progress = new(certificate.CompanyId, scheme.Id, certificate.Service, certificate.Date, certificate.PercentComplete, certificate.StandsReached, certificate.Reference.Trim());
        _db.Set<SchemeServicingProgress>().Add(progress);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return progress.Id;
    }

    /// <inheritdoc />
    public async Task<Result<long>> RecordValuationAsync(long companyId, string parcelNumber, DateOnly date, decimal value, string valuer, CancellationToken ct = default)
    {
        if (value <= 0m || string.IsNullOrWhiteSpace(valuer))
        {
            return Error.Validation("Hse.ValuationDetails", "A valuation needs a positive value and the valuer.");
        }

        string number = parcelNumber.Trim().ToUpperInvariant();
        long? parcelId = await _db.LandParcels.Where(p => p.CompanyId == companyId && p.ParcelNumber == number).Select(p => (long?)p.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (parcelId is null)
        {
            return Error.NotFound("Hse.ParcelNotFound", $"Parcel {parcelNumber} not found.");
        }

        LandValuation valuation = new(companyId, parcelId.Value, date, value, valuer.Trim());
        _db.Set<LandValuation>().Add(valuation);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return valuation.Id;
    }
}
