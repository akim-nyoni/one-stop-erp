using System.Text;
using FluentAssertions;
using One4All.Reporting.Design;
using One4All.Reporting.Engine;
using One4All.Reporting.Model;
using Xunit;

namespace One4All.UnitTests.Reporting;

/// <summary>
/// The designer gaps from the twelfth audit (brief §13.1): record sorting apart from grouping, element groups that move together, tooltips
/// and gradients in the rendered page, right anchoring in column layouts and bottom anchoring when a band grows, and pictures read from a
/// file path at render time.
/// </summary>
public sealed class DesignerExtrasTests
{
    private static readonly ReportEngine Engine = new();

    private static ReportDefinition Definition()
    {
        ReportDefinition def = new() { Code = "DX", Name = "Designer extras" };
        def.Bands.Add(new ReportBand
        {
            Type = BandType.Detail, Height = 20,
            Elements = [new ReportElement { Kind = ElementKind.Field, Name = "name", FieldName = "Name", Width = 100, Height = 16 }, new ReportElement { Kind = ElementKind.Field, Name = "amount", FieldName = "Amount", X = 100, Width = 80, Height = 16 }],
        });
        return def;
    }

    private static IReadOnlyDictionary<string, object?> Row(string name, decimal amount, string region = "N") => new Dictionary<string, object?> { ["Name"] = name, ["Amount"] = amount, ["Region"] = region };

    [Fact]
    public void Records_sort_apart_from_grouping_and_keep_that_order_inside_groups()
    {
        ReportDefinition def = Definition();
        def.Sorts.Add(new ReportSort { Expression = "Amount", Descending = true });
        RenderModel model = Engine.Build(new ReportInput { Definition = def, Rows = [Row("a", 5m), Row("b", 9m), Row("c", 1m)] });
        model.Body.Select(b => b.Cells[0].Text).Should().Equal("b", "a", "c");

        def.Groups.Add(new ReportGroup { Expression = "Region" });
        def.Bands.Add(new ReportBand { Type = BandType.GroupHeader, Height = 16, Elements = [new ReportElement { Kind = ElementKind.Field, Name = "g", FieldName = "Region", Width = 100, Height = 14 }] });
        model = Engine.Build(new ReportInput { Definition = def, Rows = [Row("a", 5m, "S"), Row("b", 9m, "N"), Row("c", 1m, "S"), Row("d", 7m, "N")] });
        model.Body.Where(b => b.Type == BandType.Detail).Select(b => b.Cells[0].Text).Should().Equal(new[] { "b", "d", "a", "c" }, "grouped by region, then by amount descending within each group");
    }

    [Fact]
    public void Tooltips_gradients_and_pictures_from_a_path_reach_the_rendered_page()
    {
        string file = Path.Combine(Path.GetTempPath(), $"one4all-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(file, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        try
        {
            ReportDefinition def = Definition();
            def.Bands[0].Elements[0].Tooltip = "The customer's name";
            def.Bands[0].Elements[0].Background = "#FFFFFF";
            def.Bands[0].Elements[0].BackgroundGradient = "#CCCCCC";
            def.Bands[0].Elements.Add(new ReportElement { Kind = ElementKind.Image, Name = "pic", ImageSource = file, X = 200, Width = 40, Height = 16 });
            def.Bands[0].Elements.Add(new ReportElement { Kind = ElementKind.Image, Name = "missing", ImageSource = file + ".nope", X = 250, Width = 40, Height = 16 });
            RenderModel model = Engine.Build(new ReportInput { Definition = def, Rows = [Row("a", 5m)] });
            RenderBand band = model.Body.Single();
            band.Cells.Single(c => c.Text == "a").Tooltip.Should().Be("The customer's name");
            band.Cells.Single(c => c.Kind == ElementKind.Image && c.ImageData is not null).ImageData.Should().StartWith("data:image/png;base64,iVBOR");
            string html = Encoding.UTF8.GetString(new HtmlReportRenderer().Render(model));
            html.Should().Contain("title=\"The customer's name\"").And.Contain("linear-gradient(to right,#FFFFFF,#CCCCCC)");
        }
        finally
        {
            File.Delete(file);
            ImageSources.Forget();
        }
    }

    [Fact]
    public void Right_anchored_cells_sit_at_the_right_of_their_column_and_bottom_anchored_cells_follow_a_growing_band()
    {
        ReportDefinition def = Definition();
        def.Page.Columns = 2;
        def.Page.ColumnGap = 10;
        def.Bands[0].Elements[1].AnchorRight = true;
        RenderModel model = Engine.Build(new ReportInput { Definition = def, Rows = [Row("a", 1m), Row("b", 2m)] });
        (double paperWidth, _) = ReportLayout.PaperSize(model.Page);
        double printable = paperWidth - model.Page.MarginLeft - model.Page.MarginRight;
        double pitch = ((printable - 10) / 2) + 10;
        RenderBand across = model.Body.Single(b => b.Type == BandType.Detail);
        across.Cells.Where(c => c.AnchorRight).Select(c => c.X).Should().Equal(pitch - 10 - 80, (pitch - 10 - 80) + pitch);

        RenderBand grown = new() { Type = BandType.Detail, Height = 40, Cells = [new RenderCell { Kind = ElementKind.Label, Text = "foot", Y = 4, Height = 12, AnchorBottom = true }] };
        grown.Height.Should().Be(40);
    }

    [Fact]
    public void Grouped_elements_move_together_until_ungrouped_and_sorts_round_trip_as_text()
    {
        ReportDesignSession s = new(new ReportDefinition { Code = "T", Name = "Test" }, grid: 1);
        string a = s.AddElement(BandType.Detail, ElementKind.Label, 10, 2, "A");
        string b = s.AddElement(BandType.Detail, ElementKind.Label, 60, 2, "B");
        string c = s.AddElement(BandType.Detail, ElementKind.Label, 120, 2, "C");
        s.Group([a]).Should().BeNull("a group needs two elements");
        string? group = s.Group([a, b]);
        group.Should().NotBeNull();
        s.Find(a).Element!.GroupName.Should().Be(group);
        s.Move([a], 5, 0);
        (s.Find(a).Element!.X, s.Find(b).Element!.X, s.Find(c).Element!.X).Should().Be((15d, 65d, 120d), "b moved with a; c did not");
        s.Ungroup([b]).Should().Be(2);
        s.Move([a], 5, 0);
        (s.Find(a).Element!.X, s.Find(b).Element!.X).Should().Be((20d, 65d));
        s.Undo();
        s.Find(a).Element!.X.Should().Be(15d);

        s.SetSorts("Amount desc; Name").Should().BeNull();
        s.Definition.Sorts.Select(x => (x.Expression, x.Descending)).Should().Equal(("Amount", true), ("Name", false));
        s.SortsText().Should().Be("Amount desc; Name");
        s.SetSorts("; desc").Should().NotBeNull();
        s.SetSorts(string.Empty).Should().BeNull();
        s.Definition.Sorts.Should().BeEmpty();

        s.SetProperty(a, nameof(ReportElement.Tooltip), "hint").Should().BeNull();
        s.SetProperty(a, nameof(ReportElement.AnchorBottom), "true").Should().BeNull();
        s.SetProperty(a, nameof(ReportElement.BackgroundGradient), "#EEEEEE").Should().BeNull();
        (s.GetProperty(a, nameof(ReportElement.Tooltip)), s.GetProperty(a, nameof(ReportElement.AnchorBottom))).Should().Be(("hint", "True"));
    }
}
