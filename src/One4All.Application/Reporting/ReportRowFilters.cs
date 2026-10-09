namespace One4All.Application.Reporting;

/// <summary>
/// Filters any report's rows by its columns (brief §9 common parameters): <c>Filter.Branch=HQ</c> keeps the rows whose Branch is HQ, and so on
/// for cost centre, currency, department or any column. The filters travel under reserved names a report never reads itself, so they never
/// clash with a report's own parameters; a filter on a column the report does not have leaves its rows alone. Pure.
/// </summary>
public static class ReportRowFilters
{
    /// <summary>The prefix the filters' parameter names carry.</summary>
    public const string Prefix = "Filter.";

    /// <summary>Reads "Branch=HQ; Currency=USD" into filter parameters.</summary>
    /// <param name="text">The filters as typed.</param>
    /// <returns>The parameters (<c>Filter.Branch</c> = HQ…).</returns>
    public static IReadOnlyDictionary<string, object?> Parse(string? text)
    {
        Dictionary<string, object?> filters = new(StringComparer.OrdinalIgnoreCase);
        foreach (string part in (text ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int equals = part.IndexOf('=', StringComparison.Ordinal);
            if (equals > 0 && part[(equals + 1)..].Trim().Length > 0)
            {
                filters[Prefix + part[..equals].Trim()] = part[(equals + 1)..].Trim();
            }
        }

        return filters;
    }

    /// <summary>Keeps the rows every filter on one of their columns allows.</summary>
    /// <param name="rows">The report's rows.</param>
    /// <param name="parameters">The run's parameters (filters among them).</param>
    /// <returns>The rows kept.</returns>
    public static IReadOnlyList<IReadOnlyDictionary<string, object?>> Apply(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, IReadOnlyDictionary<string, object?> parameters)
    {
        List<(string Column, string Value)> filters = [.. parameters
            .Where(p => p.Key.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) && p.Key.Length > Prefix.Length && p.Value is not null && Convert.ToString(p.Value, System.Globalization.CultureInfo.InvariantCulture)!.Trim().Length > 0)
            .Select(p => (p.Key[Prefix.Length..], Convert.ToString(p.Value, System.Globalization.CultureInfo.InvariantCulture)!.Trim()))];
        if (filters.Count == 0 || rows.Count == 0)
        {
            return rows;
        }

        // Only the filters on columns the report has (looked up whatever their case).
        List<(string Column, string Value)> applicable = [];
        foreach ((string column, string value) in filters)
        {
            string? key = rows[0].Keys.FirstOrDefault(k => string.Equals(k, column, StringComparison.OrdinalIgnoreCase));
            if (key is not null)
            {
                applicable.Add((key, value));
            }
        }

        return applicable.Count == 0 ? rows : [.. rows.Where(r => applicable.All(f => string.Equals(
            Convert.ToString(r.GetValueOrDefault(f.Column), System.Globalization.CultureInfo.InvariantCulture)?.Trim(), f.Value, StringComparison.OrdinalIgnoreCase)))];
    }
}
