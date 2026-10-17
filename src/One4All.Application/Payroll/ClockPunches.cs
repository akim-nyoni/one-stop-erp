using System.Globalization;

namespace One4All.Application.Payroll;

/// <summary>An employee's day from the time clock: the first punch in and the last punch out.</summary>
/// <param name="EmployeeNumber">The badge (the employee number).</param>
/// <param name="Date">The day.</param>
/// <param name="ClockIn">The first punch.</param>
/// <param name="ClockOut">The last punch (null: only one punch that day).</param>
public sealed record ClockDay(string EmployeeNumber, DateOnly Date, TimeOnly ClockIn, TimeOnly? ClockOut)
{
    /// <summary>The hours between the first and last punch.</summary>
    public decimal HoursWorked => ClockOut is TimeOnly o ? decimal.Round((decimal)(o - ClockIn).TotalHours, 2, MidpointRounding.AwayFromZero) : 0m;
}

/// <summary>
/// Reads a time-clock export (brief §4.15 clock import): one punch a line - badge, then the date and time ("E001,2026-05-04 07:58") - and pairs
/// each employee's punches into days, the first punch in and the last out. Lines that cannot be read are reported, not guessed. Pure.
/// </summary>
public static class ClockPunches
{
    private static readonly string[] Formats = ["yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss", "dd/MM/yyyy HH:mm", "dd/MM/yyyy HH:mm:ss"];

    /// <summary>Reads the punches and pairs them into days.</summary>
    /// <param name="text">The export: badge,date time per line (a header line is skipped).</param>
    /// <param name="problems">The lines that could not be read, with their line numbers.</param>
    /// <returns>The days, by employee and date.</returns>
    public static IReadOnlyList<ClockDay> Days(string text, out IReadOnlyList<string> problems)
    {
        List<string> bad = [];
        List<(string Badge, DateTime At)> punches = [];
        string[] lines = (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0)
            {
                continue;
            }

            string[] parts = line.Split([',', ';', '\t'], StringSplitOptions.TrimEntries);
            string when = parts.Length >= 3 ? $"{parts[1]} {parts[2]}" : parts.Length == 2 ? parts[1] : string.Empty;
            if (parts.Length >= 2 && parts[0].Length > 0 && DateTime.TryParseExact(when, Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime at))
            {
                punches.Add((parts[0].ToUpperInvariant(), at));
            }
            else if (i > 0 || !line.Contains("badge", StringComparison.OrdinalIgnoreCase) && !line.Contains("employee", StringComparison.OrdinalIgnoreCase))
            {
                bad.Add($"Line {i + 1}: \"{line}\" is not badge,date time.");
            }
        }

        problems = bad;
        return [.. punches.GroupBy(p => (p.Badge, Date: DateOnly.FromDateTime(p.At)))
            .Select(g =>
            {
                List<DateTime> times = [.. g.Select(p => p.At).Order()];
                return new ClockDay(g.Key.Badge, g.Key.Date, TimeOnly.FromDateTime(times[0]), times.Count > 1 ? TimeOnly.FromDateTime(times[^1]) : null);
            })
            .OrderBy(d => d.EmployeeNumber, StringComparer.Ordinal).ThenBy(d => d.Date)];
    }

    /// <summary>The overtime in a clocked day: the hours worked beyond the rostered shift's length (none without a shift or a clock-out).</summary>
    /// <param name="day">The day.</param>
    /// <param name="shiftStart">The rostered shift's start.</param>
    /// <param name="shiftEnd">Its end.</param>
    /// <returns>The overtime hours.</returns>
    public static decimal Overtime(ClockDay day, TimeOnly? shiftStart, TimeOnly? shiftEnd)
    {
        if (shiftStart is not TimeOnly start || shiftEnd is not TimeOnly end || day.ClockOut is null)
        {
            return 0m;
        }

        decimal shift = (decimal)(end > start ? (end - start).TotalHours : (end - start).TotalHours + 24);
        return Math.Max(0m, decimal.Round(day.HoursWorked - shift, 2, MidpointRounding.AwayFromZero));
    }
}
