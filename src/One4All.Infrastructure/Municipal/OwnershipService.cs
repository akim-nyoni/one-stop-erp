using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Municipal;
using One4All.Application.Municipal;
using One4All.Core.Common;
using One4All.Core.Municipal;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Municipal;

/// <summary>The owner register and property transfers through rates clearance.</summary>
public sealed class OwnershipService : IOwnershipService
{
    private const string RegisterReference = "REGISTER";

    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public OwnershipService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public Task<Result> RegisterOwnersAsync(long companyId, long propertyId, IReadOnlyList<OwnerShare> owners, DateOnly ownedFrom, string? deedsReference, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(owners);
        return _db.AtomicAsync(() => RegisterCoreAsync(companyId, propertyId, owners, ownedFrom, deedsReference, ct), ct);
    }

    private async Task<Result> RegisterCoreAsync(long companyId, long propertyId, IReadOnlyList<OwnerShare> owners, DateOnly ownedFrom, string? deedsReference, CancellationToken ct)
    {
        Result<Property> property = await CheckAsync(companyId, propertyId, owners, ct).ConfigureAwait(false);
        if (property.IsFailure)
        {
            return Result.Failure(property.Error);
        }

        if (await _db.Set<PropertyOwner>().AnyAsync(o => o.PropertyId == propertyId, ct).ConfigureAwait(false))
        {
            return Result.Failure(Error.Conflict("Mun.OwnersRegistered", $"Stand {property.Value.StandNumber} already has owners; change them by transfer."));
        }

        Record(property.Value, owners, ownedFrom, RegisterReference, deedsReference);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public Task<Result> TransferAsync(long companyId, long propertyId, IReadOnlyList<OwnerShare> buyers, DateOnly transferDate, string clearanceNumber, string? deedsReference, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(buyers);
        return _db.AtomicAsync(() => TransferCoreAsync(companyId, propertyId, buyers, transferDate, clearanceNumber, deedsReference, ct), ct);
    }

    private async Task<Result> TransferCoreAsync(long companyId, long propertyId, IReadOnlyList<OwnerShare> buyers, DateOnly transferDate, string clearanceNumber, string? deedsReference, CancellationToken ct)
    {
        Result<Property> property = await CheckAsync(companyId, propertyId, buyers, ct).ConfigureAwait(false);
        if (property.IsFailure)
        {
            return Result.Failure(property.Error);
        }

        string number = (clearanceNumber ?? string.Empty).Trim();
        ClearanceCertificate? certificate = await _db.Set<ClearanceCertificate>().AsNoTracking()
            .FirstOrDefaultAsync(c => c.CompanyId == companyId && c.CertificateNumber == number && c.PropertyId == propertyId, ct).ConfigureAwait(false);
        if (certificate is null)
        {
            return Result.Failure(Error.NotFound("Mun.ClearanceNotFound", $"No clearance certificate {number} for stand {property.Value.StandNumber}."));
        }

        if (transferDate < certificate.IssuedOn || transferDate > certificate.ValidUntil)
        {
            return Result.Failure(Error.Rule("Mun.ClearanceExpired", $"Certificate {number} is valid from {certificate.IssuedOn:yyyy-MM-dd} to {certificate.ValidUntil:yyyy-MM-dd}."));
        }

        if (await _db.Set<PropertyOwner>().AnyAsync(o => o.CompanyId == companyId && o.Reference == number, ct).ConfigureAwait(false))
        {
            return Result.Failure(Error.Conflict("Mun.ClearanceUsed", $"Certificate {number} has already been used for a transfer."));
        }

        List<PropertyOwner> current = await _db.Set<PropertyOwner>().Where(o => o.PropertyId == propertyId && o.OwnedTo == null).ToListAsync(ct).ConfigureAwait(false);
        if (current.Any(o => o.OwnedFrom >= transferDate))
        {
            return Result.Failure(Error.Rule("Mun.TransferDate", "The transfer must be after the current owners' ownership began."));
        }

        foreach (PropertyOwner seller in current)
        {
            seller.End(transferDate.AddDays(-1));
        }

        Record(property.Value, buyers, transferDate, number, deedsReference);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OwnerEntry>> OwnersAsync(long companyId, long propertyId, DateOnly asAtDate, CancellationToken ct = default)
        => (await QueryAsync(companyId, propertyId, ct).ConfigureAwait(false))
            .Where(o => o.OwnedFrom <= asAtDate && (o.OwnedTo == null || o.OwnedTo >= asAtDate))
            .OrderByDescending(o => o.IsRatepayer).ThenByDescending(o => o.SharePercent).ToList();

    /// <inheritdoc />
    public async Task<IReadOnlyList<OwnerEntry>> HistoryAsync(long companyId, long propertyId, CancellationToken ct = default)
        => await QueryAsync(companyId, propertyId, ct).ConfigureAwait(false);

    private async Task<List<OwnerEntry>> QueryAsync(long companyId, long propertyId, CancellationToken ct)
        => await (
            from o in _db.Set<PropertyOwner>().AsNoTracking()
            join c in _db.Customers.AsNoTracking() on o.CustomerId equals c.Id
            where o.CompanyId == companyId && o.PropertyId == propertyId
            orderby o.OwnedFrom, o.Id
            select new OwnerEntry(c.Code, o.SharePercent, o.IsRatepayer, o.OwnedFrom, o.OwnedTo, o.Reference))
            .ToListAsync(ct).ConfigureAwait(false);

    private async Task<Result<Property>> CheckAsync(long companyId, long propertyId, IReadOnlyList<OwnerShare> owners, CancellationToken ct)
    {
        string? problem = OwnershipShares.Problem(owners);
        if (problem is not null)
        {
            return Error.Validation("Mun.Shares", problem);
        }

        Property? property = await _db.Properties.FirstOrDefaultAsync(p => p.Id == propertyId && p.CompanyId == companyId, ct).ConfigureAwait(false);
        if (property is null)
        {
            return Error.NotFound("Mun.PropertyNotFound", $"Property {propertyId} not found.");
        }

        List<long> ids = owners.Select(o => o.CustomerId).ToList();
        int known = await _db.Customers.CountAsync(c => c.CompanyId == companyId && ids.Contains(c.Id), ct).ConfigureAwait(false);
        if (known != ids.Count)
        {
            return Error.NotFound("Mun.OwnerNotFound", "An owner is not a customer of this council.");
        }

        return property;
    }

    private void Record(Property property, IReadOnlyList<OwnerShare> owners, DateOnly from, string reference, string? deedsReference)
    {
        foreach (OwnerShare owner in owners)
        {
            _db.Set<PropertyOwner>().Add(new PropertyOwner(property.CompanyId, property.Id, owner.CustomerId, owner.SharePercent, owner.IsRatepayer, from, reference, deedsReference?.Trim()));
        }

        property.RatepayerCustomerId = owners.Single(o => o.IsRatepayer).CustomerId;
    }
}
