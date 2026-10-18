using FluentAssertions;
using One4All.Reporting.Design;
using One4All.Reporting.Engine;
using One4All.Reporting.Model;
using Xunit;

namespace One4All.UnitTests.Reporting;

/// <summary>
/// Thirteenth audit #20 (brief §13.1 designer model): a detail-child band under each row, alternating row shading, RowNumber and GroupRowNumber,
/// top-N groups with an "others" group, ellipses and round-cornered boxes, and text turned to any angle.
/// </summary>
public sealed class DesignerModelExtrasTests
{
    private static readonly ReportEngine Engine = new();

    [Fact]
    public void A_detail_child_band_follows_each_row_and_row_numbers_count_the_rows()
    {
        ReportDefinition def = Definition();
        def.Bands.Add(new ReportBand { Type = BandType.DetailChild, Height = 12, SuppressWhen = "Amount < 5",
            Elements = [new ReportElement { Kind = ElementKind.Expression, Name = "note", Expression = "\"note \" + RowNumber", Width = 100, Height = 12 }] });
        def.BandOfType(BandType.Detail)!.Elements.Add(new ReportElement { Kind = ElementKind.Expression, Name = "no", Expression = "RowNumber", X = 200, Width = 30, Height = 16 });

        RenderModel model = Engine.Build(Input(def, Row("A", "N", 5m), Row("B", "N", 1m), Row("C", "S", 7m)));
        model.Body.Select(b => (b.Type, b.Cells[0].Text)).Should().Equal(
            (BandType.Detail, "A"), (BandType.DetailChild, "note 1"), (BandType.Detail, "B"), (BandType.Detail, "C"), (BandType.DetailChild, "note 3"));
        model.Body.Where(b => b.Type == BandType.Detail).Select(b => b.Cells.Single(c => c.X == 200).Text).Should().Equal("1", "2", "3");
    }

    [Fact]
    public void Group_row_numbers_restart_in_each_group()
    {
        ReportDefinition def = Definition();
        def.Groups.Add(new ReportGroup { Expression = "Region" });
        def.BandOfType(BandType.Detail)!.Elements.Add(new ReportElement { Kind = ElementKind.Expression, Name = "no", Expression = "GroupRowNumber", X = 200, Width = 30, Height = 16 });
        RenderModel model = Engine.Build(Input(def, Row("A", "N", 1m), Row("B", "S", 2m), Row("C", "N", 3m), Row("D", "S", 4m), Row("E", "S", 5m)));
        model.Body.Where(b => b.Type == BandType.Detail).Select(b => b.Cells[0].Text + b.Cells.Single(c => c.X == 200).Text).Should().Equal("A1", "C2", "B1", "D2", "E3");
    }

    [Fact]
    public void Every_second_detail_row_is_shaded_across_the_page()
    {
        ReportDefinition def = Definition();
        def.BandOfType(BandType.Detail)!.AlternateBackground = "#EEF3FA";
        List<RenderBand> rows = [.. Engine.Build(Input(def, Row("A", "N", 1m), Row("B", "N", 2m), Row("C", "N", 3m))).Body];
        rows.Select(b => b.Cells.Any(c => c.Background == "#EEF3FA")).Should().Equal(false, true, false);
        RenderCell shade = rows[1].Cells[0];
        shade.Width.Should().BeApproximately(595 - def.Page.MarginLeft - def.Page.MarginRight, 0.01);
        rows[1].Cells[1].Text.Should().Be("B", "the shading sits under the row's own cells");
    }

    [Fact]
    public void Top_groups_keep_the_largest_and_gather_the_rest_into_others()
    {
        ReportDefinition def = Definition();
        def.Groups.Add(new ReportGroup { Expression = "Region", TopCount = 2, TopBy = "Amount", OthersLabel = "Others" });
        def.Bands.Add(new ReportBand { Type = BandType.GroupHeader, Height = 16, Elements = [new ReportElement { Kind = ElementKind.Field, Name = "r", FieldName = "Region", Width = 80, Height = 16 }] });
        def.Bands.Add(new ReportBand { Type = BandType.GroupFooter, Height = 16,
            Elements = [new ReportElement { Kind = ElementKind.Aggregate, Name = "t", Aggregate = AggregateFunction.Sum, AggregateField = "Amount", Width = 80, Height = 16 }] });
        ReportInput input = Input(def, Row("a", "North", 50m), Row("b", "South", 5m), Row("c", "East", 30m), Row("d", "West", 2m), Row("e", "North", 1m));

        List<RenderBand> body = [.. Engine.Build(input).Body];
        body.Where(b => b.Type == BandType.GroupHeader).Select(b => b.Cells[0].Text).Should().Equal("East", "North", "Others");
        body.Where(b => b.Type == BandType.GroupFooter).Select(b => b.Cells[0].Text).Should().Equal("30", "51", "7");

        def.Groups[0].Descending = true;
        Engine.Build(input).Body.Where(b => b.Type == BandType.GroupHeader).Select(b => b.Cells[0].Text).Should().Equal(["North", "East", "Others"], "others stays last");

        def.Groups[0].OthersLabel = null;
        Engine.Build(input).Body.Where(b => b.Type == BandType.Detail).Select(b => b.Cells[0].Text).Should().Equal(["a", "e", "c"], "the rest are left out");
    }

    [Fact]
    public void Ellipses_round_boxes_and_any_angle_reach_the_renderers()
    {
        ReportDefinition def = Definition();
        def.BandOfType(BandType.Detail)!.Elements.AddRange(
        [
            new ReportElement { Kind = ElementKind.Ellipse, Name = "e", X = 100, Width = 40, Height = 20, Background = "#FFCC00", BorderThickness = 1 },
            new ReportElement { Kind = ElementKind.Rectangle, Name = "b", X = 150, Width = 40, Height = 20, CornerRadius = 6 },
            new ReportElement { Kind = ElementKind.Label, Name = "t", Text = "Draft", X = 200, Width = 60, Height = 20, Rotation = -45 },
        ]);
        RenderModel model = Engine.Build(Input(def, Row("A", "N", 1m)));
        List<RenderCell> cells = model.Body[0].Cells;
        cells.Single(c => c.Kind == ElementKind.Ellipse).Borders().Should().Be((1d, 1d, 1d, 1d));
        cells.Single(c => c.Kind == ElementKind.Rectangle).CornerRadius.Should().Be(6);
        cells.Single(c => c.Text == "Draft").Rotation.Should().Be(315);

        string html = System.Text.Encoding.UTF8.GetString(new HtmlReportRenderer().Render(model));
        html.Should().Contain("border-radius:50%").And.Contain("border-radius:6px").And.Contain("rotate(315deg)");
        byte[] pdf = new PdfReportRenderer().Render(model);
        pdf.Length.Should().BeGreaterThan(500);
        System.Text.Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");

        DesignerCatalogue.Tools.Should().Contain(t => t.Kind == ElementKind.Ellipse);
        DesignerCatalogue.Category("CornerRadius").Should().Be("Layout");
    }

    private static ReportDefinition Definition() => new()
    {
        Code = "T20", Name = "Designer extras",
        Bands = [new ReportBand { Type = BandType.Detail, Height = 16, Elements = [new ReportElement { Kind = ElementKind.Field, Name = "name", FieldName = "Name", Width = 100, Height = 16 }] }],
    };

    private static Dictionary<string, object?> Row(string name, string region, decimal amount) => new() { ["Name"] = name, ["Region"] = region, ["Amount"] = amount };

    private static ReportInput Input(ReportDefinition def, params Dictionary<string, object?>[] rows) => new() { Definition = def, Rows = rows };
}
