using System.Globalization;

namespace One4All.Application.Reporting;

/// <summary>
/// Relative date tokens for scheduled reports (brief §13.3 scheduling): a subscription stores <c>@PrevMonthEnd</c> rather
/// than a fixed date, and each run resolves it against the run date - <c>@Today</c>, <c>@Yesterday</c>,
/// <c>@MonthStart</c>, <c>@MonthEnd</c>, <c>@PrevMonthStart</c>, <c>@PrevMonthEnd</c>, <c>@YearStart</c>,
/// <c>@PrevYearEnd</c>. ISO dates become dates, whole numbers become numbers, anything else stays text. Pure and
/// unit-tested.
/// </summary>
public static class ReportParameterTokens
{
    /// <summary>Resolves stored parameter values for a run.</summary>
    /// <param name="stored">The stored values.</param>
    /// <param name="runDate">The run date.</param>
    /// <returns>The values for the report runner.</returns>
    public static IReadOnlyDictionary<string, object?> Resolve(IReadOnlyDictionary<string, string> stored, DateOnly runDate)
    {
        ArgumentNullException.ThrowIfNull(stored);
        Dictionary<string, object?> resolved = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, string value) in stored)
        {
            resolved[name] = Value(value, runDate);
        }

        return resolved;
    }

    /// <summary>Whether a value is a known token (or not a token at all).</summary>
    /// <param name="value">The stored value.</param>
    /// <returns>False for an unknown <c>@</c> token.</returns>
    public static bool IsValid(string value) => !value.StartsWith('@') || Date(value, new DateOnly(2000, 1, 1)) is not null;

    private static object? Value(string value, DateOnly runDate)
    {
        string text = value.Trim();
        if (text.StartsWith('@'))
        {
            return Date(text, runDate) ?? throw new ArgumentException($"Unknown date token {text}.", nameof(value));
        }

        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date))
        {
            return date;
        }

        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long number) ? number : text;
    }

    private static DateOnly? Date(string token, DateOnly run)
    {
        DateOnly monthStart = new(run.Year, run.Month, 1);
        return token.ToUpperInvariant() switch
        {
            "@TODAY" => run,
            "@YESTERDAY" => run.AddDays(-1),
            "@MONTHSTART" => monthStart,
            "@MONTHEND" => monthStart.AddMonths(1).AddDays(-1),
            "@PREVMONTHSTART" => monthStart.AddMonths(-1),
            "@PREVMONTHEND" => monthStart.AddDays(-1),
            "@YEARSTART" => new DateOnly(run.Year, 1, 1),
            "@PREVYEAREND" => new DateOnly(run.Year - 1, 12, 31),
            _ => null,
        };
    }
}
