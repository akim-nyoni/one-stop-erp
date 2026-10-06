namespace One4All.Application.Pos;

/// <summary>Points earned on a purchase, the tier a member is in, and which earned points have expired — first in, first out.</summary>
public static class LoyaltyRules
{
    /// <summary>Whole points earned on a spend.</summary>
    /// <param name="spend">The amount spent.</param>
    /// <param name="pointsPerUnit">Points per currency unit.</param>
    /// <param name="multiplier">The tier's multiplier.</param>
    /// <returns>The points (never negative).</returns>
    public static decimal Points(decimal spend, decimal pointsPerUnit, decimal multiplier) => Math.Max(0m, decimal.Floor(spend * pointsPerUnit * multiplier));

    /// <summary>The highest tier whose threshold the points earned in the last twelve months reach.</summary>
    /// <param name="earnedLastYear">Points earned in the last twelve months.</param>
    /// <param name="tiers">The tiers: name, threshold, multiplier.</param>
    /// <returns>The tier's name and multiplier (none and 1 when no tier applies).</returns>
    public static (string? Tier, decimal Multiplier) Tier(decimal earnedLastYear, IEnumerable<(string Name, decimal MinPoints, decimal Multiplier)> tiers)
        => tiers.Where(t => earnedLastYear >= t.MinPoints).OrderByDescending(t => t.MinPoints).Select(t => ((string?)t.Name, t.Multiplier)).FirstOrDefault((null, 1m));

    /// <summary>
    /// Points that have expired unused by a date: points used (redeemed or expired already) are taken from the oldest earned first; what
    /// is left of earnings whose expiry has passed is due to expire.
    /// </summary>
    /// <param name="earned">Each earning: points and when they expire (null: never).</param>
    /// <param name="used">Points already redeemed or expired.</param>
    /// <param name="asAt">The date.</param>
    /// <returns>The points to expire now.</returns>
    public static decimal Expiring(IEnumerable<(decimal Points, DateOnly? ExpiresOn)> earned, decimal used, DateOnly asAt)
    {
        decimal toConsume = used;
        decimal expiring = 0m;
        foreach ((decimal points, DateOnly? expiresOn) in earned.OrderBy(e => e.ExpiresOn ?? DateOnly.MaxValue))
        {
            decimal consumed = Math.Min(points, toConsume);
            toConsume -= consumed;
            if (expiresOn is DateOnly due && due <= asAt)
            {
                expiring += points - consumed;
            }
        }

        return expiring;
    }
}
