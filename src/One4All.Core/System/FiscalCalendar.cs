namespace One4All.Core.System;

/// <summary>How a fiscal year is divided into periods (brief §3.4).</summary>
public enum FiscalCalendar
{
    /// <summary>Calendar months from the start date.</summary>
    Monthly = 1,

    /// <summary>52 weeks in 12 periods of 4, 4 and 5 weeks per quarter.</summary>
    FourFourFive = 2,

    /// <summary>52 weeks in 12 periods of 4, 5 and 4 weeks per quarter.</summary>
    FourFiveFour = 3,

    /// <summary>52 weeks in 12 periods of 5, 4 and 4 weeks per quarter.</summary>
    FiveFourFour = 4,

    /// <summary>52 weeks in 13 periods of 4 weeks.</summary>
    ThirteenFourWeek = 5,
}

/// <summary>Builds the periods of a fiscal year and works out the year after one.</summary>
public static class FiscalCalendars
{
    /// <summary>Whether a calendar counts in weeks (every calendar but the monthly one).</summary>
    /// <param name="calendar">The calendar.</param>
    /// <returns>True for a week-based calendar.</returns>
    public static bool IsWeekly(FiscalCalendar calendar) => calendar != FiscalCalendar.Monthly;

    /// <summary>The weeks in each period of a week-based calendar (empty for the monthly one).</summary>
    /// <param name="calendar">The calendar.</param>
    /// <returns>The weeks per period.</returns>
    public static IReadOnlyList<int> Weeks(FiscalCalendar calendar) => calendar switch
    {
        FiscalCalendar.FourFourFive => Quarters(4, 4, 5),
        FiscalCalendar.FourFiveFour => Quarters(4, 5, 4),
        FiscalCalendar.FiveFourFour => Quarters(5, 4, 4),
        FiscalCalendar.ThirteenFourWeek => [4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4],
        _ => [],
    };

    /// <summary>The periods (start and end) of a year on a calendar.</summary>
    /// <param name="calendar">The calendar.</param>
    /// <param name="start">The first day.</param>
    /// <param name="months">Monthly periods (monthly calendar only).</param>
    /// <param name="week53">For a week-based calendar, a 53rd week added to the last period (brief §3.4: 52/53-week years).</param>
    /// <returns>The periods in order.</returns>
    public static IReadOnlyList<(DateOnly Start, DateOnly End)> Periods(FiscalCalendar calendar, DateOnly start, int months = 12, bool week53 = false)
    {
        List<(DateOnly, DateOnly)> periods = [];
        if (IsWeekly(calendar))
        {
            IReadOnlyList<int> pattern = Weeks(calendar);
            DateOnly from = start;
            for (int n = 0; n < pattern.Count; n++)
            {
                int weeks = pattern[n] + (week53 && n == pattern.Count - 1 ? 1 : 0);
                periods.Add((from, from.AddDays((weeks * 7) - 1)));
                from = from.AddDays(weeks * 7);
            }

            return periods;
        }

        for (int n = 0; n < months; n++)
        {
            DateOnly from = start.AddMonths(n);
            periods.Add((from, start.AddMonths(n + 1).AddDays(-1)));
        }

        return periods;
    }

    /// <summary>The calendar a year's ordinary periods follow: a week pattern when the periods match one (the last period may carry a 53rd week), else monthly.</summary>
    /// <param name="periods">The year's ordinary periods in order.</param>
    /// <returns>The calendar.</returns>
    public static FiscalCalendar Detect(IReadOnlyList<(DateOnly Start, DateOnly End)> periods)
    {
        foreach (FiscalCalendar calendar in new[] { FiscalCalendar.FourFourFive, FiscalCalendar.FourFiveFour, FiscalCalendar.FiveFourFour, FiscalCalendar.ThirteenFourWeek })
        {
            IReadOnlyList<int> pattern = Weeks(calendar);
            if (periods.Count == pattern.Count && periods.Select((p, n) => (Days: p.End.DayNumber - p.Start.DayNumber + 1, n))
                .All(x => x.Days == pattern[x.n] * 7 || (x.n == pattern.Count - 1 && x.Days == (pattern[x.n] + 1) * 7)))
            {
                return calendar;
            }
        }

        return FiscalCalendar.Monthly;
    }

    /// <summary>
    /// Whether a week-based year starting on a day needs a 53rd week to stay with its anchor (brief §3.4): the year ends on the day nearest the anchor
    /// month and day, so when 52 weeks would end more than three days before it, a week is added.
    /// </summary>
    /// <param name="start">The year's first day.</param>
    /// <param name="anchorMonth">The month of the year-end the calendar keeps to.</param>
    /// <param name="anchorDay">Its day.</param>
    /// <returns>True when the year has 53 weeks.</returns>
    public static bool NeedsWeek53(DateOnly start, int anchorMonth, int anchorDay)
    {
        DateOnly end = start.AddDays(363);
        DateOnly Anchor(int year) => new(year, anchorMonth, Math.Min(anchorDay, DateTime.DaysInMonth(year, anchorMonth)));
        DateOnly nearest = new[] { Anchor(end.Year - 1), Anchor(end.Year), Anchor(end.Year + 1) }.MinBy(a => Math.Abs(a.DayNumber - end.DayNumber));
        return nearest.DayNumber - end.DayNumber > 3;
    }

    private static int[] Quarters(int first, int second, int third) => [first, second, third, first, second, third, first, second, third, first, second, third];

    /// <summary>
    /// The next year's code: every run of digits moves on by one ("2026" → "2027", "2026/27" → "2027/28", "FY26" → "FY27"); a code
    /// with no digits gets the next start year appended.
    /// </summary>
    /// <param name="code">This year's code.</param>
    /// <param name="nextStart">The next year's first day.</param>
    /// <returns>The next code.</returns>
    public static string NextCode(string code, DateOnly nextStart)
    {
        if (!code.Any(char.IsDigit))
        {
            return $"{code}{nextStart.Year}";
        }

        global::System.Text.StringBuilder next = new();
        int i = 0;
        while (i < code.Length)
        {
            if (!char.IsDigit(code[i]))
            {
                next.Append(code[i++]);
                continue;
            }

            int j = i;
            while (j < code.Length && char.IsDigit(code[j]))
            {
                j++;
            }

            string digits = code[i..j];
            long value = long.Parse(digits, global::System.Globalization.CultureInfo.InvariantCulture) + 1;
            string moved = value.ToString(global::System.Globalization.CultureInfo.InvariantCulture);
            next.Append(moved.Length > digits.Length ? moved[^digits.Length..] : moved.PadLeft(digits.Length, '0'));
            i = j;
        }

        return next.ToString();
    }
}
