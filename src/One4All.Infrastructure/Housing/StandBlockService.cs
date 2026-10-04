using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Security;
using One4All.Core.Common;
using One4All.Core.Housing;
using One4All.Core.Security;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Housing;

/// <summary>Caveats and blocks on stands, placed and lifted by different officers holding the caveat permission.</summary>
public sealed class StandBlockService : IStandBlockService
{
    private const string CaveatPermission = "Housing.Caveat";

    private readonly One4AllDbContext _db;
    private readonly IPermissionService _permissions;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="permissions">The permission resolver.</param>
    public StandBlockService(One4AllDbContext db, IPermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    /// <inheritdoc />
    public async Task<Result> PlaceAsync(long companyId, long standId, BlockReason reason, string detail, string documentReference, long userId, DateOnly date, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(reason) || string.IsNullOrWhiteSpace(detail) || string.IsNullOrWhiteSpace(documentReference))
        {
            return Result.Failure(Error.Validation("Hse.BlockDetails", "A block needs a reason, its particulars and a supporting document."));
        }

        if (!(await _permissions.ResolveAsync(userId, ct).ConfigureAwait(false)).Has(CaveatPermission, OperationFlags.Create))
        {
            return Result.Failure(Error.Rule("Hse.NotAuthorised", "Only an officer with the caveat permission can block a stand."));
        }

        HousingStand? stand = await _db.HousingStands.AsNoTracking().FirstOrDefaultAsync(s => s.Id == standId && s.CompanyId == companyId, ct).ConfigureAwait(false);
        if (stand is null)
        {
            return Result.Failure(Error.NotFound("Hse.StandNotFound", $"Stand {standId} not found."));
        }

        if (await _db.Set<StandBlock>().AnyAsync(b => b.StandId == standId && b.LiftedOn == null, ct).ConfigureAwait(false))
        {
            return Result.Failure(Error.Conflict("Hse.AlreadyBlocked", $"Stand {stand.StandNumber} is already blocked."));
        }

        _db.Set<StandBlock>().Add(new StandBlock(companyId, standId, reason, detail.Trim(), documentReference.Trim(), userId, date));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> LiftAsync(long companyId, long standId, string reason, string documentReference, long userId, DateOnly date, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason) || string.IsNullOrWhiteSpace(documentReference))
        {
            return Result.Failure(Error.Validation("Hse.LiftDetails", "Lifting a block needs a reason and a supporting document."));
        }

        if (!(await _permissions.ResolveAsync(userId, ct).ConfigureAwait(false)).Has(CaveatPermission, OperationFlags.Delete))
        {
            return Result.Failure(Error.Rule("Hse.NotAuthorised", "Only an officer with the caveat permission can lift a block."));
        }

        StandBlock? block = await _db.Set<StandBlock>().FirstOrDefaultAsync(b => b.CompanyId == companyId && b.StandId == standId && b.LiftedOn == null, ct).ConfigureAwait(false);
        if (block is null)
        {
            return Result.Failure(Error.NotFound("Hse.NotBlocked", $"Stand {standId} has no block in force."));
        }

        if (block.PlacedByUserId == userId)
        {
            return Result.Failure(Error.Rule("Hse.SelfLift", "A block must be lifted by an officer other than the one who placed it (SOD-HSE-04)."));
        }

        if (date < block.PlacedOn)
        {
            return Result.Failure(Error.Validation("Hse.LiftDate", "A block cannot be lifted before it was placed."));
        }

        block.Lift(userId, date, reason.Trim(), documentReference.Trim());
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<StandBlockEntry>> HistoryAsync(long companyId, long standId, CancellationToken ct = default)
        => ListAsync(companyId, b => b.StandId == standId, ct);

    /// <inheritdoc />
    public Task<IReadOnlyList<StandBlockEntry>> InForceAsync(long companyId, CancellationToken ct = default)
        => ListAsync(companyId, b => b.LiftedOn == null, ct);

    private async Task<IReadOnlyList<StandBlockEntry>> ListAsync(long companyId, System.Linq.Expressions.Expression<Func<StandBlock, bool>> filter, CancellationToken ct)
    {
        var rows = await (
            from b in _db.Set<StandBlock>().AsNoTracking().Where(filter)
            join s in _db.HousingStands.AsNoTracking() on b.StandId equals s.Id
            where b.CompanyId == companyId
            orderby s.StandNumber, b.PlacedOn, b.Id
            select new { s.StandNumber, Block = b }).ToListAsync(ct).ConfigureAwait(false);
        return [.. rows.Select(r => new StandBlockEntry(r.StandNumber, r.Block.Reason, r.Block.Detail, r.Block.DocumentReference, r.Block.PlacedByUserId, r.Block.PlacedOn,
            r.Block.LiftedByUserId, r.Block.LiftedOn, r.Block.LiftReason))];
    }
}
