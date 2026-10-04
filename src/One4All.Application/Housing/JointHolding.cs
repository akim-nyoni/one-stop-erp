using One4All.Core.Housing;

namespace One4All.Application.Housing;

/// <summary>A holder's share of a stand.</summary>
/// <param name="CustomerId">The holder.</param>
/// <param name="SharePercent">The share.</param>
public sealed record HolderShare(long CustomerId, decimal SharePercent);

/// <summary>
/// Joint ownership of a stand (brief §4.10.6): the holders' shares must total 100%; one holder's share — or part of it —
/// can be transferred on its own; and on a death, joint tenancy passes the deceased's share to the survivors in
/// proportion to their shares, while tenancy in common leaves it with the deceased's estate. Pure and unit-tested.
/// </summary>
public static class JointHolding
{
    /// <summary>What is wrong with a set of holders, or null when they are in order.</summary>
    /// <param name="holders">The holders.</param>
    /// <returns>The problem.</returns>
    public static string? Problem(IReadOnlyList<HolderShare> holders)
    {
        ArgumentNullException.ThrowIfNull(holders);
        if (holders.Count == 0)
        {
            return "A stand needs at least one holder.";
        }

        if (holders.Select(h => h.CustomerId).Distinct().Count() != holders.Count)
        {
            return "A holder is listed more than once.";
        }

        if (holders.Any(h => h.SharePercent <= 0m))
        {
            return "Every holder needs a positive share.";
        }

        decimal total = holders.Sum(h => h.SharePercent);
        return total == 100m ? null : $"The shares add up to {total:0.####}%, not 100%.";
    }

    /// <summary>The holders after part or all of one holder's share passes to another (who may already hold a share).</summary>
    /// <param name="holders">The holders before.</param>
    /// <param name="fromCustomerId">The holder parting with a share.</param>
    /// <param name="toCustomerId">The recipient.</param>
    /// <param name="portion">The share transferred (percentage points of the whole).</param>
    /// <returns>The holders after, or null when the transferor does not hold that much.</returns>
    public static IReadOnlyList<HolderShare>? TransferShare(IReadOnlyList<HolderShare> holders, long fromCustomerId, long toCustomerId, decimal portion)
    {
        ArgumentNullException.ThrowIfNull(holders);
        HolderShare? from = holders.FirstOrDefault(h => h.CustomerId == fromCustomerId);
        if (from is null || portion <= 0m || portion > from.SharePercent || fromCustomerId == toCustomerId)
        {
            return null;
        }

        Dictionary<long, decimal> shares = holders.ToDictionary(h => h.CustomerId, h => h.SharePercent);
        shares[fromCustomerId] -= portion;
        shares[toCustomerId] = shares.GetValueOrDefault(toCustomerId) + portion;
        return Ordered(shares, holders);
    }

    /// <summary>
    /// The holders after a death. Joint tenancy: the deceased's share is shared among the survivors in proportion to their
    /// shares (any rounding to the largest survivor). Tenancy in common: unchanged — the share belongs to the estate.
    /// </summary>
    /// <param name="holders">The holders before.</param>
    /// <param name="deceasedCustomerId">The holder who died.</param>
    /// <param name="regime">How the stand is held.</param>
    /// <returns>The holders after, or null when the deceased is not a holder.</returns>
    public static IReadOnlyList<HolderShare>? OnDeath(IReadOnlyList<HolderShare> holders, long deceasedCustomerId, CoOwnershipRegime regime)
    {
        ArgumentNullException.ThrowIfNull(holders);
        HolderShare? deceased = holders.FirstOrDefault(h => h.CustomerId == deceasedCustomerId);
        if (deceased is null)
        {
            return null;
        }

        List<HolderShare> survivors = [.. holders.Where(h => h.CustomerId != deceasedCustomerId)];
        if (regime == CoOwnershipRegime.InCommon || survivors.Count == 0)
        {
            return holders;
        }

        decimal survivingTotal = survivors.Sum(s => s.SharePercent);
        Dictionary<long, decimal> shares = survivors.ToDictionary(s => s.CustomerId,
            s => s.SharePercent + Math.Round(deceased.SharePercent * s.SharePercent / survivingTotal, 4, MidpointRounding.AwayFromZero));
        long largest = survivors.OrderByDescending(s => s.SharePercent).ThenBy(s => s.CustomerId).First().CustomerId;
        shares[largest] += 100m - shares.Values.Sum();
        return Ordered(shares, holders);
    }

    // Keeps the original holders' order, newcomers last; drops holders left with nothing.
    private static List<HolderShare> Ordered(Dictionary<long, decimal> shares, IReadOnlyList<HolderShare> before)
    {
        List<long> order = [.. before.Select(h => h.CustomerId), .. shares.Keys.Where(k => before.All(h => h.CustomerId != k))];
        return [.. order.Where(id => shares.TryGetValue(id, out decimal share) && share > 0m).Select(id => new HolderShare(id, shares[id]))];
    }
}
