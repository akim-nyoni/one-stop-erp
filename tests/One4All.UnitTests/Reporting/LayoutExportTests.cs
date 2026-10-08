using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using One4All.Reporting.Engine;
using One4All.Reporting.Model;
using Xunit;

namespace One4All.UnitTests.Reporting;

/// <summary>
/// Covers exporting a report as laid out (brief §13.3: XLSX "with real columns", plus CSV and DOCX): the column titles, group headers,
/// detail lines and totals in order and in columns taken from where the text sits, figures as numbers and codes with leading zeros as text.
/// </summary>
public sealed class LayoutExportTests
{
    [Fact]
    public void The_grid_follows_the_report_layout()
    {
        IReadOnlyList<GridRow> rows = LayoutGrid.Build(Model());
        rows.Select(r => r.Band).Should().Equal(BandType.PageHeader, BandType.GroupHeader, BandType.Detail, BandType.Detail, BandType.GroupFooter);
        rows[0].Cells.Should().Equal("Account", "Name", "Balance");
        rows[1].Cells.Should().Equal(new object?[] { "Assets", null, null });
        rows[2].Cells.Should().Equal("0110", "Petty cash", 1250.5m);
        rows[3].Cells.Should().Equal(1100m, "Bank", -300m);
        rows[4].Cells.Should().Equal(new object?[] { null, "Total", 950.5m });
        rows.Where(r => r.Emphasised).Should().HaveCount(3);
    }

    [Fact]
    public void Excel_csv_and_word_carry_the_grid()
    {
        RenderModel model = Model();
        IReadOnlyList<IReadOnlyList<string?>> sheet = XlsxGrid.Read(LayoutGrid.ToXlsx(model));
        sheet[2].Should().Equal("0110", "Petty cash", "1250.5");
        sheet[4][1].Should().Be("Total");

        string csv = Encoding.UTF8.GetString(LayoutGrid.ToCsv(model)).TrimStart('﻿');
        csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Should().Equal("Account,Name,Balance", "Assets,,", "0110,Petty cash,1250.50", "1100,Bank,-300.00", ",Total,950.50");

        using ZipArchive docx = new(new MemoryStream(LayoutGrid.ToDocx(model)));
        docx.GetEntry("[Content_Types].xml").Should().NotBeNull();
        using Stream body = docx.GetEntry("word/document.xml")!.Open();
        XDocument document = XDocument.Load(body);
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        document.Descendants(w + "tr").Should().HaveCount(5);
        string text = string.Concat(document.Descendants(w + "t").Select(t => t.Value));
        text.Should().StartWith("Trial balance").And.Contain("Petty cash").And.Contain("1,250.50");
    }

    private static RenderModel Model()
    {
        RenderModel model = new() { ReportName = "Trial balance", Page = new PageSetup(), PageHeader = Band(BandType.PageHeader, ("Account", 0), ("Name", 60), ("Balance", 200)) };
        model.Body.Add(Band(BandType.GroupHeader, ("Assets", 1)));
        model.Body.Add(Band(BandType.Detail, ("0110", 0), ("Petty cash", 61), ("1,250.50", 202)));
        model.Body.Add(Band(BandType.Detail, ("1100", 0), ("Bank", 60), ("(300.00)", 200)));
        model.Body.Add(Band(BandType.GroupFooter, ("Total", 60), ("950.50", 200)));
        model.Body.Add(new RenderBand { Type = BandType.Detail, Height = 4, Cells = [new RenderCell { Kind = ElementKind.Line, X = 0, Width = 300 }] });
        return model;
    }

    private static RenderBand Band(BandType type, params (string Text, double X)[] cells)
        => new() { Type = type, Height = 16, Cells = [.. cells.Select(c => new RenderCell { Kind = ElementKind.Label, Text = c.Text, X = c.X, Width = 50, Height = 14 })] };
}
