namespace One4All.Application.Municipal;

/// <summary>What a licence renewal costs and how far it runs.</summary>
/// <param name="Cycles">The renewal cycles charged: one, plus every cycle missed while the licence had lapsed.</param>
/// <param name="NewExpiry">The last day the renewed licence is valid.</param>
/// <param name="Fee">The renewal fee for those cycles.</param>
/// <param name="Penalty">The late-renewal penalty (nothing when renewed by the expiry date).</param>
/// <param name="Late">Whether the renewal came after the licence expired.</param>
public sealed record RenewalTerms(int Cycles, DateOnly NewExpiry, decimal Fee, decimal Penalty, bool Late)
{
    /// <summary>The fee and penalty together.</summary>
    public decimal Total => Fee + Penalty;
}

/// <summary>
/// Licence renewal arithmetic (brief §4.9 licensing and permits). A licence runs in fixed cycles from its first expiry,
/// so a renewal always extends from the old expiry, never from the renewal date: a late renewal pays for every cycle
/// it missed (the holder traded or built under it) plus a penalty on the fee. Pure and unit-tested.
/// </summary>
public static class LicenceRenewal
{
    /// <summary>Works out a renewal.</summary>
    /// <param name="currentExpiry">The last day the licence is valid now.</param>
    /// <param name="renewalDate">The renewal date.</param>
    /// <param name="cycleMonths">The months in a renewal cycle (at least one).</param>
    /// <param name="feePerCycle">The fee for one cycle.</param>
    /// <param name="penaltyPercent">The late penalty as a percentage of the fee due.</param>
    /// <returns>The renewal terms.</returns>
    public static RenewalTerms Renew(DateOnly currentExpiry, DateOnly renewalDate, int cycleMonths, decimal feePerCycle, decimal penaltyPercent)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(cycleMonths, 1);
        int cycles = 1;
        while (Extend(currentExpiry, cycleMonths * cycles) < renewalDate)
        {
            cycles++;
        }

        bool late = renewalDate > currentExpiry;
        decimal fee = feePerCycle * cycles;
        decimal penalty = late ? decimal.Round(fee * penaltyPercent / 100m, 2, MidpointRounding.AwayFromZero) : 0m;
        return new RenewalTerms(cycles, Extend(currentExpiry, cycleMonths * cycles), fee, penalty, late);
    }

    /// <summary>The expiry after the given months, keeping a month-end expiry on the month end.</summary>
    /// <param name="expiry">The current expiry.</param>
    /// <param name="months">The months to add.</param>
    /// <returns>The new expiry.</returns>
    public static DateOnly Extend(DateOnly expiry, int months)
    {
        DateOnly moved = expiry.AddMonths(months);
        bool monthEnd = expiry.Day == DateTime.DaysInMonth(expiry.Year, expiry.Month);
        return monthEnd ? new DateOnly(moved.Year, moved.Month, DateTime.DaysInMonth(moved.Year, moved.Month)) : moved;
    }
}
