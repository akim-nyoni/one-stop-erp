using One4All.Core.Common;

namespace One4All.Core.Reporting;

/// <summary>A report a company's verticals feature (brief §12 report set): listed first in the report viewer.</summary>
public sealed class FeaturedReport : AuditableEntity, ICompanyScoped
{
    private FeaturedReport()
    {
        ReportCode = string.Empty;
        Source = string.Empty;
    }

    /// <summary>Features a report.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="reportCode">The report code.</param>
    /// <param name="source">What featured it (a vertical, or "user").</param>
    public FeaturedReport(long companyId, string reportCode, string source)
    {
        CompanyId = companyId;
        ReportCode = reportCode;
        Source = source;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the report code.</summary>
    public string ReportCode { get; private set; }

    /// <summary>Gets what featured it.</summary>
    public string Source { get; private set; }
}
