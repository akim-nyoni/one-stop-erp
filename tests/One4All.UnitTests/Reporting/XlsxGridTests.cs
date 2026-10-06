using System.IO.Compression;
using System.Text;
using FluentAssertions;
using One4All.Reporting.Engine;
using Xunit;

namespace One4All.UnitTests.Reporting;

/// <summary>Covers the .xlsx grid: what it writes reads back, and a workbook laid out the way Excel saves one (shared strings, sparse cells, formula results) reads too.</summary>
public sealed class XlsxGridTests
{
    [Fact]
    public void A_written_grid_reads_back()
    {
        byte[] xlsx = XlsxGrid.Write("Budget: 2026/27", [["Account", "P1", "Total"], ["6000", 1250.5m, new XlsxFormula("SUM(B2:B2)")], [null, "a < b & c", 7]], boldRows: 1);

        IReadOnlyList<IReadOnlyList<string?>> rows = XlsxGrid.Read(xlsx);
        rows[0].Should().Equal("Account", "P1", "Total");
        rows[1].Should().Equal("6000", "1250.5", null);
        rows[2].Should().Equal(null, "a < b & c", "7");
    }

    [Fact]
    public void A_workbook_saved_by_Excel_reads()
    {
        using MemoryStream stream = new();
        using (ZipArchive zip = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Put(zip, "xl/workbook.xml", """<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Budget" sheetId="1" r:id="rId3"/></sheets></workbook>""");
            Put(zip, "xl/_rels/workbook.xml.rels", """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId3" Type="worksheet" Target="worksheets/sheet7.xml"/></Relationships>""");
            Put(zip, "xl/sharedStrings.xml", """<sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><si><t>Account</t></si><si><r><t>Rich</t></r><r><t> text</t></r></si></sst>""");
            Put(zip, "xl/worksheets/sheet7.xml", """<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData><row r="1"><c r="A1" t="s"><v>0</v></c><c r="C1" t="s"><v>1</v></c></row><row r="3"><c r="B3"><f>1+1</f><v>2</v></c><c r="AA3" t="b"><v>1</v></c><c r="AB3" t="str"><f>"x"</f><v>x</v></c></row></sheetData></worksheet>""");
        }

        IReadOnlyList<IReadOnlyList<string?>> rows = XlsxGrid.Read(stream.ToArray());
        rows.Should().HaveCount(3);
        rows[0].Should().Equal("Account", null, "Rich text");
        rows[1].Should().BeEmpty();
        rows[2][1].Should().Be("2");
        rows[2][26].Should().Be("TRUE");
        rows[2][27].Should().Be("x");
    }

    [Fact]
    public void Something_that_is_not_a_workbook_is_refused()
    {
        Action read = () => XlsxGrid.Read(Encoding.UTF8.GetBytes("Account,P1\n6000,10"));
        read.Should().Throw<InvalidDataException>();
    }

    private static void Put(ZipArchive zip, string path, string xml)
    {
        using StreamWriter writer = new(zip.CreateEntry(path).Open());
        writer.Write(xml);
    }
}
