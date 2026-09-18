using System.Globalization;

namespace One4All.Infrastructure.Reporting;

/// <summary>Helpers for reading typed report parameters from the loosely-typed parameter dictionary.</summary>
internal static class ReportParameters
{
    /// <summary>Reads a <see cref="DateOnly"/> parameter, defaulting to today when absent or unparseable.</summary>
    /// <param name="parameters">The parameter dictionary.</param>
    /// <param name="name">The parameter name.</param>
    /// <returns>The date.</returns>
    public static DateOnly Date(IReadOnlyDictionary<string, object?> parameters, string name) =>
        TryDate(parameters, name) ?? DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>Reads an optional <see cref="DateOnly"/> parameter.</summary>
    /// <param name="parameters">The parameter dictionary.</param>
    /// <param name="name">The parameter name.</param>
    /// <returns>The date, or null.</returns>
    public static DateOnly? TryDate(IReadOnlyDictionary<string, object?> parameters, string name) =>
        parameters.TryGetValue(name, out object? value) ? Coerce(value) : null;

    /// <summary>Reads a <see cref="long"/> parameter (0 when absent).</summary>
    /// <param name="parameters">The parameter dictionary.</param>
    /// <param name="name">The parameter name.</param>
    /// <returns>The value.</returns>
    public static long Long(IReadOnlyDictionary<string, object?> parameters, string name) =>
        parameters.TryGetValue(name, out object? value) && value is not null && long.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long result)
            ? result
            : 0L;

    /// <summary>Reads a string parameter (empty when absent).</summary>
    /// <param name="parameters">The parameter dictionary.</param>
    /// <param name="name">The parameter name.</param>
    /// <returns>The value.</returns>
    public static string String(IReadOnlyDictionary<string, object?> parameters, string name) =>
        parameters.TryGetValue(name, out object? value) ? value?.ToString() ?? string.Empty : string.Empty;

    private static DateOnly? Coerce(object? value) => value switch
    {
        DateOnly d => d,
        DateTime dt => DateOnly.FromDateTime(dt),
        string s when DateOnly.TryParse(s, CultureInfo.InvariantCulture, out DateOnly parsed) => parsed,
        string s when DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dt) => DateOnly.FromDateTime(dt),
        _ => null,
    };
}
