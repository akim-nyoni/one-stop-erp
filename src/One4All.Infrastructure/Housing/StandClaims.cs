using Microsoft.EntityFrameworkCore;
using One4All.Core.Housing;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Housing;

/// <summary>
/// The anti-double-allocation check shared by every route a stand can be promised through: a live waiting-list allocation,
/// a cooperative, a regularised informal-settlement household, or a developer agreement (until a council-quota stand is
/// handed over, when it returns to the council's own pool).
/// </summary>
internal static class StandClaims
{
    private static readonly AllocationStatus[] Live = [AllocationStatus.Proposed, AllocationStatus.Offered, AllocationStatus.Accepted];

    /// <summary>Whether any of the stands is already promised through any route.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="standIds">The stands.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True when at least one is claimed.</returns>
    public static async Task<bool> AnyClaimedAsync(One4AllDbContext db, IReadOnlyCollection<long> standIds, CancellationToken ct)
        => await db.Set<StandAllocation>().AnyAsync(a => standIds.Contains(a.StandId) && Live.Contains(a.Status), ct).ConfigureAwait(false)
            || await db.Set<CooperativeStand>().AnyAsync(c => standIds.Contains(c.StandId), ct).ConfigureAwait(false)
            || await db.Set<EnumeratedHousehold>().AnyAsync(h => h.StandId != null && standIds.Contains(h.StandId.Value), ct).ConfigureAwait(false)
            || await db.Set<DeveloperAgreementStand>().AnyAsync(d => standIds.Contains(d.StandId) && !(d.Quota == StandQuota.Council && d.HandedOverOn != null), ct).ConfigureAwait(false);
}
