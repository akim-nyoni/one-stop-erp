using System.IO.Compression;
using System.Text;
using FluentAssertions;
using One4All.Reporting.Engine;
using Xunit;

namespace One4All.UnitTests.Reporting;

/// <summary>Covers the dependency-free XLSX exporter: a valid single-sheet workbook with headers and typed cells.</summary>
public class XlsxReportExporterTests
{
    [Fact]
    public void Export_produces_a_valid_workbook_with_headers_and_numeric_cells()
    {
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows =
        [
            new Dictionary<string, object?> { ["Account"] = "1100", ["Name"] = "Bank", ["Balance"] = 1234.50m },
            new Dictionary<string, object?> { ["Account"] = "4000", ["Name"] = "Sales", ["Balance"] = -980m },
        ];

        byte[] bytes = new XlsxReportExporter().Export(rows, "Trial Balance");

        using MemoryStream stream = new(bytes);
        using ZipArchive zip = new(stream, ZipArchiveMode.Read);

        // A minimal but complete OpenXML package.
        zip.GetEntry("[Content_Types].xml").Should().NotBeNull();
        zip.GetEntry("xl/workbook.xml").Should().NotBeNull();
        zip.GetEntry("xl/_rels/workbook.xml.rels").Should().NotBeNull();
        zip.GetEntry("xl/styles.xml").Should().NotBeNull();

        ZipArchiveEntry? sheet = zip.GetEntry("xl/worksheets/sheet1.xml");
        sheet.Should().NotBeNull();

        using StreamReader reader = new(sheet!.Open(), Encoding.UTF8);
        string xml = reader.ReadToEnd();

        xml.Should().Contain("Account").And.Contain("Balance");          // header row (inline strings)
        xml.Should().Contain("<v>1234.5</v>");                            // numeric cell, not a string
        xml.Should().Contain("<v>-980</v>");
        xml.Should().Contain(">Bank<");                                   // text cell
    }

    [Fact]
    public void Export_of_no_rows_still_produces_a_readable_workbook()
    {
        byte[] bytes = new XlsxReportExporter().Export([], "Empty");

        using MemoryStream stream = new(bytes);
        using ZipArchive zip = new(stream, ZipArchiveMode.Read);
        zip.GetEntry("xl/worksheets/sheet1.xml").Should().NotBeNull();
    }
}
