using System.Text;
using FluentAssertions;
using One4All.Reporting.Engine;
using One4All.Reporting.Model;
using Xunit;

namespace One4All.UnitTests.Reporting;

/// <summary>
/// Covers the element text options (brief §13.1): text for a missing value, vertical alignment, padding, per-side borders, word-wrap,
/// shrink-to-fit and rotation - carried from the definition to the rendered cell, kept by layout copies, and drawn by HTML and PDF.
/// </summary>
public sealed class ElementTextOptionTests
{
    [Fact]
    public void Options_reach_the_rendered_cell()
    {
        ReportElement field = new()
        {
            Kind = ElementKind.Field, Name = "ref", FieldName = "Reference", NullText = "(none)", VAlign = ReportVAlign.Bottom, Padding = 3, BorderBottom = 1.5, WordWrap = false,
            Rotation = -90, Width = 120, Height = 20,
        };
        RenderCell cell = Build(field, new Dictionary<string, object?> { ["Reference"] = null }).Single();
        cell.Text.Should().Be("(none)");
        (cell.VAlign, cell.Padding, cell.WordWrap, cell.Rotation).Should().Be((ReportVAlign.Bottom, 3d, false, 270));
        cell.Borders().Should().Be((0d, 0d, 0d, 1.5d), "only the bottom border is drawn");

        Build(field, new Dictionary<string, object?> { ["Reference"] = "INV-1" }).Single().Text.Should().Be("INV-1");
        (Layouts.Moved(cell, 5, 6) with { }).Should().BeEquivalentTo(cell with { X = 5, Y = 6 }, "a moved cell keeps every option");

        RenderCell box = new() { Kind = ElementKind.Rectangle, BorderLeft = 2 };
        box.Borders().Should().Be((2d, 0.5d, 0.5d, 0.5d), "a rectangle keeps a hairline where a side is not set");
    }

    [Fact]
    public void Shrink_to_fit_lowers_the_font_until_the_text_fits()
    {
        ReportElement name = new() { Kind = ElementKind.Field, Name = "name", FieldName = "Name", FontSize = 12, AutoShrink = true, WordWrap = false, Width = 60, Height = 16, Padding = 0 };
        double shrunk = Build(name, new Dictionary<string, object?> { ["Name"] = "Long customer name" }).Single().FontSize;
        shrunk.Should().BeLessThan(12).And.BeGreaterThanOrEqualTo(5);
        (18 * shrunk * 0.5).Should().BeLessThanOrEqualTo(60, "the text now fits on one line");

        Build(name, new Dictionary<string, object?> { ["Name"] = "Short" }).Single().FontSize.Should().Be(12, "text that fits keeps its size");
        Layouts.FittedFontSize(new string('x', 500), 10, 20, 10, wrap: true).Should().Be(5, "never below 5pt");
    }

    [Fact]
    public void Html_and_pdf_draw_the_options()
    {
        RenderModel model = new() { ReportName = "Options", Page = new PageSetup() };
        model.Body.Add(new RenderBand
        {
            Type = BandType.Detail, Height = 40,
            Cells =
            [
                new RenderCell { Kind = ElementKind.Label, Text = "Turned", Width = 30, Height = 40, Rotation = 90, VAlign = ReportVAlign.Middle, BorderLeft = 1, WordWrap = false, Padding = 2 },
                new RenderCell { Kind = ElementKind.Label, Text = "Plain", X = 40, Width = 60, Height = 20 },
            ],
        });

        string html = Encoding.UTF8.GetString(new HtmlReportRenderer().Render(model));
        html.Should().Contain("transform:rotate(90deg)").And.Contain("justify-content:center").And.Contain("border-left:1px solid").And.Contain("white-space:nowrap")
            .And.Contain("padding:2pt").And.NotContain("border-top");

        new PdfReportRenderer().Render(model).Take(4).Should().Equal(Encoding.ASCII.GetBytes("%PDF"));
    }

    private static List<RenderCell> Build(ReportElement element, Dictionary<string, object?> row)
    {
        ReportDefinition definition = new() { Code = "OPT", Name = "Options", Bands = [new ReportBand { Type = BandType.Detail, Height = 30, Elements = [element] }] };
        return new ReportEngine().Build(new ReportInput { Definition = definition, Rows = [row] }).Body.Single().Cells;
    }
}
