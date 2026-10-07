using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Housing;
using One4All.Core.Common;
using One4All.Core.Housing;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Housing;

/// <summary>The stand status state machine, consolidation and re-subdivision (brief §4.10.2).</summary>
public sealed class StandLifecycleService : IStandLifecycleService
{
    // These moves belong to their own documents (allocation run / offer, sale, cession, repossession, consolidation, re-subdivision).
    private static readonly HashSet<StandStatus> DocumentOnly =
        [StandStatus.Allocated, StandStatus.Sold, StandStatus.Transferred, StandStatus.ConsolidatedAway, StandStatus.ReSubdivided];

    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public StandLifecycleService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result<StandStatus>> MoveAsync(long companyId, string standNumber, StandStatus to, DateOnly date, long userId, string authority, string? reason, CancellationToken ct = default)
    {
        Result<HousingStand> found = await FindAsync(companyId, standNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        HousingStand stand = found.Value;
        bool release = stand.Status == StandStatus.Withheld && to == stand.StatusBeforeHold;
        if (!release && (DocumentOnly.Contains(to) || (to == StandStatus.Available && stand.Status is StandStatus.Allocated or StandStatus.Sold or StandStatus.Transferred)))
        {
            return Error.Rule("Hse.StandMoveByDocument", $"Stand {stand.StandNumber} goes to {to} through its own process (allocation, sale, cession, repossession, consolidation or re-subdivision).");
        }

        Result moved = stand.MoveTo(to, date, userId, authority, reason);
        if (moved.IsFailure)
        {
            return moved.Error;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return stand.Status;
    }

    /// <inheritdoc />
    public Task<Result<string>> ConsolidateAsync(long companyId, IReadOnlyList<string> standNumbers, string newStandNumber, DateOnly date, long userId, string authority, CancellationToken ct = default)
        => _db.AtomicAsync(async () =>
        {
            List<string> numbers = [.. standNumbers.Select(Normalise).Where(n => n.Length > 0).Distinct(StringComparer.Ordinal)];
            string number = Normalise(newStandNumber);
            if (numbers.Count < 2 || number.Length == 0 || string.IsNullOrWhiteSpace(authority))
            {
                return Error.Validation("Hse.Consolidation", "A consolidation needs at least two stands, the new stand number and the authority for it.");
            }

            List<HousingStand> stands = await _db.HousingStands.Where(s => s.CompanyId == companyId && numbers.Contains(s.StandNumber)).ToListAsync(ct).ConfigureAwait(false);
            if (stands.Count != numbers.Count)
            {
                return Error.NotFound("Hse.StandNotFound", $"Not found: {string.Join(", ", numbers.Except(stands.Select(s => s.StandNumber)))}.");
            }

            Result free = await CheckFreeAsync(stands, ct).ConfigureAwait(false);
            if (free.IsFailure)
            {
                return free.Error;
            }

            if (stands.Select(s => s.SchemeId).Distinct().Count() > 1 || stands.Select(s => s.Status).Distinct().Count() > 1)
            {
                return Error.Rule("Hse.ConsolidationMix", "Only stands of one scheme, at the same stage, can be consolidated.");
            }

            if (await _db.HousingStands.AnyAsync(s => s.CompanyId == companyId && s.StandNumber == number, ct).ConfigureAwait(false))
            {
                return Error.Conflict("Hse.StandExists", $"Stand {number} already exists.");
            }

            HousingStand first = stands.OrderBy(s => s.StandNumber, StringComparer.Ordinal).First();
            HousingStand merged = new(companyId, first.LandParcelId, first.SchemeId, number, stands.Sum(s => s.SizeSquareMetres), 0m, date, first.Status);
            _db.HousingStands.Add(merged);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            decimal cost = stands.Sum(s => s.ConsolidateInto(merged.Id, date, userId, authority.Trim()));
            merged.AddServicingCost(cost);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result<string>.Success(merged.StandNumber);
        }, ct);

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<string>>> ResubdivideAsync(long companyId, string standNumber, IReadOnlyList<StandPart> parts, DateOnly date, long userId, string authority, CancellationToken ct = default)
        => _db.AtomicAsync(async () =>
        {
            Result<HousingStand> found = await FindAsync(companyId, standNumber, ct).ConfigureAwait(false);
            if (found.IsFailure)
            {
                return found.Error;
            }

            HousingStand parent = found.Value;
            List<StandPart> clean = [.. parts.Select(p => new StandPart(Normalise(p.StandNumber), p.SizeSquareMetres))];
            if (clean.Count < 2 || string.IsNullOrWhiteSpace(authority) || clean.Any(p => p.StandNumber.Length == 0 || p.SizeSquareMetres <= 0m)
                || clean.Select(p => p.StandNumber).Distinct(StringComparer.Ordinal).Count() != clean.Count)
            {
                return Error.Validation("Hse.Resubdivision", "A re-subdivision needs at least two new stands with distinct numbers and sizes, and the authority for it.");
            }

            if (Math.Abs(clean.Sum(p => p.SizeSquareMetres) - parent.SizeSquareMetres) > 0.01m)
            {
                return Error.Rule("Hse.ResubdivisionArea", $"The new stands come to {clean.Sum(p => p.SizeSquareMetres):0.##} m², not the {parent.SizeSquareMetres:0.##} m² of {parent.StandNumber}.");
            }

            Result free = await CheckFreeAsync([parent], ct).ConfigureAwait(false);
            if (free.IsFailure)
            {
                return free.Error;
            }

            List<string> numbers = [.. clean.Select(p => p.StandNumber)];
            List<string> taken = await _db.HousingStands.Where(s => s.CompanyId == companyId && numbers.Contains(s.StandNumber)).Select(s => s.StandNumber).ToListAsync(ct).ConfigureAwait(false);
            if (taken.Count > 0)
            {
                return Error.Conflict("Hse.StandExists", $"Already exist: {string.Join(", ", taken)}.");
            }

            // Cost split by area; the last stand takes the rounding.
            decimal cost = parent.AllocatedCost, given = 0m;
            List<HousingStand> children = [];
            for (int i = 0; i < clean.Count; i++)
            {
                decimal share = i == clean.Count - 1 ? cost - given : decimal.Round(cost * clean[i].SizeSquareMetres / parent.SizeSquareMetres, 2, MidpointRounding.AwayFromZero);
                given += share;
                HousingStand child = new(companyId, parent.LandParcelId, parent.SchemeId, clean[i].StandNumber, clean[i].SizeSquareMetres, share, date, parent.Status);
                child.SetParent(parent.Id);
                children.Add(child);
            }

            _db.HousingStands.AddRange(children);
            parent.Resubdivide(date, userId, authority.Trim(), numbers);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result<IReadOnlyList<string>>.Success(numbers);
        }, ct);

    /// <inheritdoc />
    public async Task<Result<StandHistory>> HistoryAsync(long companyId, string standNumber, CancellationToken ct = default)
    {
        string number = Normalise(standNumber);
        HousingStand? stand = await _db.HousingStands.AsNoTracking().Include(s => s.StatusChanges).FirstOrDefaultAsync(s => s.CompanyId == companyId && s.StandNumber == number, ct).ConfigureAwait(false);
        if (stand is null)
        {
            return Error.NotFound("Hse.StandNotFound", $"Stand {number} not found.");
        }

        var all = await _db.HousingStands.AsNoTracking().Where(s => s.CompanyId == companyId)
            .Select(s => new { s.Id, s.StandNumber, s.ParentStandId, s.ConsolidatedIntoStandId }).ToListAsync(ct).ConfigureAwait(false);
        List<string> lineage = [];
        void Walk(long id, int depth)
        {
            var self = all.First(s => s.Id == id);
            if (self.ParentStandId is long parentId && all.FirstOrDefault(s => s.Id == parentId) is { } parent)
            {
                lineage.Add($"{new string(' ', depth * 2)}{self.StandNumber} re-subdivided from {parent.StandNumber}");
                Walk(parent.Id, depth + 1);
            }

            foreach (var source in all.Where(s => s.ConsolidatedIntoStandId == id).OrderBy(s => s.StandNumber, StringComparer.Ordinal))
            {
                lineage.Add($"{new string(' ', depth * 2)}{self.StandNumber} consolidated from {source.StandNumber}");
                Walk(source.Id, depth + 1);
            }
        }

        Walk(stand.Id, 0);
        string parcel = await _db.LandParcels.AsNoTracking().Where(p => p.Id == stand.LandParcelId).Select(p => p.ParcelNumber).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? "?";
        lineage.Add($"Parcel {parcel}");
        return new StandHistory(stand.StandNumber, stand.Status, StandLifecycle.Next(stand.Status),
            [.. stand.StatusChanges.OrderBy(c => c.Id).Select(c => new StandMove(c.From, c.To, c.Date, c.UserId, c.Authority, c.Reason))], lineage);
    }

    private static string Normalise(string? number) => (number ?? string.Empty).Trim().ToUpperInvariant();

    private async Task<Result<HousingStand>> FindAsync(long companyId, string standNumber, CancellationToken ct)
    {
        string number = Normalise(standNumber);
        HousingStand? stand = await _db.HousingStands.FirstOrDefaultAsync(s => s.CompanyId == companyId && s.StandNumber == number, ct).ConfigureAwait(false);
        return stand is null ? Error.NotFound("Hse.StandNotFound", $"Stand {number} not found.") : stand;
    }

    // Nobody holds, is promised or has blocked any of them.
    private async Task<Result> CheckFreeAsync(IReadOnlyList<HousingStand> stands, CancellationToken ct)
    {
        if (stands.FirstOrDefault(s => !StandLifecycle.Unencumbered.Contains(s.Status) || s.CurrentHolderCustomerId is not null) is HousingStand held)
        {
            return Result.Failure(Error.Rule("Hse.StandEncumbered", $"Stand {held.StandNumber} is {held.Status}: only stands nobody holds or has been promised can be consolidated or re-subdivided."));
        }

        List<long> ids = [.. stands.Select(s => s.Id)];
        if (await StandClaims.AnyClaimedAsync(_db, ids, ct).ConfigureAwait(false) || await StandClaims.AnyBlockedAsync(_db, ids, ct).ConfigureAwait(false))
        {
            return Result.Failure(Error.Rule("Hse.StandEncumbered", "A stand is promised, under agreement or blocked."));
        }

        return Result.Success();
    }
}
