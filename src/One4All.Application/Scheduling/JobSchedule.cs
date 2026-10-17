using One4All.Core.System;

namespace One4All.Application.Scheduling;

/// <summary>
/// When a scheduled job next runs (brief §4.17 scheduled jobs), in UTC: hourly at a minute past the hour, daily at a time,
/// weekly on a day at a time, or monthly on a day at a time - a day beyond the month's end runs on its last day. Pure and
/// unit-tested.
/// </summary>
public static class JobSchedule
{
    /// <summary>The first run strictly after a moment.</summary>
    /// <param name="frequency">How often.</param>
    /// <param name="timeOfDay">The time of day (for hourly, only its minute counts).</param>
    /// <param name="dayOfWeek">The day for weekly jobs.</param>
    /// <param name="dayOfMonth">The day for monthly jobs (1–31).</param>
    /// <param name="after">The moment.</param>
    /// <returns>The next run.</returns>
    public static DateTimeOffset NextRun(JobFrequency frequency, TimeOnly timeOfDay, DayOfWeek? dayOfWeek, int? dayOfMonth, DateTimeOffset after)
    {
        DateTime now = after.UtcDateTime;
        DateTime candidate;
        switch (frequency)
        {
            case JobFrequency.Hourly:
                candidate = new DateTime(now.Year, now.Month, now.Day, now.Hour, timeOfDay.Minute, 0, DateTimeKind.Utc);
                if (candidate <= now)
                {
                    candidate = candidate.AddHours(1);
                }

                break;
            case JobFrequency.Daily:
                candidate = now.Date.Add(timeOfDay.ToTimeSpan());
                if (candidate <= now)
                {
                    candidate = candidate.AddDays(1);
                }

                break;
            case JobFrequency.Weekly:
                DayOfWeek day = dayOfWeek ?? throw new ArgumentException("A weekly job needs its day.", nameof(dayOfWeek));
                candidate = now.Date.AddDays(((int)day - (int)now.DayOfWeek + 7) % 7).Add(timeOfDay.ToTimeSpan());
                if (candidate <= now)
                {
                    candidate = candidate.AddDays(7);
                }

                break;
            case JobFrequency.Monthly:
                int wanted = dayOfMonth is >= 1 and <= 31 ? dayOfMonth.Value : throw new ArgumentException("A monthly job needs a day from 1 to 31.", nameof(dayOfMonth));
                candidate = OnDay(now.Year, now.Month, wanted, timeOfDay);
                if (candidate <= now)
                {
                    DateTime next = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
                    candidate = OnDay(next.Year, next.Month, wanted, timeOfDay);
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(frequency), frequency, "Unknown frequency.");
        }

        return new DateTimeOffset(candidate, TimeSpan.Zero);
    }

    private static DateTime OnDay(int year, int month, int day, TimeOnly time)
        => new DateTime(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)), 0, 0, 0, DateTimeKind.Utc).Add(time.ToTimeSpan());
}
