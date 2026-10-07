namespace One4All.Core.System;

/// <summary>How a fiscal year is divided into periods (brief §3.4).</summary>
public enum FiscalCalendar
{
    /// <summary>Calendar months from the start date.</summary>
    Monthly = 1,

    /// <summary>52 weeks in 12 periods of 4, 4 and 5 weeks per quarter.</summary>
    FourFourFive = 2,
}

/// <summary>Builds the periods of a fiscal year and works out the year after one.</summary>
public static class FiscalCalendars
{
    /// <summary>The periods (start and end) of a year on a calendar.</summary>
    /// <param name="calendar">The calendar.</param>
    /// <param name="start">The first day.</param>
    /// <param name="months">Monthly periods (monthly calendar only).</param>
    /// <returns>The periods in order.</returns>
    public static IReadOnlyList<(DateOnly Start, DateOnly End)> Periods(FiscalCalendar calendar, DateOnly start, int months = 12)
    {
        List<(DateOnly, DateOnly)> periods = [];
        if (calendar == FiscalCalendar.FourFourFive)
        {
            DateOnly from = start;
            for (int n = 0; n < 12; n++)
            {
                int weeks = n % 3 == 2 ? 5 : 4;
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

    /// <summary>The calendar a year's ordinary periods follow (monthly when each runs month-day to the day before the next month).</summary>
    /// <param name="periods">The year's ordinary periods in order.</param>
    /// <returns>The calendar.</returns>
    public static FiscalCalendar Detect(IReadOnlyList<(DateOnly Start, DateOnly End)> periods)
        => periods.Count == 12 && periods.All(p => p.End.DayNumber - p.Start.DayNumber + 1 is 28 or 35) ? FiscalCalendar.FourFourFive : FiscalCalendar.Monthly;

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
