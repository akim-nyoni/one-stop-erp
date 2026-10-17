using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Housing;
using One4All.Core.Common;
using One4All.Core.Housing;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Housing;

/// <summary>Occupants and lodger permits on council rental units (brief §4.10.7).</summary>
public sealed class RentalOccupancyService : IRentalOccupancyService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public RentalOccupancyService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result> SetMaxOccupantsAsync(long companyId, string unitCode, int? maxOccupants, CancellationToken ct = default)
    {
        if (maxOccupants is < 1)
        {
            return Result.Failure(Error.Validation("Hse.MaxOccupants", "An occupancy limit is at least one person."));
        }

        string code = (unitCode ?? string.Empty).Trim().ToUpperInvariant();
        RentalUnit? unit = await _db.Set<RentalUnit>().FirstOrDefaultAsync(u => u.CompanyId == companyId && u.Code.ToUpper() == code, ct).ConfigureAwait(false);
        if (unit is null)
        {
            return Result.Failure(Error.NotFound("Hse.UnitNotFound", $"Rental unit {code} not found."));
        }

        unit.MaxOccupants = maxOccupants;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<long>> AddOccupantAsync(long companyId, string tenancyNumber, string fullName, string? idNumber, string relationship, DateOnly movedInOn, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(relationship))
        {
            return Error.Validation("Hse.Occupant", "An occupant needs a name and their relationship to the tenant.");
        }

        Result<(Tenancy Tenancy, RentalUnit Unit)> found = await ActiveAsync(companyId, tenancyNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        (Tenancy tenancy, RentalUnit unit) = found.Value;
        if (movedInOn < tenancy.StartDate)
        {
            return Error.Validation("Hse.OccupantDate", "An occupant cannot move in before the tenancy starts.");
        }

        Error? full = await OverLimitAsync(tenancy, unit, movedInOn, 1, ct).ConfigureAwait(false);
        if (full is not null)
        {
            return full;
        }

        TenancyOccupant occupant = tenancy.AddOccupant(new TenancyOccupant(fullName.Trim(), Clean(idNumber), relationship.Trim(), movedInOn));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return occupant.Id;
    }

    /// <inheritdoc />
    public async Task<Result> RemoveOccupantAsync(long companyId, string tenancyNumber, long occupantId, DateOnly movedOutOn, CancellationToken ct = default)
    {
        Result<(Tenancy Tenancy, RentalUnit Unit)> found = await ActiveAsync(companyId, tenancyNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        TenancyOccupant? occupant = found.Value.Tenancy.Occupants.FirstOrDefault(o => o.Id == occupantId && o.MovedOutOn is null);
        if (occupant is null || movedOutOn <= occupant.MovedInOn)
        {
            return Result.Failure(Error.NotFound("Hse.OccupantNotFound", $"No occupant {occupantId} living in tenancy {found.Value.Tenancy.TenancyNumber} before {movedOutOn:yyyy-MM-dd}."));
        }

        occupant.MoveOut(movedOutOn);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<string>> IssueLodgerPermitAsync(LodgerPermitRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.LodgerName) || request.Rooms < 1 || request.ExpiresOn < request.IssuedOn || request.MonthlyFee < 0m
            || (request.MonthlyFee > 0m && string.IsNullOrWhiteSpace(request.FeeAccountCode)))
        {
            return Error.Validation("Hse.LodgerPermit", "A lodger permit needs the lodger, at least one room, an expiry on or after its start, and an income account with a fee.");
        }

        Result<(Tenancy Tenancy, RentalUnit Unit)> found = await ActiveAsync(request.CompanyId, request.TenancyNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        (Tenancy tenancy, RentalUnit unit) = found.Value;
        List<LodgerPermit> permits = await _db.Set<LodgerPermit>().Where(p => p.TenancyId == tenancy.Id && p.Status == LodgerPermitStatus.Active).ToListAsync(ct).ConfigureAwait(false);
        int roomsLet = permits.Where(p => p.InForceBetween(request.IssuedOn, request.ExpiresOn)).Sum(p => p.Rooms);
        if (roomsLet + request.Rooms > unit.Rooms - 1)
        {
            return Error.Rule("Hse.LodgerRooms", $"Unit {unit.Code} has {unit.Rooms} room(s) and {roomsLet} already let to lodgers; the tenant must keep a room of their own.");
        }

        Error? full = await OverLimitAsync(tenancy, unit, request.IssuedOn, 1, ct).ConfigureAwait(false);
        if (full is not null)
        {
            return full;
        }

        int count = await _db.Set<LodgerPermit>().CountAsync(p => p.TenancyId == tenancy.Id, ct).ConfigureAwait(false);
        string number = $"LDG-{tenancy.TenancyNumber}-{count + 1}";
        _db.Set<LodgerPermit>().Add(new LodgerPermit(request.CompanyId, number, tenancy.Id, request.LodgerName.Trim(), Clean(request.IdNumber), request.Rooms, request.MonthlyFee,
            request.FeeAccountCode?.Trim(), request.IssuedOn, request.ExpiresOn));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return number;
    }

    /// <inheritdoc />
    public async Task<Result> RevokeLodgerPermitAsync(long companyId, string permitNumber, DateOnly lastDay, string reason, CancellationToken ct = default)
    {
        string number = (permitNumber ?? string.Empty).Trim().ToUpperInvariant();
        LodgerPermit? permit = await _db.Set<LodgerPermit>().FirstOrDefaultAsync(p => p.CompanyId == companyId && p.PermitNumber == number && p.Status == LodgerPermitStatus.Active, ct)
            .ConfigureAwait(false);
        if (permit is null)
        {
            return Result.Failure(Error.NotFound("Hse.PermitNotFound", $"No lodger permit {number} in force."));
        }

        if (string.IsNullOrWhiteSpace(reason) || lastDay < permit.IssuedOn)
        {
            return Result.Failure(Error.Validation("Hse.PermitRevoke", "Revoking a permit needs a reason and a last day on or after it was issued."));
        }

        permit.Revoke(lastDay, reason.Trim());
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<TenancyOccupancy>> OccupancyAsync(long companyId, string tenancyNumber, DateOnly date, CancellationToken ct = default)
    {
        string number = (tenancyNumber ?? string.Empty).Trim();
        Tenancy? tenancy = await _db.Set<Tenancy>().AsNoTracking().Include(t => t.Occupants).FirstOrDefaultAsync(t => t.CompanyId == companyId && t.TenancyNumber == number, ct).ConfigureAwait(false);
        if (tenancy is null)
        {
            return Error.NotFound("Hse.TenancyNotFound", $"Tenancy {number} not found.");
        }

        RentalUnit unit = await _db.Set<RentalUnit>().AsNoTracking().FirstAsync(u => u.Id == tenancy.UnitId, ct).ConfigureAwait(false);
        List<string> occupants = [.. tenancy.Occupants.Where(o => o.MovedInOn <= date && (o.MovedOutOn is null || o.MovedOutOn > date)).Select(o => o.FullName)];
        List<string> lodgers = [.. (await _db.Set<LodgerPermit>().AsNoTracking().Where(p => p.TenancyId == tenancy.Id).ToListAsync(ct).ConfigureAwait(false))
            .Where(p => p.InForceOn(date)).Select(p => p.LodgerName)];
        bool tenantThere = tenancy.StartDate <= date && (tenancy.EndDate is null || tenancy.EndDate >= date);
        return new TenancyOccupancy(tenancy.TenancyNumber, unit.Code, occupants, lodgers, (tenantThere ? 1 : 0) + occupants.Count + lodgers.Count, unit.MaxOccupants);
    }

    // Everyone living there on the date - the tenant, occupants and lodgers - plus those arriving, against the unit's limit.
    private async Task<Error?> OverLimitAsync(Tenancy tenancy, RentalUnit unit, DateOnly date, int arriving, CancellationToken ct)
    {
        if (unit.MaxOccupants is not int max)
        {
            return null;
        }

        int lodgers = (await _db.Set<LodgerPermit>().Where(p => p.TenancyId == tenancy.Id && p.Status == LodgerPermitStatus.Active).ToListAsync(ct).ConfigureAwait(false))
            .Count(p => p.InForceOn(date));
        int people = 1 + tenancy.OccupantsOn(date) + lodgers;
        return people + arriving > max ? Error.Rule("Hse.Overcrowded", $"Unit {unit.Code} may house {max}; {people} already live there.") : null;
    }

    private async Task<Result<(Tenancy Tenancy, RentalUnit Unit)>> ActiveAsync(long companyId, string tenancyNumber, CancellationToken ct)
    {
        string number = (tenancyNumber ?? string.Empty).Trim();
        Tenancy? tenancy = await _db.Set<Tenancy>().Include(t => t.Occupants).FirstOrDefaultAsync(t => t.CompanyId == companyId && t.TenancyNumber == number, ct).ConfigureAwait(false);
        if (tenancy is null)
        {
            return Error.NotFound("Hse.TenancyNotFound", $"Tenancy {number} not found.");
        }

        if (tenancy.Status != TenancyStatus.Active)
        {
            return Error.Rule("Hse.TenancyEnded", $"Tenancy {number} has ended.");
        }

        RentalUnit unit = await _db.Set<RentalUnit>().FirstAsync(u => u.Id == tenancy.UnitId, ct).ConfigureAwait(false);
        return (tenancy, unit);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
}
