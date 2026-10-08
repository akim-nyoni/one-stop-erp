using System.Globalization;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Common;
using One4All.Infrastructure.Persistence;
using One4All.Reporting.Model;

namespace One4All.Infrastructure.Reporting;

/// <summary>
/// Runs a report parameter's list-of-values query through the same guarded, read-only path as report queries (a single SELECT filtered by
/// <c>@CompanyId</c>, no system schemas), binding the values of the parameters it depends on.
/// </summary>
public sealed class ReportParameterLookupService : IReportParameterLookupService
{
    /// <summary>The most choices a list offers.</summary>
    public const int MaxOptions = 1000;

    private readonly One4AllDbContext _db;
    private readonly SqlReportDataProvider _sql;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public ReportParameterLookupService(One4AllDbContext db)
    {
        _db = db;
        _sql = new SqlReportDataProvider(db);
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<ParameterOption>>> OptionsAsync(long companyId, string reportCode, string parameterName, IReadOnlyDictionary<string, string> values,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(values);
        ReportDefinition? definition = await ReportRunner.DefinitionAsync(_db, companyId, reportCode, ct).ConfigureAwait(false);
        if (definition is null)
        {
            return Error.NotFound("Report.NotFound", $"No report definition found for code '{reportCode}'.");
        }

        ReportParameter? parameter = definition.Parameters.FirstOrDefault(p => string.Equals(p.Name, parameterName, StringComparison.OrdinalIgnoreCase));
        if (parameter is null)
        {
            return Error.NotFound("Report.NoParameter", $"Report {reportCode} has no parameter {parameterName}.");
        }

        if (string.IsNullOrWhiteSpace(parameter.LookupQuery))
        {
            return Result.Success<IReadOnlyList<ParameterOption>>([]);
        }

        // The values entered so far, typed as their parameters say; a lookup waits until those it depends on are given.
        Dictionary<string, object?> typed = new(StringComparer.OrdinalIgnoreCase);
        foreach (ReportParameter other in definition.Parameters.Where(p => !ReferenceEquals(p, parameter)))
        {
            typed[other.Name] = values.TryGetValue(other.Name, out string? raw) ? ReportParameterValues.Parse(other, raw) : null;
        }

        IReadOnlyList<string> waiting = ReportParameterValues.WaitingOn(parameter, typed);
        if (waiting.Count > 0)
        {
            return Error.Validation("Report.ParameterWaiting", $"Choose {string.Join(", ", waiting)} first.");
        }

        (string? _, string? refusal) = SqlReportDataProvider.Statement("sql:" + parameter.LookupQuery, [.. typed.Keys]);
        if (refusal is not null)
        {
            return Error.Validation("Report.LookupRefused", $"The list for {parameter.Name} cannot run: {refusal}");
        }

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = await _sql.GetRowsAsync(companyId, "sql:" + parameter.LookupQuery, typed, ct).ConfigureAwait(false);
        List<ParameterOption> options = [];
        foreach (IReadOnlyDictionary<string, object?> row in rows.Take(MaxOptions))
        {
            object?[] cells = [.. row.Values];
            if (cells.Length == 0 || cells[0] is null)
            {
                continue;
            }

            string value = Text(cells[0]);
            options.Add(new ParameterOption(value, cells.Length > 1 && cells[1] is not null ? Text(cells[1]) : value));
        }

        return Result.Success<IReadOnlyList<ParameterOption>>(options);
    }

    private static string Text(object? value) => value switch
    {
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTime d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };
}
