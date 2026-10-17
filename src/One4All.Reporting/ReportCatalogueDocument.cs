using System.Text;
using One4All.Reporting.Model;

namespace One4All.Reporting;

/// <summary>
/// Renders the report catalogue (brief §14: <c>docs/reports/CATALOGUE.md</c> listing each report with code, name, module,
/// purpose, parameters, columns, grouping, totals and output formats) from the report definitions themselves, so the
/// document can never drift from what ships. Pure and unit-tested.
/// </summary>
public static class ReportCatalogueDocument
{
    /// <summary>The output formats every report renders to.</summary>
    public const string Formats = "HTML · PDF · XLSX · CSV · Text";

    /// <summary>Renders the catalogue as Markdown, one section per module in code order.</summary>
    /// <param name="definitions">The report definitions.</param>
    /// <returns>The Markdown document.</returns>
    public static string Render(IEnumerable<ReportDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        List<ReportDefinition> reports = [.. definitions];
        StringBuilder md = new();
        md.Append("# Report Catalogue\n\n");
        md.Append("Generated from the system report definitions (`SystemReportCatalogue.All()`); do not edit by hand - ");
        md.Append("run the `ReportCatalogueDocumentTests` with `ONE4ALL_WRITE_CATALOGUE=1` to regenerate.\n\n");
        md.Append($"{reports.Count} reports. Every report renders to {Formats}.\n");

        foreach (IGrouping<string, ReportDefinition> module in reports.GroupBy(r => string.IsNullOrWhiteSpace(r.Module) ? "General" : r.Module!).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            md.Append($"\n## {module.Key}\n\n");
            md.Append("| Code | Name | Purpose | Parameters | Columns | Grouping | Totals |\n");
            md.Append("|---|---|---|---|---|---|---|\n");
            foreach (ReportDefinition report in module.OrderBy(r => r.Code, StringComparer.Ordinal))
            {
                md.Append($"| {Cell(report.Code)} | {Cell(report.Name)} | {Cell(report.Description)} | {Cell(Parameters(report))} | {Cell(Columns(report))} | {Cell(Grouping(report))} | {Cell(Totals(report))} |\n");
            }
        }

        return md.ToString();
    }

    private static string Parameters(ReportDefinition report)
        => string.Join(", ", report.Parameters.Select(p => $"{p.Name} ({p.Type}{(p.Required ? string.Empty : ", optional")})"));

    private static string Columns(ReportDefinition report)
        => string.Join(", ", report.Bands.Where(b => b.Type == BandType.Detail).SelectMany(b => b.Elements)
            .Where(e => e.Kind is ElementKind.Field or ElementKind.Expression)
            .Select(e => e.Kind == ElementKind.Field && !string.IsNullOrWhiteSpace(e.FieldName) ? e.FieldName! : e.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.Ordinal));

    private static string Grouping(ReportDefinition report)
        => string.Join(" › ", report.Groups.Select(g => g.Descending ? $"{g.Expression} (desc)" : g.Expression));

    private static string Totals(ReportDefinition report)
        => string.Join(", ", report.Bands.Where(b => b.Type != BandType.Detail).SelectMany(b => b.Elements)
            .Where(e => e.Kind == ElementKind.Aggregate)
            .Select(e => $"{e.Aggregate}({e.AggregateField ?? e.Expression})").Distinct(StringComparer.Ordinal));

    // Table cells: pipes escaped, line breaks flattened, empty shown as a dash.
    private static string Cell(string? text)
        => string.IsNullOrWhiteSpace(text) ? "-" : text.Replace("|", "\\|", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();
}
