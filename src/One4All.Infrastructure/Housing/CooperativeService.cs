using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Housing;
using One4All.Core.Common;
using One4All.Core.Housing;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Housing;

/// <summary>Cooperative allocations: stands to cooperatives, then to their members.</summary>
public sealed class CooperativeService : ICooperativeService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public CooperativeService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result<long>> RegisterAsync(long companyId, string code, string name, string registrationNumber, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(registrationNumber))
        {
            return Error.Validation("Hse.CooperativeDetails", "A cooperative needs a code, a name and a registration number.");
        }

        string key = code.Trim().ToUpperInvariant();
        if (await _db.Set<Cooperative>().AnyAsync(c => c.CompanyId == companyId && c.Code == key, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Hse.CooperativeExists", $"Cooperative {key} already exists.");
        }

        Cooperative cooperative = new(companyId, key, name.Trim(), registrationNumber.Trim());
        _db.Set<Cooperative>().Add(cooperative);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return cooperative.Id;
    }

    /// <inheritdoc />
    public async Task<Result> AddMemberAsync(long companyId, string cooperativeCode, string idNumber, string fullName, DateOnly joinedOn, CancellationToken ct = default)
    {
        string id = NormaliseId(idNumber);
        if (id.Length == 0 || string.IsNullOrWhiteSpace(fullName))
        {
            return Result.Failure(Error.Validation("Hse.MemberDetails", "A member needs a national ID and a name."));
        }

        Result<Cooperative> found = await FindAsync(companyId, cooperativeCode, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        if (found.Value.Members.Any(m => m.IdNumber == id))
        {
            return Result.Failure(Error.Conflict("Hse.MemberExists", $"ID {id} is already a member of {found.Value.Code}."));
        }

        found.Value.AddMember(new CooperativeMember(id, fullName.Trim(), joinedOn));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> RemoveMemberAsync(long companyId, string cooperativeCode, string idNumber, CancellationToken ct = default)
    {
        Result<Cooperative> found = await FindAsync(companyId, cooperativeCode, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        CooperativeMember? member = found.Value.Members.FirstOrDefault(m => m.IdNumber == NormaliseId(idNumber) && m.IsActive);
        if (member is null)
        {
            return Result.Failure(Error.NotFound("Hse.MemberNotFound", $"No active member {NormaliseId(idNumber)} in {found.Value.Code}."));
        }

        member.Leave();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public Task<Result<int>> AllocateStandsAsync(long companyId, string cooperativeCode, IReadOnlyList<long> standIds, DateOnly date, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(standIds);
        return _db.AtomicAsync(() => AllocateCoreAsync(companyId, cooperativeCode, standIds, date, ct), ct);
    }

    private async Task<Result<int>> AllocateCoreAsync(long companyId, string cooperativeCode, IReadOnlyList<long> standIds, DateOnly date, CancellationToken ct)
    {
        if (standIds.Count == 0 || standIds.Distinct().Count() != standIds.Count)
        {
            return Error.Validation("Hse.CooperativeStands", "List each stand once.");
        }

        Result<Cooperative> found = await FindAsync(companyId, cooperativeCode, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        List<HousingStand> stands = await _db.HousingStands.Where(s => s.CompanyId == companyId && standIds.Contains(s.Id)).ToListAsync(ct).ConfigureAwait(false);
        if (stands.Count != standIds.Count)
        {
            return Error.NotFound("Hse.StandNotFound", "A stand is not on the register.");
        }

        HousingStand? unavailable = stands.FirstOrDefault(s => s.Status != StandStatus.Available);
        if (unavailable is not null || await StandClaims.AnyClaimedAsync(_db, standIds, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Hse.StandUnavailable", "Every stand must be available and not already allocated.");
        }

        foreach (HousingStand stand in stands)
        {
            stand.MarkAllocated();
            _db.Set<CooperativeStand>().Add(new CooperativeStand(companyId, found.Value.Id, stand.Id, date));
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return stands.Count;
    }

    /// <inheritdoc />
    public async Task<Result> SubAllocateAsync(long companyId, string cooperativeCode, long standId, string memberIdNumber, DateOnly date, CancellationToken ct = default)
    {
        Result<Cooperative> found = await FindAsync(companyId, cooperativeCode, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        Cooperative cooperative = found.Value;
        CooperativeStand? holding = await _db.Set<CooperativeStand>().FirstOrDefaultAsync(c => c.CooperativeId == cooperative.Id && c.StandId == standId, ct).ConfigureAwait(false);
        if (holding is null)
        {
            return Result.Failure(Error.Rule("Hse.NotCooperativeStand", $"The stand is not allocated to {cooperative.Code}."));
        }

        if (holding.MemberId is not null)
        {
            return Result.Failure(Error.Conflict("Hse.StandSubAllocated", "The stand has already been sub-allocated."));
        }

        CooperativeMember? member = cooperative.Members.FirstOrDefault(m => m.IdNumber == NormaliseId(memberIdNumber) && m.IsActive);
        if (member is null)
        {
            return Result.Failure(Error.Rule("Hse.NotActiveMember", $"{NormaliseId(memberIdNumber)} is not an active member of {cooperative.Code}."));
        }

        if (await _db.Set<CooperativeStand>().AnyAsync(c => c.MemberId == member.Id, ct).ConfigureAwait(false))
        {
            return Result.Failure(Error.Conflict("Hse.MemberHoldsStand", $"{member.FullName} already holds a stand."));
        }

        holding.SubAllocate(member.Id, date);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<CooperativeStandEntry>>> StandsAsync(long companyId, string cooperativeCode, CancellationToken ct = default)
    {
        Result<Cooperative> found = await FindAsync(companyId, cooperativeCode, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        long cooperativeId = found.Value.Id;
        Dictionary<long, string> members = found.Value.Members.ToDictionary(m => m.Id, m => m.FullName);
        var rows = await (
            from c in _db.Set<CooperativeStand>().AsNoTracking()
            join s in _db.HousingStands.AsNoTracking() on c.StandId equals s.Id
            where c.CooperativeId == cooperativeId
            orderby s.StandNumber
            select new { s.StandNumber, c.AllocatedOn, c.MemberId, c.SubAllocatedOn }).ToListAsync(ct).ConfigureAwait(false);
        return Result<IReadOnlyList<CooperativeStandEntry>>.Success([.. rows.Select(r =>
            new CooperativeStandEntry(r.StandNumber, r.AllocatedOn, r.MemberId is long id ? members.GetValueOrDefault(id) : null, r.SubAllocatedOn))]);
    }

    private static string NormaliseId(string? value) => (value ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal).Trim().ToUpperInvariant();

    private async Task<Result<Cooperative>> FindAsync(long companyId, string cooperativeCode, CancellationToken ct)
    {
        string key = (cooperativeCode ?? string.Empty).Trim().ToUpperInvariant();
        Cooperative? cooperative = await _db.Set<Cooperative>().Include(c => c.Members)
            .FirstOrDefaultAsync(c => c.CompanyId == companyId && c.Code == key, ct).ConfigureAwait(false);
        if (cooperative is null)
        {
            return Error.NotFound("Hse.CooperativeNotFound", $"Cooperative {key} not found.");
        }

        return cooperative;
    }
}
