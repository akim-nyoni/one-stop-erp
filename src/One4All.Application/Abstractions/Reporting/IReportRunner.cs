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

    /// <summary>
    /// Renders a report for a user: refused when they may not run its module's reports (or its own required permission), restricted
    /// columns left out, and rows cut to their data scope (brief §13.3).
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="reportCode">The report code.</param>
    /// <param name="parameters">The run-time report parameters.</param>
    /// <param name="format">The output format.</param>
    /// <param name="access">What the user may run and see.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The rendered document, or an error.</returns>
    Task<Result<RenderedDocument>> RenderAsync(long companyId, string reportCode, IReadOnlyDictionary<string, object?> parameters, ReportFormat format,
        One4All.Application.Reporting.ReportAccess access, CancellationToken ct = default)
        => RenderAsync(companyId, reportCode, parameters, format, access.Hidden, ct);

    /// <summary>
    /// Runs a report once and splits it by a key field (brief §13.3 bursting): each key's rows rendered as their own document.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="reportCode">The report.</param>
    /// <param name="parameters">Its parameters.</param>
    /// <param name="format">The output format.</param>
    /// <param name="keyField">The field the run is split by (e.g. the customer code).</param>
    /// <param name="access">What the user may run and see.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A document per key, with the key's rows.</returns>
    Task<Result<IReadOnlyList<BurstSlice>>> BurstAsync(long companyId, string reportCode, IReadOnlyDictionary<string, object?> parameters, ReportFormat format, string keyField,
        One4All.Application.Reporting.ReportAccess access, CancellationToken ct = default)
        => Task.FromResult(Result<IReadOnlyList<BurstSlice>>.Failure(Error.Rule("Rpt.BurstUnsupported", "This report runner cannot burst.")));
}

/// <summary>One key's share of a burst report.</summary>
/// <param name="Key">The key value.</param>
/// <param name="Document">Its document.</param>
/// <param name="Rows">Its rows.</param>
public sealed record BurstSlice(string Key, RenderedDocument Document, IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows);
