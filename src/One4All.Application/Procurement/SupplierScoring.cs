namespace One4All.Application.Procurement;

/// <summary>Supplier performance arithmetic for the scorecard (brief §4.3). Pure and unit-tested.</summary>
public static class SupplierScoring
{
    /// <summary>A rate as a percentage to one place, or null when there is nothing to measure.</summary>
    /// <param name="part">The count or quantity that met the standard.</param>
    /// <param name="whole">The count or quantity measured.</param>
    /// <returns>The percentage, or null when nothing was measured.</returns>
    public static decimal? Rate(decimal part, decimal whole)
        => whole <= 0m ? null : decimal.Round(part / whole * 100m, 1, MidpointRounding.AwayFromZero);

    /// <summary>Whether a delivery was on time: received no later than required, or there was no date to miss.</summary>
    /// <param name="received">When it was received.</param>
    /// <param name="requiredBy">When it was required, if a date was set.</param>
    /// <returns>True when on time; null when no date was set, so it cannot be judged.</returns>
    public static bool? OnTime(DateOnly received, DateOnly? requiredBy) => requiredBy is { } due ? received <= due : null;
}
