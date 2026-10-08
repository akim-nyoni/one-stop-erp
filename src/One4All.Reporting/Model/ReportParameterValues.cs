using System.Globalization;

namespace One4All.Reporting.Model;

/// <summary>
/// Turns what a user typed for a report parameter into the value the report's query receives (brief §13.1): dates, whole numbers, numbers
/// and true/false as such; a multi-select parameter's comma- or semicolon-separated entries as an array, so a query can test
/// <c>= ANY(@Branches)</c>.
/// </summary>
public static class ReportParameterValues
{
    /// <summary>The typed value of one entry.</summary>
    /// <param name="parameter">The parameter.</param>
    /// <param name="raw">What was entered.</param>
    /// <returns>The value (the trimmed text when it is not of the parameter's type; null when blank).</returns>
    public static object? Parse(ReportParameter parameter, string? raw)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (!parameter.MultiSelect)
        {
            return One(parameter.Type, raw.Trim());
        }

        string[] entries = raw.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        object?[] values = [.. entries.Select(e => One(parameter.Type, e))];
        return parameter.Type switch
        {
            ParameterType.Integer when values.All(v => v is long) => values.Cast<long>().ToArray(),
            ParameterType.Decimal when values.All(v => v is decimal) => values.Cast<decimal>().ToArray(),
            ParameterType.Date when values.All(v => v is DateOnly) => values.Cast<DateOnly>().Select(d => d.ToDateTime(TimeOnly.MinValue)).ToArray(),
            ParameterType.Boolean when values.All(v => v is bool) => values.Cast<bool>().ToArray(),
            _ => entries,
        };
    }

    /// <summary>A report's parameters with multi-select entries given as text turned into arrays; other values are left as they are.</summary>
    /// <param name="definition">The report.</param>
    /// <param name="values">The values supplied.</param>
    /// <returns>The values for the query.</returns>
    public static IReadOnlyDictionary<string, object?> ForQuery(ReportDefinition definition, IReadOnlyDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(values);
        if (!definition.Parameters.Any(p => p.MultiSelect))
        {
            return values;
        }

        Dictionary<string, object?> result = new(values, StringComparer.OrdinalIgnoreCase);
        foreach (ReportParameter parameter in definition.Parameters.Where(p => p.MultiSelect))
        {
            if (result.TryGetValue(parameter.Name, out object? value) && value is string text)
            {
                result[parameter.Name] = Parse(parameter, text);
            }
        }

        return result;
    }

    /// <summary>The parameters a lookup depends on that have no value yet.</summary>
    /// <param name="parameter">The parameter.</param>
    /// <param name="values">The values so far.</param>
    /// <returns>The names still needed (empty when its lookup can run).</returns>
    public static IReadOnlyList<string> WaitingOn(ReportParameter parameter, IReadOnlyDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        ArgumentNullException.ThrowIfNull(values);
        return [.. parameter.DependsOn.Where(name => !values.TryGetValue(name, out object? value) || value is null || (value is string s && s.Length == 0))];
    }

    private static object? One(ParameterType type, string raw) => type switch
    {
        ParameterType.Date when DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly d) => d,
        ParameterType.Integer when long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long i) => i,
        ParameterType.Decimal when decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal m) => m,
        ParameterType.Boolean when bool.TryParse(raw, out bool b) => b,
        _ => raw,
    };
}
