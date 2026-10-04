using FluentAssertions;
using One4All.Reporting;
using One4All.Reporting.Model;
using Xunit;

namespace One4All.UnitTests.Reporting;

public class ReportCatalogueDocumentTests
{
    [Fact]
    public void A_report_is_listed_with_its_parameters_columns_grouping_and_totals()
    {
        ReportDefinition report = new()
        {
            Code = "X-1", Name = "Sales | by rep", Description = "Who sold what", Module = "Sales",
            Parameters = [new ReportParameter { Name = "From", Type = ParameterType.Date }, new ReportParameter { Name = "Rep", Type = ParameterType.String, Required = false }],
            Groups = [new ReportGroup { Expression = "Rep" }],
            Bands =
            [
                new ReportBand { Type = BandType.Detail, Elements = [new ReportElement { Kind = ElementKind.Field, FieldName = "Invoice" }, new ReportElement { Kind = ElementKind.Expression, Name = "Margin" }] },
                new ReportBand { Type = BandType.GroupFooter, Elements = [new ReportElement { Kind = ElementKind.Aggregate, Aggregate = AggregateFunction.Sum, AggregateField = "Amount" }] },
            ],
        };

        string md = ReportCatalogueDocument.Render([report]);
        md.Should().Contain("## Sales").And.Contain("| X-1 | Sales \\| by rep | Who sold what | From (Date), Rep (String, optional) | Invoice, Margin | Rep | Sum(Amount) |");
    }

    [Fact]
    public void Modules_are_sections_and_empty_cells_show_a_dash()
    {
        string md = ReportCatalogueDocument.Render([new ReportDefinition { Code = "B", Name = "Bare" }, new ReportDefinition { Code = "A", Name = "Alpha", Module = "AR" }]);
        md.IndexOf("## AR", StringComparison.Ordinal).Should().BeLessThan(md.IndexOf("## General", StringComparison.Ordinal));
        md.Should().Contain("| B | Bare | — | — | — | — | — |").And.Contain("2 reports.");
    }

    [Fact]
    public void The_committed_catalogue_matches_the_shipped_reports()
    {
        string expected = ReportCatalogueDocument.Render(SystemReportCatalogue.All());
        string path = Path.Combine(RepositoryRoot(), "docs", "reports", "CATALOGUE.md");
        if (Environment.GetEnvironmentVariable("ONE4ALL_WRITE_CATALOGUE") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, expected);
        }

        File.Exists(path).Should().BeTrue("docs/reports/CATALOGUE.md must exist (regenerate with ONE4ALL_WRITE_CATALOGUE=1)");
        File.ReadAllText(path).ReplaceLineEndings("\n").Should().Be(expected, "the catalogue is generated from the report definitions — regenerate it with ONE4ALL_WRITE_CATALOGUE=1");
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "One4All.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root (One4All.slnx) not found.");
    }
}
