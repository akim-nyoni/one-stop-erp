using One4All.Core.Common;

namespace One4All.Core.Reporting;

/// <summary>What a report was run for (brief §13.3: every print/export written to the audit log).</summary>
public enum ReportRunPurpose
{
    /// <summary>On-screen preview.</summary>
    View = 1,

    /// <summary>Printed.</summary>
    Print = 2,

    /// <summary>Exported to a file.</summary>
    Export = 3,

    /// <summary>Printed again — the same report with the same parameters had been printed before.</summary>
    Reprint = 4,
}

/// <summary>
/// One run of a report (<c>rpt.report_run</c>): who ran which report with which parameters, for what, the document's size
/// and SHA-256 fingerprint, and — for prints — which copy it was. Append-only.
/// </summary>
public sealed class ReportRun : Entity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private ReportRun()
    {
        ReportCode = string.Empty;
        Parameters = string.Empty;
        ContentHash = string.Empty;
        Format = string.Empty;
    }

    /// <summary>Records a run.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="reportCode">The report.</param>
    /// <param name="userId">Who ran it, when known.</param>
    /// <param name="purpose">What for.</param>
    /// <param name="format">The output format.</param>
    /// <param name="parameters">The parameters, as canonical text.</param>
    /// <param name="runAt">When.</param>
    /// <param name="bytes">The document's size.</param>
    /// <param name="contentHash">The document's SHA-256.</param>
    /// <param name="copyNumber">For prints, which copy (1 for the original).</param>
    public ReportRun(long companyId, string reportCode, long? userId, ReportRunPurpose purpose, string format, string parameters, DateTimeOffset runAt, int bytes, string contentHash, int? copyNumber)
    {
        CompanyId = companyId;
        ReportCode = reportCode;
        UserId = userId;
        Purpose = purpose;
        Format = format;
        Parameters = parameters;
        RunAt = runAt;
        Bytes = bytes;
        ContentHash = contentHash;
        CopyNumber = copyNumber;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The report.</summary>
    public string ReportCode { get; private set; }

    /// <summary>Who ran it, when known.</summary>
    public long? UserId { get; private set; }

    /// <summary>What for.</summary>
    public ReportRunPurpose Purpose { get; private set; }

    /// <summary>The output format.</summary>
    public string Format { get; private set; }

    /// <summary>The parameters, as canonical text.</summary>
    public string Parameters { get; private set; }

    /// <summary>When.</summary>
    public DateTimeOffset RunAt { get; private set; }

    /// <summary>The document's size.</summary>
    public int Bytes { get; private set; }

    /// <summary>The document's SHA-256.</summary>
    public string ContentHash { get; private set; }

    /// <summary>For prints, which copy.</summary>
    public int? CopyNumber { get; private set; }
}
