using Microsoft.EntityFrameworkCore;
using One4All.Core.Housing;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Housing;

/// <summary>
/// The anti-double-allocation check shared by every route a stand can be promised through: a live waiting-list allocation,
/// a cooperative, a regularised informal-settlement household, or a developer agreement (until a council-quota stand is
/// handed over, when it returns to the council's own pool) - and the caveat check: a stand under a block in force.
/// </summary>
internal static class StandClaims
{
    private static readonly AllocationStatus[] Live = [AllocationStatus.Proposed, AllocationStatus.Offered, AllocationStatus.Accepted];

    /// <summary>
    /// Whether any of the stands is blocked: by a stand block in force, a caveat in force on its parent parcel, or an open
    /// land dispute over the stand itself.
    /// </summary>
    /// <param name="db">The database context.</param>
    /// <param name="standIds">The stands.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True when at least one is blocked.</returns>
    public static async Task<bool> AnyBlockedAsync(One4AllDbContext db, IReadOnlyCollection<long> standIds, CancellationToken ct)
        => await db.Set<StandBlock>().AnyAsync(b => standIds.Contains(b.StandId) && b.LiftedOn == null, ct).ConfigureAwait(false)
            || await db.Set<LandEncumbrance>().AnyAsync(e => e.Type == EncumbranceType.Caveat && e.LiftedOn == null
                && db.HousingStands.Any(s => standIds.Contains(s.Id) && s.LandParcelId == e.LandParcelId), ct).ConfigureAwait(false)
            || await db.Set<LandDispute>().AnyAsync(d => d.StandId != null && standIds.Contains(d.StandId.Value) && d.Stage != LandDisputeStage.Resolved, ct).ConfigureAwait(false)
            || await db.HousingStands.AnyAsync(s => standIds.Contains(s.Id) && s.Status == StandStatus.Withheld, ct).ConfigureAwait(false);

    /// <summary>
    /// The number of the stand's title application still in progress (applied for but not yet registered), if any: while
    /// one is open the holder it was applied for must not change, so no transfer of the stand or a share of it may proceed.
    /// </summary>
    /// <param name="db">The database context.</param>
    /// <param name="standId">The stand.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The open application's number, or null.</returns>
    public static Task<string?> OpenTitleApplicationAsync(One4AllDbContext db, long standId, CancellationToken ct)
        => db.Set<TitleDeed>().Where(t => t.StandId == standId && t.Stage < TitleStage.Registered).Select(t => t.ApplicationNumber).FirstOrDefaultAsync(ct);

    /// <summary>Every stand of a council that is blocked or already promised through any route (for picking free stands).</summary>
    /// <param name="db">The database context.</param>
    /// <param name="companyId">The council.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The unavailable stand ids.</returns>
    public static async Task<HashSet<long>> UnavailableAsync(One4AllDbContext db, long companyId, CancellationToken ct)
    {
        HashSet<long> ids = [.. await db.Set<StandBlock>().Where(b => b.CompanyId == companyId && b.LiftedOn == null).Select(b => b.StandId).ToListAsync(ct).ConfigureAwait(false)];
        ids.UnionWith(await db.HousingStands.Where(s => s.CompanyId == companyId
            && db.Set<LandEncumbrance>().Any(e => e.LandParcelId == s.LandParcelId && e.Type == EncumbranceType.Caveat && e.LiftedOn == null)).Select(s => s.Id).ToListAsync(ct).ConfigureAwait(false));
        ids.UnionWith(await db.Set<LandDispute>().Where(d => d.CompanyId == companyId && d.StandId != null && d.Stage != LandDisputeStage.Resolved).Select(d => d.StandId!.Value).ToListAsync(ct).ConfigureAwait(false));
        ids.UnionWith(await db.Set<StandAllocation>().Where(a => a.CompanyId == companyId && Live.Contains(a.Status)).Select(a => a.StandId).ToListAsync(ct).ConfigureAwait(false));
        ids.UnionWith(await db.Set<CooperativeStand>().Where(c => c.CompanyId == companyId).Select(c => c.StandId).ToListAsync(ct).ConfigureAwait(false));
        ids.UnionWith(await db.Set<EnumeratedHousehold>().Where(h => h.StandId != null && db.HousingStands.Any(s => s.Id == h.StandId && s.CompanyId == companyId)).Select(h => h.StandId!.Value).ToListAsync(ct).ConfigureAwait(false));
        ids.UnionWith(await db.Set<DeveloperAgreementStand>().Where(d => !(d.Quota == StandQuota.Council && d.HandedOverOn != null) && db.HousingStands.Any(s => s.Id == d.StandId && s.CompanyId == companyId))
            .Select(d => d.StandId).ToListAsync(ct).ConfigureAwait(false));
        return ids;
    }

    /// <summary>Whether any of the stands is blocked, or already promised through any route.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="standIds">The stands.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True when at least one is blocked or claimed.</returns>
    public static async Task<bool> AnyClaimedAsync(One4AllDbContext db, IReadOnlyCollection<long> standIds, CancellationToken ct)
        => await AnyBlockedAsync(db, standIds, ct).ConfigureAwait(false)
            || await db.Set<StandAllocation>().AnyAsync(a => standIds.Contains(a.StandId) && Live.Contains(a.Status), ct).ConfigureAwait(false)
            || await db.Set<CooperativeStand>().AnyAsync(c => standIds.Contains(c.StandId), ct).ConfigureAwait(false)
            || await db.Set<EnumeratedHousehold>().AnyAsync(h => h.StandId != null && standIds.Contains(h.StandId.Value), ct).ConfigureAwait(false)
            || await db.Set<DeveloperAgreementStand>().AnyAsync(d => standIds.Contains(d.StandId) && !(d.Quota == StandQuota.Council && d.HandedOverOn != null), ct).ConfigureAwait(false);
}
