using One4All.Core.Common;

namespace One4All.Application.Abstractions.Reporting;

/// <summary>A column a report data source returns.</summary>
/// <param name="Name">The column name.</param>
/// <param name="TypeName">Its database type.</param>
public sealed record ReportFieldInfo(string Name, string TypeName);

/// <summary>
/// The designer's field discovery (brief §13.1 "a field-discovery step (SELECT … LIMIT 0) to read schema"): the columns a SQL, view or function source
/// returns, read without fetching any rows, after the same safety checks as running it.
/// </summary>
public interface IReportFieldDiscovery
{
    /// <summary>The columns a data source returns.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="command">The command (sql:…, view:…, function:…).</param>
    /// <param name="parameters">The report parameters (bound by name).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The columns in order, or why the source was refused.</returns>
    Task<Result<IReadOnlyList<ReportFieldInfo>>> DescribeAsync(long companyId, string command, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct = default);
}
