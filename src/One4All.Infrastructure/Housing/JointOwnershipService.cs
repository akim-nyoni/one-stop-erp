using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Housing;
using One4All.Core.Common;
using One4All.Core.Housing;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Housing;

/// <summary>Joint ownership of stands: shares, share transfers, survivorship and the holders on any date.</summary>
public sealed class JointOwnershipService : IJointOwnershipService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public JointOwnershipService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result> RegisterAsync(long companyId, long standId, CoOwnershipRegime regime, IReadOnlyList<HolderShare> holders, DateOnly date, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(holders);
        string? problem = Enum.IsDefined(regime) ? JointHolding.Problem(holders) : "Name the co-ownership regime.";
        if (problem is not null)
        {
            return Result.Failure(Error.Validation("Hse.Holders", problem));
        }

        HousingStand? stand = await _db.HousingStands.AsNoTracking().FirstOrDefaultAsync(s => s.Id == standId && s.CompanyId == companyId, ct).ConfigureAwait(false);
        if (stand is null)
        {
            return Result.Failure(Error.NotFound("Hse.StandNotFound", $"Stand {standId} not found."));
        }

        if (stand.CurrentHolderCustomerId is not long holderId || holders.All(h => h.CustomerId != holderId))
        {
            return Result.Failure(Error.Rule("Hse.HolderNotListed", $"Stand {stand.StandNumber}'s current holder must be one of the joint holders."));
        }

        List<long> ids = [.. holders.Select(h => h.CustomerId)];
        if (await _db.Customers.CountAsync(c => c.CompanyId == companyId && ids.Contains(c.Id), ct).ConfigureAwait(false) != ids.Count)
        {
            return Result.Failure(Error.NotFound("Hse.HolderNotFound", "Every holder needs a customer account."));
        }

        if (await _db.Set<StandCoOwnership>().AnyAsync(c => c.StandId == standId, ct).ConfigureAwait(false))
        {
            return Result.Failure(Error.Conflict("Hse.JointRegistered", $"Stand {stand.StandNumber} already has joint holders; change them by transfer or survivorship."));
        }

        _db.Set<StandCoOwnership>().Add(new StandCoOwnership(companyId, standId, regime));
        foreach (HolderShare holder in holders)
        {
            _db.Set<StandHolding>().Add(new StandHolding(companyId, standId, holder.CustomerId, holder.SharePercent, date, "Registered"));
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public Task<Result> TransferShareAsync(long companyId, long standId, long fromCustomerId, long toCustomerId, decimal portion, DateOnly date, CancellationToken ct = default)
        => _db.AtomicAsync(() => TransferShareCoreAsync(companyId, standId, fromCustomerId, toCustomerId, portion, date, ct), ct);

    private async Task<Result> TransferShareCoreAsync(long companyId, long standId, long fromCustomerId, long toCustomerId, decimal portion, DateOnly date, CancellationToken ct)
    {
        Result<Holding> found = await CurrentAsync(companyId, standId, date, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        if (await StandClaims.AnyBlockedAsync(_db, [standId], ct).ConfigureAwait(false))
        {
            return Result.Failure(Error.Rule("Hse.StandBlocked", "The stand is blocked by a caveat; no share can be transferred."));
        }

        if (!await _db.Customers.AnyAsync(c => c.Id == toCustomerId && c.CompanyId == companyId, ct).ConfigureAwait(false))
        {
            return Result.Failure(Error.NotFound("Hse.HolderNotFound", $"Customer {toCustomerId} not found."));
        }

        Holding holding = found.Value;
        IReadOnlyList<HolderShare>? after = JointHolding.TransferShare(Shares(holding), fromCustomerId, toCustomerId, portion);
        if (after is null)
        {
            return Result.Failure(Error.Rule("Hse.ShareTransfer", "The transferor must be a holder with at least the share transferred, to someone else."));
        }

        holding.CoOwnership.Sever(); // a share sold on its own ends any right of survivorship
        Replace(holding, after, date, $"Share transfer {portion:0.####}% from {fromCustomerId} to {toCustomerId}");
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<StandHolderEntry>>> RecordDeathAsync(long companyId, long standId, long deceasedCustomerId, DateOnly date, CancellationToken ct = default)
        => _db.AtomicAsync(() => RecordDeathCoreAsync(companyId, standId, deceasedCustomerId, date, ct), ct);

    private async Task<Result<IReadOnlyList<StandHolderEntry>>> RecordDeathCoreAsync(long companyId, long standId, long deceasedCustomerId, DateOnly date, CancellationToken ct)
    {
        Result<Holding> found = await CurrentAsync(companyId, standId, date, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        Holding holding = found.Value;
        StandHolding? deceased = holding.Rows.FirstOrDefault(r => r.CustomerId == deceasedCustomerId);
        if (deceased is null || deceased.HolderDeceased)
        {
            return Error.Rule("Hse.NotAHolder", $"Customer {deceasedCustomerId} is not a living holder of the stand.");
        }

        if (holding.CoOwnership.Regime == CoOwnershipRegime.JointWithSurvivorship && holding.Rows.Count > 1)
        {
            Replace(holding, JointHolding.OnDeath(Shares(holding), deceasedCustomerId, CoOwnershipRegime.JointWithSurvivorship)!, date.AddDays(1), $"Survivorship on the death of {deceasedCustomerId}");
        }
        else
        {
            deceased.MarkDeceased(); // the share sits with the estate until transferred by inheritance
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result<IReadOnlyList<StandHolderEntry>>.Success(await HoldersAsync(companyId, standId, date.AddDays(1), ct).ConfigureAwait(false));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StandHolderEntry>> HoldersAsync(long companyId, long standId, DateOnly asAt, CancellationToken ct = default)
    {
        var rows = await (
            from h in _db.Set<StandHolding>().AsNoTracking()
            join c in _db.Customers.AsNoTracking() on h.CustomerId equals c.Id
            where h.CompanyId == companyId && h.StandId == standId && h.HeldFrom <= asAt && (h.HeldTo == null || h.HeldTo >= asAt)
            select new { h.CustomerId, c.Name, h.SharePercent, h.HeldFrom, h.HolderDeceased }).ToListAsync(ct).ConfigureAwait(false);
        return [.. rows.OrderByDescending(r => r.SharePercent).ThenBy(r => r.Name, StringComparer.Ordinal)
            .Select(r => new StandHolderEntry(r.CustomerId, r.Name, r.SharePercent, r.HeldFrom, r.HolderDeceased))];
    }

    // Ends the current periods the day before and opens the new shares; an estate keeps its deceased marker.
    private void Replace(Holding holding, IReadOnlyList<HolderShare> after, DateOnly from, string reason)
    {
        HashSet<long> deceased = [.. holding.Rows.Where(r => r.HolderDeceased).Select(r => r.CustomerId)];
        foreach (StandHolding row in holding.Rows)
        {
            row.End(from.AddDays(-1));
        }

        foreach (HolderShare share in after)
        {
            StandHolding row = new(holding.CoOwnership.CompanyId, holding.CoOwnership.StandId, share.CustomerId, share.SharePercent, from, reason);
            if (deceased.Contains(share.CustomerId))
            {
                row.MarkDeceased();
            }

            _db.Set<StandHolding>().Add(row);
        }
    }

    private static List<HolderShare> Shares(Holding holding) => [.. holding.Rows.Select(r => new HolderShare(r.CustomerId, r.SharePercent))];

    private async Task<Result<Holding>> CurrentAsync(long companyId, long standId, DateOnly date, CancellationToken ct)
    {
        StandCoOwnership? coOwnership = await _db.Set<StandCoOwnership>().FirstOrDefaultAsync(c => c.CompanyId == companyId && c.StandId == standId, ct).ConfigureAwait(false);
        if (coOwnership is null)
        {
            return Error.NotFound("Hse.NotJointlyHeld", $"Stand {standId} has no joint holders registered.");
        }

        List<StandHolding> rows = await _db.Set<StandHolding>().Where(h => h.StandId == standId && h.HeldTo == null).OrderBy(h => h.Id).ToListAsync(ct).ConfigureAwait(false);
        if (rows.Any(r => r.HeldFrom >= date))
        {
            return Error.Validation("Hse.HoldingDate", "A change must be dated after the holders' current shares began.");
        }

        return new Holding(coOwnership, rows);
    }

    private sealed record Holding(StandCoOwnership CoOwnership, List<StandHolding> Rows);
}
