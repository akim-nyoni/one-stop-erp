using System.IO.Compression;
using System.Text;
using FluentAssertions;
using One4All.Application.Onboarding;
using Xunit;

namespace One4All.UnitTests.Onboarding;

/// <summary>Covers reading an Excel worksheet as CSV: shared and inline strings, numbers, booleans, a date-styled cell, a skipped column and quoting.</summary>
public sealed class XlsxReaderTests
{
    [Fact]
    public void A_worksheet_reads_as_csv()
    {
        byte[] workbook = Workbook(
            """<sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><si><t>stand</t></si><si><t>parcel</t></si><si><t>Rue Émile, Zola</t></si></sst>""",
            """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>
              <row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1" t="s"><v>1</v></c><c r="D1" t="inlineStr"><is><t>when</t></is></c></row>
              <row r="2"><c r="A2" t="s"><v>2</v></c><c r="B2"><v>1250.5</v></c><c r="C2" t="b"><v>1</v></c><c r="D2" s="1"><v>46023</v></c></row>
              <row r="3"/>
            </sheetData></worksheet>
            """);

        XlsxReader.IsWorkbook(workbook).Should().BeTrue();
        XlsxReader.IsWorkbook(Encoding.UTF8.GetBytes("a,b")).Should().BeFalse();
        XlsxReader.ToCsv(workbook).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Should().Equal("stand,parcel,,when", "\"Rue Émile, Zola\",1250.5,true,2026-01-01");
    }

    private static byte[] Workbook(string sharedStrings, string sheet)
    {
        using MemoryStream stream = new();
        using (ZipArchive zip = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, string xml)
            {
                using StreamWriter writer = new(zip.CreateEntry(path).Open(), new UTF8Encoding(false));
                writer.Write(xml);
            }

            Add("xl/workbook.xml", """
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                <sheets><sheet name="Stands" sheetId="1" r:id="rId1"/></sheets></workbook>
                """);
            Add("xl/_rels/workbook.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/></Relationships>
                """);
            Add("xl/styles.xml", """
                <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><cellXfs count="2"><xf numFmtId="0"/><xf numFmtId="14"/></cellXfs></styleSheet>
                """);
            Add("xl/sharedStrings.xml", sharedStrings);
            Add("xl/worksheets/sheet1.xml", sheet);
        }

        return stream.ToArray();
    }
}
