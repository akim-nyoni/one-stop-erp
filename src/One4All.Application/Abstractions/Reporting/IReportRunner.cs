using One4All.Core.Common;

namespace One4All.Application.Abstractions.Reporting;

/// <summary>
/// Runs any seeded report by code (brief §13): resolves its definition (user → company → system), fetches
/// live rows from the data provider that handles the definition's data-source command, and renders to the
/// requested format through the banded report engine. This is the single generic entry point for every
/// report in the catalogue, whatever module it belongs to.
/// </summary>
public interface IReportRunner
{
    /// <summary>Renders a report by its code against live data.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="reportCode">The report code (e.g. <c>GL-TB</c>, <c>AR-AGE</c>, <c>RC45</c>).</param>
    /// <param name="parameters">The run-time report parameters.</param>
    /// <param name="format">The output format.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The rendered document, or an error (e.g. an unknown report code).</returns>
    Task<Result<RenderedDocument>> RenderAsync(long companyId, string reportCode, IReadOnlyDictionary<string, object?> parameters, ReportFormat format, CancellationToken ct = default);

    /// <summary>Renders a report leaving out restricted columns the user may not see (brief §5.1 field-level sensitivity).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="reportCode">The report code.</param>
    /// <param name="parameters">The run-time report parameters.</param>
    /// <param name="format">The output format.</param>
    /// <param name="hiddenFields">The kinds of field to leave out.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The rendered document, or an error.</returns>
    Task<Result<RenderedDocument>> RenderAsync(long companyId, string reportCode, IReadOnlyDictionary<string, object?> parameters, ReportFormat format,
        IReadOnlySet<One4All.Application.Security.SensitiveField> hiddenFields, CancellationToken ct = default)
        => RenderAsync(companyId, reportCode, parameters, format, ct);
}
