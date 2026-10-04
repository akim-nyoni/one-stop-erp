using One4All.Core.Common;
using One4All.Core.Reporting;

namespace One4All.Application.Abstractions.Reporting;

/// <summary>A report run as the audit log shows it.</summary>
/// <param name="RunAt">When.</param>
/// <param name="ReportCode">The report.</param>
/// <param name="UserId">Who.</param>
/// <param name="Purpose">What for.</param>
/// <param name="Format">The output format.</param>
/// <param name="Parameters">The parameters.</param>
/// <param name="CopyNumber">For prints, which copy.</param>
/// <param name="ContentHash">The document's fingerprint.</param>
public sealed record ReportRunEntry(DateTimeOffset RunAt, string ReportCode, long? UserId, ReportRunPurpose Purpose, string Format, string Parameters, int? CopyNumber, string ContentHash);

/// <summary>An audited run's document.</summary>
/// <param name="Document">The rendered document.</param>
/// <param name="Purpose">What it was recorded as (a repeated print becomes a reprint).</param>
/// <param name="CopyNumber">For prints, which copy.</param>
public sealed record AuditedReport(RenderedDocument Document, ReportRunPurpose Purpose, int? CopyNumber);

/// <summary>
/// Report runs with security and an audit trail (brief §13.3): when the user is known, the run needs the
/// <c>Reporting.Report</c> permission flag for its purpose (view, print, export, reprint); every run is recorded with its
/// parameters and the document's fingerprint; and printing what was printed before is recorded as a numbered reprint.
/// </summary>
public interface IAuditedReportRunner
{
    /// <summary>Runs a report for a purpose and records it.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="reportCode">The report.</param>
    /// <param name="parameters">The parameters.</param>
    /// <param name="format">The output format.</param>
    /// <param name="purpose">What it is run for.</param>
    /// <param name="userId">Who runs it (checked against their permissions), when known.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The document, or why it was refused.</returns>
    Task<Result<AuditedReport>> RunAsync(long companyId, string reportCode, IReadOnlyDictionary<string, object?> parameters, ReportFormat format, ReportRunPurpose purpose, long? userId, CancellationToken ct = default);

    /// <summary>The runs of a report, newest first.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="reportCode">The report.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The runs.</returns>
    Task<IReadOnlyList<ReportRunEntry>> RunLogAsync(long companyId, string reportCode, CancellationToken ct = default);
}
