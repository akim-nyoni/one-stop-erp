using One4All.Core.Common;

namespace One4All.Application.Abstractions.Reporting;

/// <summary>One choice for a report parameter.</summary>
/// <param name="Value">The value passed to the report.</param>
/// <param name="Label">What the user sees.</param>
public sealed record ParameterOption(string Value, string Label);

/// <summary>
/// The choices for a report parameter with a list-of-values query (brief §13.1): the query runs read-only for the company, with the values
/// of the parameters it depends on, so a choice can cascade from another (a branch's warehouses, a ward's properties).
/// </summary>
public interface IReportParameterLookupService
{
    /// <summary>The choices for a parameter.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="reportCode">The report.</param>
    /// <param name="parameterName">The parameter.</param>
    /// <param name="values">The values entered so far (by parameter name, as typed).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The choices (value first column, label second), or why there are none.</returns>
    Task<Result<IReadOnlyList<ParameterOption>>> OptionsAsync(long companyId, string reportCode, string parameterName, IReadOnlyDictionary<string, string> values,
        CancellationToken ct = default);
}
