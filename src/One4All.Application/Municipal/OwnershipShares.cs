namespace One4All.Application.Municipal;

/// <summary>An owner and the share they hold.</summary>
/// <param name="CustomerId">The owner's customer account.</param>
/// <param name="SharePercent">The share.</param>
/// <param name="IsRatepayer">Whether this owner is billed for the property.</param>
public sealed record OwnerShare(long CustomerId, decimal SharePercent, bool IsRatepayer);

/// <summary>Checks a set of joint owners (brief §4.9 owner register). Pure and unit-tested.</summary>
public static class OwnershipShares
{
    /// <summary>Checks the owners: at least one, each listed once with a positive share, shares totalling 100%, one ratepayer.</summary>
    /// <param name="owners">The owners.</param>
    /// <returns>What is wrong, or null when they are in order.</returns>
    public static string? Problem(IReadOnlyList<OwnerShare> owners)
    {
        ArgumentNullException.ThrowIfNull(owners);
        if (owners.Count == 0)
        {
            return "A property needs at least one owner.";
        }

        if (owners.Select(o => o.CustomerId).Distinct().Count() != owners.Count)
        {
            return "An owner is listed more than once.";
        }

        if (owners.Any(o => o.SharePercent <= 0m))
        {
            return "Every owner needs a positive share.";
        }

        decimal total = owners.Sum(o => o.SharePercent);
        if (total != 100m)
        {
            return $"The shares add up to {total:0.####}%, not 100%.";
        }

        int ratepayers = owners.Count(o => o.IsRatepayer);
        return ratepayers == 1 ? null : $"Exactly one owner must be the ratepayer; {ratepayers} are.";
    }
}
