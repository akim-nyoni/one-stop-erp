using One4All.Core.Payroll;

namespace One4All.Application.Payroll;

/// <summary>What a leave policy gives an employee for a month (brief §4.15). Pure and unit-tested.</summary>
public static class LeavePolicyRules
{
    /// <summary>Completed years of service at a date.</summary>
    /// <param name="engaged">The engagement date.</param>
    /// <param name="asAt">The date.</param>
    /// <returns>The completed years.</returns>
    public static int YearsOfService(DateOnly engaged, DateOnly asAt)
    {
        int years = asAt.Year - engaged.Year;
        return asAt < engaged.AddYears(years) ? Math.Max(0, years - 1) : Math.Max(0, years);
    }

    /// <summary>The yearly entitlement at a date: the highest service band reached, else the policy's own days.</summary>
    /// <param name="policy">The policy.</param>
    /// <param name="engaged">The engagement date.</param>
    /// <param name="asAt">The date.</param>
    /// <returns>Days a year.</returns>
    /// <param name="grade">The employee's grade (its days, when the policy lists it, replace the policy's own).</param>
    public static decimal AnnualEntitlement(LeavePolicy policy, DateOnly engaged, DateOnly asAt, string? grade = null)
    {
        int years = YearsOfService(engaged, asAt);
        decimal basis = GradeDays(policy.GradeDays, grade) ?? policy.AnnualDays;
        decimal? band = policy.Bands.Where(b => b.FromYears <= years).OrderByDescending(b => b.FromYears).Select(b => (decimal?)b.AnnualDays).FirstOrDefault();
        return band is decimal service ? Math.Max(service, basis) : basis;
    }

    /// <summary>The days a grade gets under a "GRADE=days; …" list, or null when it is not listed.</summary>
    /// <param name="gradeDays">The list.</param>
    /// <param name="grade">The grade.</param>
    /// <returns>The days, or null.</returns>
    public static decimal? GradeDays(string? gradeDays, string? grade)
    {
        if (string.IsNullOrWhiteSpace(gradeDays) || string.IsNullOrWhiteSpace(grade))
        {
            return null;
        }

        foreach (string part in gradeDays.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] bits = part.Split('=', StringSplitOptions.TrimEntries);
            if (bits.Length == 2 && string.Equals(bits[0], grade.Trim(), StringComparison.OrdinalIgnoreCase)
                && decimal.TryParse(bits[1], System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out decimal days))
            {
                return days;
            }
        }

        return null;
    }

    /// <summary>The days of time off in lieu overtime hours earn.</summary>
    /// <param name="overtimeHours">The overtime worked.</param>
    /// <param name="hoursPerDay">The hours in a working day.</param>
    /// <param name="ratio">The hours off per hour worked (1 = hour for hour, 1.5 = time and a half).</param>
    /// <returns>The days, to four places.</returns>
    public static decimal TimeOffInLieu(decimal overtimeHours, decimal hoursPerDay, decimal ratio = 1m)
        => overtimeHours <= 0m || hoursPerDay <= 0m || ratio <= 0m ? 0m : decimal.Round(overtimeHours * ratio / hoursPerDay, 4, MidpointRounding.AwayFromZero);

    /// <summary>
    /// The days earned for a month: monthly policies a twelfth of the entitlement (a joiner's first month and a leaver's last pro rata by days
    /// employed); yearly policies the whole entitlement in the leave-year start month, or for a joiner the months left in the leave year
    /// in the month they join.
    /// </summary>
    /// <param name="policy">The policy.</param>
    /// <param name="engaged">The engagement date.</param>
    /// <param name="terminated">The termination date, if any.</param>
    /// <param name="month">Any day in the month.</param>
    /// <returns>The days, to four places.</returns>
    /// <param name="grade">The employee's grade.</param>
    public static decimal DaysForMonth(LeavePolicy policy, DateOnly engaged, DateOnly? terminated, DateOnly month, string? grade = null)
    {
        DateOnly first = new(month.Year, month.Month, 1), last = first.AddMonths(1).AddDays(-1);
        if (engaged > last || (terminated is DateOnly t && t < first))
        {
            return 0m;
        }

        decimal annual = AnnualEntitlement(policy, engaged, last, grade);
        if (policy.AccruesMonthly)
        {
            DateOnly from = engaged > first ? engaged : first, to = terminated is DateOnly end && end < last ? end : last;
            decimal share = policy.ProRataJoiners ? (decimal)(to.DayNumber - from.DayNumber + 1) / (last.DayNumber - first.DayNumber + 1) : 1m;
            return decimal.Round(annual / 12m * share, 4, MidpointRounding.AwayFromZero);
        }

        bool yearStarts = month.Month == policy.LeaveYearStartMonth;
        bool joinsThisMonth = engaged >= first && engaged <= last;
        if (joinsThisMonth && !yearStarts)
        {
            int monthsLeft = ((policy.LeaveYearStartMonth - month.Month) % 12 + 12) % 12;
            return policy.ProRataJoiners ? decimal.Round(annual * monthsLeft / 12m, 4, MidpointRounding.AwayFromZero) : annual;
        }

        return yearStarts ? annual : 0m;
    }
}
