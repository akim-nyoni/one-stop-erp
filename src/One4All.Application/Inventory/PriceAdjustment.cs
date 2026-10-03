namespace One4All.Application.Inventory;

/// <summary>How an adjusted price is rounded to its price point.</summary>
public enum PriceRounding
{
    /// <summary>To the nearest increment (halves away from zero).</summary>
    Nearest = 0,

    /// <summary>Up to the next increment.</summary>
    Up = 1,

    /// <summary>Down to the previous increment.</summary>
    Down = 2,
}

/// <summary>
/// Price arithmetic for bulk price-list maintenance: apply a percentage change, then round to a price-point
/// increment (e.g. 0.05, 0.50, 1.00) in a chosen direction. Pure and unit-tested.
/// </summary>
public static class PriceAdjustment
{
    /// <summary>Applies a percentage change and rounds.</summary>
    /// <param name="price">The current price (≥ 0).</param>
    /// <param name="percent">The change in percent (e.g. 5 for +5%, −10 for −10%); must be above −100.</param>
    /// <param name="roundTo">The price-point increment; 0 or less rounds to cents.</param>
    /// <param name="rounding">The rounding direction.</param>
    /// <returns>The new price, never negative.</returns>
    public static decimal Apply(decimal price, decimal percent, decimal roundTo, PriceRounding rounding)
    {
        if (percent <= -100m)
        {
            throw new ArgumentOutOfRangeException(nameof(percent), "A price cannot be reduced by 100% or more.");
        }

        decimal raw = price * (100m + percent) / 100m;
        decimal increment = roundTo > 0m ? roundTo : 0.01m;
        decimal steps = raw / increment;
        decimal roundedSteps = rounding switch
        {
            PriceRounding.Up => decimal.Ceiling(steps),
            PriceRounding.Down => decimal.Floor(steps),
            _ => decimal.Round(steps, 0, MidpointRounding.AwayFromZero),
        };

        return Math.Max(0m, decimal.Round(roundedSteps * increment, 4, MidpointRounding.ToEven));
    }
}
