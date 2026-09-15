using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Common;
using One4All.Core.Reporting;
using One4All.Infrastructure.Persistence;
using One4All.Reporting;
using One4All.Reporting.Engine;
using One4All.Reporting.Model;

namespace One4All.Infrastructure.Reporting;

/// <summary>
/// Renders the core financial reports by feeding the live report DTOs (from <see cref="IGeneralLedgerReports"/>)
/// through the banded report engine and a format renderer (brief §16 Phase 3). The report layout is resolved
/// from <c>rpt.report_definition</c> (user → company → system), falling back to the built-in system definition.
/// </summary>
public sealed class FinancialReportRenderer : IFinancialReportRenderer
{
    private readonly One4AllDbContext _db;
    private readonly IGeneralLedgerReports _reports;
    private readonly ReportEngine _engine = new();

    /// <summary>Creates the renderer.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="reports">The GL report services.</param>
    public FinancialReportRenderer(One4AllDbContext db, IGeneralLedgerReports reports)
    {
        _db = db;
        _reports = reports;
    }

    /// <inheritdoc />
    public async Task<Result<RenderedDocument>> RenderTrialBalanceAsync(long companyId, DateOnly asAtDate, ReportFormat format, CancellationToken ct = default)
    {
        TrialBalanceReport tb = await _reports.TrialBalanceAsync(companyId, asAtDate, ct).ConfigureAwait(false);
        string companyName = await _db.Companies.Where(c => c.Id == companyId).Select(c => c.Name).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? string.Empty;

        List<IReadOnlyDictionary<string, object?>> rows = tb.Rows
            .Select(r => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
            {
                ["AccountCode"] = r.AccountCode,
                ["AccountName"] = r.AccountName,
                ["Debit"] = r.Debit,
                ["Credit"] = r.Credit,
            })
            .ToList();

        ReportDefinition definition = await ResolveDefinitionAsync(companyId, SystemReports.TrialBalanceCode, ct).ConfigureAwait(false)
            ?? SystemReports.TrialBalance();

        ReportInput input = new()
        {
            Definition = definition,
            Rows = rows,
            CompanyName = companyName,
            Parameters = new Dictionary<string, object?> { ["AsAtDate"] = asAtDate.ToDateTime(TimeOnly.MinValue), ["Currency"] = tb.Currency },
        };

        return Render(input, format, "trial-balance");
    }

    private Result<RenderedDocument> Render(ReportInput input, ReportFormat format, string baseName)
    {
        if (format == ReportFormat.Csv)
        {
            byte[] csv = new CsvReportExporter().Export(input.Rows);
            return new RenderedDocument(csv, "text/csv", $"{baseName}.csv");
        }

        RenderModel model = _engine.Build(input);
        (byte[] bytes, string contentType, string ext) = format switch
        {
            ReportFormat.Pdf => (new PdfReportRenderer().Render(model), "application/pdf", "pdf"),
            ReportFormat.Text => (new PlainTextReportRenderer().Render(model), "text/plain", "txt"),
            _ => (new HtmlReportRenderer().Render(model), "text/html", "html"),
        };

        return new RenderedDocument(bytes, contentType, $"{baseName}.{ext}");
    }

    private async Task<ReportDefinition?> ResolveDefinitionAsync(long companyId, string code, CancellationToken ct)
    {
        // Precedence: company customisation, then system. (User-level resolution needs the current user.)
        ReportDefinitionRecord? record = await _db.ReportDefinitions
            .Where(r => r.Code == code && (r.Scope == ReportScope.System || (r.Scope == ReportScope.Company && r.CompanyId == companyId)))
            .OrderByDescending(r => r.Scope)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return record is null ? null : ReportDefinitionSerializer.Deserialize(record.DefinitionJson);
    }
}
