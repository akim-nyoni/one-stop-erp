using System.Text;
using FluentAssertions;
using One4All.Reporting.Engine;
using One4All.Reporting.Model;
using Xunit;

namespace One4All.UnitTests.Reporting;

/// <summary>
/// Covers the page setup additions (brief §13.1): detail rows flowing across several columns (labels) a column's width apart, group bands
/// still spanning the page and restarting the columns, and a background picture behind every page in HTML and PDF.
/// </summary>
public sealed class PageColumnsAndBackgroundTests
{
    [Fact]
    public void Labels_flow_across_the_columns_then_down()
    {
        ReportDefinition def = Labels(columns: 3);
        RenderModel model = new ReportEngine().Build(new ReportInput { Definition = def, Rows = [.. Enumerable.Range(1, 7).Select(i => Row($"L{i}", "A"))] });

        model.Body.Should().HaveCount(3, "seven labels make rows of three, three and one");
        model.Body[0].Cells.Select(c => c.Text).Should().Equal("L1", "L2", "L3");
        double pitch = ((595 - 72 - (2 * 12)) / 3d) + 12;
        model.Body[0].Cells.Select(c => c.X).Should().Equal(5, 5 + pitch, 5 + (2 * pitch));
        model.Body[2].Cells.Single().Text.Should().Be("L7");
        model.Body.Should().OnlyContain(b => b.Height == 40);
    }

    [Fact]
    public void A_group_band_spans_the_page_and_restarts_the_columns()
    {
        ReportDefinition def = Labels(columns: 2);
        def.Groups.Add(new ReportGroup { Expression = "Area" });
        def.Bands.Add(new ReportBand { Type = BandType.GroupHeader, Height = 20, Elements = [new ReportElement { Kind = ElementKind.Field, Name = "area", FieldName = "Area", Width = 200, Height = 16 }] });

        RenderModel model = new ReportEngine().Build(new ReportInput { Definition = def, Rows = [Row("L1", "A"), Row("L2", "A"), Row("L3", "A"), Row("L4", "B")] });
        model.Body.Select(b => (b.Type, string.Join(",", b.Cells.Select(c => c.Text)))).Should().Equal(
            (BandType.GroupHeader, "A"), (BandType.Detail, "L1,L2"), (BandType.Detail, "L3"), (BandType.GroupHeader, "B"), (BandType.Detail, "L4"));

        def.Page.Columns = 1;
        new ReportEngine().Build(new ReportInput { Definition = def, Rows = [Row("L1", "A"), Row("L2", "A")] }).Body.Count(b => b.Type == BandType.Detail).Should().Be(2);
    }

    [Fact]
    public void A_background_picture_sits_behind_every_page()
    {
        string picture = Convert.ToBase64String(Barcodes.Png("QR", "letterhead")!);
        ReportDefinition def = Labels(columns: 1);
        def.Page.BackgroundImage = picture;
        RenderModel model = new ReportEngine().Build(new ReportInput { Definition = def, Rows = [Row("L1", "A")] });

        Encoding.UTF8.GetString(new HtmlReportRenderer().Render(model)).Should().Contain("url('data:image/png;base64," + picture + "')");
        byte[] withBackground = new PdfReportRenderer().Render(model);
        def.Page.BackgroundImage = null;
        byte[] without = new PdfReportRenderer().Render(new ReportEngine().Build(new ReportInput { Definition = def, Rows = [Row("L1", "A")] }));
        withBackground.Length.Should().BeGreaterThan(without.Length, "the picture is embedded");
    }

    private static ReportDefinition Labels(int columns) => new()
    {
        Code = "LBL", Name = "Labels", Page = new PageSetup { Columns = columns, ColumnGap = 12 },
        Bands = [new ReportBand { Type = BandType.Detail, Height = 40, Elements = [new ReportElement { Kind = ElementKind.Field, Name = "name", FieldName = "Name", X = 5, Width = 150, Height = 16 }] }],
    };

    private static Dictionary<string, object?> Row(string name, string area) => new() { ["Name"] = name, ["Area"] = area };
}
