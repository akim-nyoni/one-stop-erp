namespace One4All.Application.Abstractions.Reporting;

/// <summary>
/// Supplies live rows for a report data-source command (brief §13.1). One provider per functional area (GL,
/// trading, receipting, …); the report runner dispatches a report's data-source command to the provider that
/// handles it. Rows are field dictionaries keyed by the field names the report definition binds to.
/// </summary>
public interface IReportDataProvider
{
    /// <summary>Whether this provider supplies rows for the given data-source command.</summary>
    /// <param name="command">The report data-source command (e.g. <c>TrialBalance</c>, <c>RC45</c>).</param>
    /// <returns>True when this provider handles the command.</returns>
    bool CanHandle(string command);

    /// <summary>
    /// The permission module whose reports right a report on this provider's data needs (e.g. <c>Payroll</c>), or null when the data needs no module
    /// right of its own. A user-defined report cannot relax it by naming another module in its saved definition.
    /// </summary>
    string? Module => null;

    /// <summary>Produces the rows for a report data-source command.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="command">The data-source command.</param>
    /// <param name="parameters">The run-time report parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The rows, as field dictionaries.</returns>
    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetRowsAsync(long companyId, string command, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct = default);
}
