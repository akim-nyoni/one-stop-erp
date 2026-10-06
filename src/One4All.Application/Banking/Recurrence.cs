using One4All.Core.Banking;

namespace One4All.Application.Banking;

/// <summary>Due dates of a recurring schedule (standing orders, direct debits).</summary>
public static class Recurrence
{
    /// <summary>
    /// The occurrences after the last one run, up to a date, never past the end. Monthly, quarterly and annual dates keep the start
    /// date's day, falling back to the month's last day when it is shorter (31 Jan → 28 Feb → 31 Mar).
    /// </summary>
    /// <param name="start">The first occurrence.</param>
    /// <param name="end">The last date allowed, if any.</param>
    /// <param name="frequency">How often.</param>
    /// <param name="lastRun">The last occurrence already run, if any.</param>
    /// <param name="upTo">Occurrences on or before this date.</param>
    /// <returns>The due dates in order.</returns>
    public static IReadOnlyList<DateOnly> Due(DateOnly start, DateOnly? end, RecurrenceFrequency frequency, DateOnly? lastRun, DateOnly upTo)
    {
        List<DateOnly> due = [];
        DateOnly limit = end is DateOnly e && e < upTo ? e : upTo;
        for (int n = 0; ; n++)
        {
            DateOnly date = Occurrence(start, frequency, n);
            if (date > limit)
            {
                break;
            }

            if (lastRun is not DateOnly last || date > last)
            {
                due.Add(date);
            }
        }

        return due;
    }

    /// <summary>The nth occurrence (0 = the start).</summary>
    /// <param name="start">The start.</param>
    /// <param name="frequency">How often.</param>
    /// <param name="n">Which occurrence.</param>
    /// <returns>Its date.</returns>
    public static DateOnly Occurrence(DateOnly start, RecurrenceFrequency frequency, int n)
    {
        if (frequency == RecurrenceFrequency.Weekly)
        {
            return start.AddDays(7 * n);
        }

        int months = n * frequency switch { RecurrenceFrequency.Quarterly => 3, RecurrenceFrequency.Annually => 12, _ => 1 };
        DateOnly first = new DateOnly(start.Year, start.Month, 1).AddMonths(months);
        return new DateOnly(first.Year, first.Month, Math.Min(start.Day, DateTime.DaysInMonth(first.Year, first.Month)));
    }
}
