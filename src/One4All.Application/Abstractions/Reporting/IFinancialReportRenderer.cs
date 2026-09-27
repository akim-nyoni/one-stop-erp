using One4All.Core.Common;

namespace One4All.Application.Abstractions.Reporting;

/// <summary>Output formats a report can be rendered to (brief §13.3).</summary>
public enum ReportFormat
{
    /// <summary>HTML.</summary>
    Html = 0,

    /// <summary>PDF.</summary>
    Pdf = 1,

    /// <summary>CSV export.</summary>
    Csv = 2,

    /// <summary>Plain text / ESC-POS.</summary>
    Text = 3,

    /// <summary>Excel workbook (.xlsx / OpenXML SpreadsheetML).</summary>
    Xlsx = 4,
}

/// <summary>A rendered report document.</summary>
/// <param name="Content">The document bytes.</param>
/// <param name="ContentType">The MIME type.</param>
/// <param name="FileName">A suggested file name.</param>
public readonly record struct RenderedDocument(byte[] Content, string ContentType, string FileName);

/// <summary>
/// Renders the core financial reports (defined as system report definitions) against live data through the
/// report engine (brief §16 Phase 3: seed the Phase 2 reports on the reporting foundation).
/// </summary>
public interface IFinancialReportRenderer
{
    /// <summary>Renders the Trial Balance as at a date to the requested format.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="asAtDate">The as-at date.</param>
    /// <param name="format">The output format.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The rendered document, or an error.</returns>
    Task<Result<RenderedDocument>> RenderTrialBalanceAsync(long companyId, DateOnly asAtDate, ReportFormat format, CancellationToken ct = default);

    /// <summary>Renders a customer's open-item statement to the requested format.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="customerId">The customer.</param>
    /// <param name="format">The output format.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The rendered document, or an error.</returns>
    Task<Result<RenderedDocument>> RenderCustomerStatementAsync(long companyId, long customerId, ReportFormat format, CancellationToken ct = default);
}
