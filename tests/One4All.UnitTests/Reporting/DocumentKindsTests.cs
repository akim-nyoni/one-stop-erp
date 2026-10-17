using FluentAssertions;
using One4All.Application.Documents;
using One4All.Reporting;
using One4All.Reporting.Model;
using Xunit;

namespace One4All.UnitTests.Reporting;

/// <summary>Every document kind (brief §S) has its designable DOC report, with the right data-source command and identification, so a company can lay out any of them.</summary>
public sealed class DocumentKindsTests
{
    [Fact]
    public void Every_document_kind_has_a_designable_report_with_its_command_and_parameters()
    {
        Dictionary<string, ReportDefinition> reports = SystemReportCatalogue.All().Where(r => r.Code.StartsWith("DOC", StringComparison.Ordinal)).ToDictionary(r => r.Code, StringComparer.Ordinal);
        DocumentKinds.All.Should().HaveCount(33);
        DocumentKinds.All.Select(k => k.DesignableReportCode).Should().OnlyHaveUniqueItems().And.BeEquivalentTo(reports.Keys);
        DocumentKinds.All.Select(k => k.Kind).Should().OnlyHaveUniqueItems();
        foreach (DocumentKind kind in DocumentKinds.All)
        {
            ReportDefinition report = reports[kind.DesignableReportCode];
            report.DataSources.Single().Command.Should().Be("Doc" + kind.Kind, kind.Name);
            report.Parameters.Select(p => p.Name).Should().Contain("Number", kind.Name);
            report.Parameters.Any(p => p.Name == "Date").Should().Be(kind.NeedsDate, $"{kind.Name} needs a date: {kind.NeedsDate}");
            if (kind.Text)
            {
                report.Bands.SelectMany(b => b.Elements).Where(e => e.Kind == ElementKind.Field).Select(e => e.FieldName).Should().Contain(["Title", "Number", "Date", "LineNo", "Line"], kind.Name);
            }
        }

        DocumentKinds.Find("payslip")!.Kind.Should().Be(DocumentKinds.Payslip);
        DocumentKinds.Find("Chain-of-title printout")!.DesignableReportCode.Should().Be("DOC30");
        DocumentKinds.All.Count(k => k.Text).Should().Be(21, "the text-backed documents");
    }
}
