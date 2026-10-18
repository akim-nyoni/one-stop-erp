using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Municipal;
using One4All.Application.Municipal;
using One4All.Core.Common;
using One4All.Core.Housing;
using One4All.Core.Municipal;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Housing;

/// <summary>Links housing stands to the municipal register at the configured event.</summary>
public sealed class HousingMunicipalLinkService : IHousingMunicipalLinkService
{
    private readonly One4AllDbContext _db;
    private readonly IOwnershipService _ownership;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="ownership">The municipal owner register.</param>
    public HousingMunicipalLinkService(One4AllDbContext db, IOwnershipService ownership)
    {
        _db = db;
        _ownership = ownership;
    }

    /// <inheritdoc />
    public async Task<Result> ConfigureAsync(long companyId, MunicipalLinkTrigger trigger, string ward, IReadOnlyList<long> tariffIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tariffIds);
        if (!Enum.IsDefined(trigger) || string.IsNullOrWhiteSpace(ward) || tariffIds.Distinct().Count() != tariffIds.Count)
        {
            return Result.Failure(Error.Validation("Hse.MunicipalLink", "Name the trigger and the ward, and list each tariff once."));
        }

        List<long> ids = [.. tariffIds];
        List<ServiceType> services = await _db.Tariffs.Where(t => t.CompanyId == companyId && ids.Contains(t.Id)).Select(t => t.ServiceType).ToListAsync(ct).ConfigureAwait(false);
        if (services.Count != ids.Count)
        {
            return Result.Failure(Error.NotFound("Hse.TariffNotFound", "A tariff is not on this council's tariff list."));
        }

        if (services.Distinct().Count() != services.Count)
        {
            return Result.Failure(Error.Validation("Hse.MunicipalLink", "Choose one tariff per service."));
        }

        MunicipalLinkSettings? settings = await _db.Set<MunicipalLinkSettings>().FirstOrDefaultAsync(s => s.CompanyId == companyId, ct).ConfigureAwait(false);
        if (settings is null)
        {
            _db.Set<MunicipalLinkSettings>().Add(new MunicipalLinkSettings(companyId, trigger, ward.Trim(), ids));
        }
        else
        {
            settings.Change(trigger, ward.Trim(), ids);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public Task<Result<MunicipalLinkResult>> OnEventAsync(long companyId, long standId, long holderCustomerId, MunicipalLinkTrigger occurred, DateOnly date, string? deedsReference, CancellationToken ct = default)
        => _db.AtomicAsync(() => OnEventCoreAsync(companyId, standId, holderCustomerId, occurred, date, deedsReference, ct), ct);

    private async Task<Result<MunicipalLinkResult>> OnEventCoreAsync(long companyId, long standId, long holderCustomerId, MunicipalLinkTrigger occurred, DateOnly date, string? deedsReference, CancellationToken ct)
    {
        MunicipalLinkSettings? settings = await _db.Set<MunicipalLinkSettings>().AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == companyId, ct).ConfigureAwait(false);
        if (settings is null || settings.Trigger != occurred)
        {
            return new MunicipalLinkResult(MunicipalLinkOutcome.NotTriggered, null);
        }

        HousingStand stand = await _db.HousingStands.AsNoTracking().FirstAsync(s => s.Id == standId && s.CompanyId == companyId, ct).ConfigureAwait(false);
        var existing = await _db.Properties.Where(p => p.CompanyId == companyId && p.StandNumber == stand.StandNumber).Select(p => new { p.Id, p.RatepayerCustomerId })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (existing is not null)
        {
            // Billed to someone else: the ratepayer moves only through a transfer with clearance, so the stand is reported (H98) rather than re-pointed here.
            return new MunicipalLinkResult(existing.RatepayerCustomerId == holderCustomerId ? MunicipalLinkOutcome.AlreadyLinked : MunicipalLinkOutcome.RatepayerDiffers, existing.Id);
        }

        // The stand's own ward when it has one; the link's default ward otherwise (thirteenth audit #14).
        Property property = new(companyId, stand.StandNumber, string.IsNullOrWhiteSpace(stand.Ward) ? settings.Ward : stand.Ward, holderCustomerId)
        {
            SizeSquareMetres = stand.SizeSquareMetres,
            Valuation = stand.SellingPrice > 0m ? stand.SellingPrice : stand.AllocatedCost,
        };
        List<long> tariffIds = settings.TariffIds;
        foreach (var tariff in await _db.Tariffs.Where(t => tariffIds.Contains(t.Id)).Select(t => new { t.Id, t.ServiceType }).ToListAsync(ct).ConfigureAwait(false))
        {
            property.AddService(tariff.ServiceType, tariff.Id, date); // billing pro-rates the first period from here
        }

        _db.Properties.Add(property);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        Result owners = await _ownership.RegisterOwnersAsync(companyId, property.Id, [new OwnerShare(holderCustomerId, 100m, true)], date, deedsReference, ct).ConfigureAwait(false);
        if (owners.IsFailure)
        {
            return owners.Error;
        }

        return new MunicipalLinkResult(MunicipalLinkOutcome.PropertyCreated, property.Id);
    }
}
