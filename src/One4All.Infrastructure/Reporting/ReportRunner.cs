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
/// The generic report runner (brief §13): resolves a report definition by code (company → system, falling
/// back to a built-in system definition), fetches live rows from the <see cref="IReportDataProvider"/> that
/// handles the definition's data-source command, and renders to the requested format through the banded
/// engine. One entry point for every report in the catalogue, whatever module owns it.
/// </summary>
public sealed class ReportRunner : IReportRunner
{
    private readonly One4AllDbContext _db;
    private readonly IEnumerable<IReportDataProvider> _providers;
    private readonly ReportEngine _engine = new();

    /// <summary>Creates the runner.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="providers">The registered data providers.</param>
    public ReportRunner(One4AllDbContext db, IEnumerable<IReportDataProvider> providers)
    {
        _db = db;
        _providers = providers;
    }

    /// <inheritdoc />
    public async Task<Result<RenderedDocument>> RenderAsync(long companyId, string reportCode, IReadOnlyDictionary<string, object?> parameters, ReportFormat format, CancellationToken ct = default)
    {
        ReportDefinition? definition = await ResolveDefinitionAsync(companyId, reportCode, ct).ConfigureAwait(false)
            ?? BuiltInDefinition(reportCode);
        if (definition is null)
        {
            return Error.NotFound("Report.NotFound", $"No report definition found for code '{reportCode}'.");
        }

        string command = definition.DataSources.FirstOrDefault()?.Command ?? reportCode;
        IReportDataProvider? provider = _providers.FirstOrDefault(p => p.CanHandle(command));
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = provider is null
            ? []
            : await provider.GetRowsAsync(companyId, command, parameters, ct).ConfigureAwait(false);

        string companyName = await _db.Companies.Where(c => c.Id == companyId).Select(c => c.Name).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? string.Empty;

        ReportInput input = new()
        {
            Definition = definition,
            Rows = rows,
            CompanyName = companyName,
            Parameters = parameters,
        };

        return Render(input, format, reportCode.ToLowerInvariant());
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
        ReportDefinitionRecord? record = await _db.ReportDefinitions
            .Where(r => r.Code == code && (r.Scope == ReportScope.System || (r.Scope == ReportScope.Company && r.CompanyId == companyId)))
            .OrderByDescending(r => r.Scope)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return record is null ? null : ReportDefinitionSerializer.Deserialize(record.DefinitionJson);
    }

    private static ReportDefinition? BuiltInDefinition(string code) => code switch
    {
        SystemReports.TrialBalanceCode => SystemReports.TrialBalance(),
        SystemReports.CustomerStatementCode => SystemReports.CustomerStatement(),
        SystemReports.IncomeStatementCode => SystemReports.IncomeStatement(),
        SystemReports.BalanceSheetCode => SystemReports.BalanceSheet(),
        SystemReports.AgedDebtorsCode => SystemReports.AgedDebtors(),
        SystemReports.AgedCreditorsCode => SystemReports.AgedCreditors(),
        SystemReports.StockValuationCode => SystemReports.StockValuation(),
        SystemReports.AssetRegisterCode => SystemReports.AssetRegister(),
        SystemReports.PayrollSummaryCode => SystemReports.PayrollSummary(),
        SystemReports.PropertyRegisterCode => SystemReports.PropertyRegister(),
        SystemReports.BudgetVsActualCode => SystemReports.BudgetVsActual(),
        _ => ReceiptingReports.All().FirstOrDefault(r => string.Equals(r.Code, code, StringComparison.OrdinalIgnoreCase)),
    };
}
