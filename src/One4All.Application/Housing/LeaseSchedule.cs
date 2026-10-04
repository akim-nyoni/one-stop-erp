namespace One4All.Application.Housing;

/// <summary>One lease year's contractual rent.</summary>
/// <param name="Year">The lease year, from 1.</param>
/// <param name="AnnualRent">The rent for that year.</param>
public readonly record struct LeaseYear(int Year, decimal AnnualRent);

/// <summary>
/// Lease rent arithmetic (brief §4.10.5 long leases with rent reviews; §4.10.9: rental income is straight-lined over the
/// lease term where escalations are fixed, IFRS 16 lessor operating lease): the contractual rent each year with fixed
/// escalation at each review, the monthly contractual rent, and the straight-line monthly income — the last month taking
/// the rounding so the income over the term equals the rent exactly. Pure and unit-tested.
/// </summary>
public static class LeaseSchedule
{
    /// <summary>The contractual rent for each lease year.</summary>
    /// <param name="annualRent">The first year's rent.</param>
    /// <param name="termYears">The lease term in years.</param>
    /// <param name="reviewEveryYears">Years between rent reviews.</param>
    /// <param name="escalationPercent">The fixed escalation at each review.</param>
    /// <returns>The years.</returns>
    public static IReadOnlyList<LeaseYear> Years(decimal annualRent, int termYears, int reviewEveryYears, decimal escalationPercent)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(termYears, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(reviewEveryYears, 1);
        List<LeaseYear> years = [];
        decimal rent = annualRent;
        for (int year = 1; year <= termYears; year++)
        {
            if (year > 1 && (year - 1) % reviewEveryYears == 0)
            {
                rent = decimal.Round(rent * (1m + escalationPercent / 100m), 2, MidpointRounding.AwayFromZero);
            }

            years.Add(new LeaseYear(year, rent));
        }

        return years;
    }

    /// <summary>The contractual rent billed in a lease month.</summary>
    /// <param name="years">The lease years.</param>
    /// <param name="monthIndex">The lease month, from 1.</param>
    /// <returns>The month's rent (the year's rent over twelve, to the cent; the year's last month takes the rounding).</returns>
    public static decimal ContractualMonth(IReadOnlyList<LeaseYear> years, int monthIndex)
    {
        ArgumentNullException.ThrowIfNull(years);
        LeaseYear year = years[(monthIndex - 1) / 12];
        decimal monthly = decimal.Round(year.AnnualRent / 12m, 2, MidpointRounding.AwayFromZero);
        return monthIndex % 12 == 0 ? year.AnnualRent - monthly * 11m : monthly;
    }

    /// <summary>The straight-line income recognised in a lease month.</summary>
    /// <param name="years">The lease years.</param>
    /// <param name="monthIndex">The lease month, from 1.</param>
    /// <returns>The income (the total rent over the months, to the cent; the final month takes the rounding).</returns>
    public static decimal StraightLineMonth(IReadOnlyList<LeaseYear> years, int monthIndex)
    {
        ArgumentNullException.ThrowIfNull(years);
        decimal total = years.Sum(y => y.AnnualRent);
        int months = years.Count * 12;
        decimal monthly = decimal.Round(total / months, 2, MidpointRounding.AwayFromZero);
        return monthIndex == months ? total - monthly * (months - 1) : monthly;
    }
}
