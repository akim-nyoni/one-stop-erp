using FluentAssertions;
using One4All.Reporting.Engine;
using One4All.Reporting.Model;
using Xunit;

namespace One4All.UnitTests.Reporting;

/// <summary>
/// Covers the report house style: standard fonts and sizes take the theme's, titles and column-heading strips take its colours, lines its accent,
/// a footer line is added to every page and the logo is placed when the layout does not place it; a cell's own font and colour are left alone;
/// no theme changes nothing; the JSON round-trips and bad colours are named.
/// </summary>
public sealed class ReportHouseStyleTests
{
    private static ReportDefinition Listing()
    {
        ReportDefinition def = new() { Code = "T", Name = "Themed" };
        ReportBand header = new() { Type = BandType.ReportHeader, Height = 40 };
        header.Elements.Add(new ReportElement { Kind = ElementKind.Label, Text = "Customer list", FontSize = 16, Bold = true, X = 0, Y = 0, Width = 200, Height = 20 });
        header.Elements.Add(new ReportElement { Kind = ElementKind.Label, Text = "own font", FontFamily = "Courier New", Foreground = "#FF0000", X = 0, Y = 20, Width = 200, Height = 14 });
        ReportBand page = new() { Type = BandType.PageHeader, Height = 16 };
        page.Elements.Add(new ReportElement { Kind = ElementKind.Label, Text = "Name", Background = "#DDDDDD", X = 0, Y = 0, Width = 100, Height = 14 });
        page.Elements.Add(new ReportElement { Kind = ElementKind.Line, X = 0, Y = 15, Width = 500, Height = 1 });
        ReportBand detail = new() { Type = BandType.Detail, Height = 14 };
        detail.Elements.Add(new ReportElement { Kind = ElementKind.Field, FieldName = "Name", X = 0, Y = 0, Width = 100, Height = 14 });
        def.Bands.AddRange([header, page, detail]);
        return def;
    }

    private static ReportInput Input(ReportHouseStyle? theme, byte[]? logo = null) => new()
    {
        Definition = Listing(), Rows = [new Dictionary<string, object?> { ["Name"] = "Ada" }], HouseStyle = theme, CompanyLogo = logo,
    };

    [Fact]
    public void The_theme_restyles_standard_text_titles_headings_lines_footer_and_logo()
    {
        ReportHouseStyle theme = new()
        {
            FontFamily = "Calibri", BaseFontSize = 10, TitleColour = "#1B5E20", HeadingBackground = "#2E7D32", HeadingForeground = "#FFFFFF", AccentColour = "#2E7D32",
            FooterText = "Demo Council · Box 990", ShowLogo = true,
        };
        RenderModel model = new ReportEngine().Build(Input(theme, [1, 2, 3]));

        RenderCell title = model.ReportHeader!.Cells.Single(c => c.Text == "Customer list");
        (title.FontFamily, title.FontSize, title.Foreground).Should().Be(("Calibri", 16d, "#1B5E20"));
        RenderCell own = model.ReportHeader.Cells.Single(c => c.Text == "own font");
        (own.FontFamily, own.Foreground, own.FontSize).Should().Be(("Courier New", "#FF0000", 10d), "its own font and colour stay; the standard size takes the theme's");
        RenderCell heading = model.PageHeader!.Cells.Single(c => c.Text == "Name");
        (heading.Background, heading.Foreground).Should().Be(("#2E7D32", "#FFFFFF"));
        model.PageHeader.Cells.Single(c => c.Kind == ElementKind.Line).Foreground.Should().Be("#2E7D32");
        model.Body.Single().Cells.Single().FontFamily.Should().Be("Calibri");
        RenderCell footer = model.PageFooter!.Cells.Single();
        (footer.Text, footer.Italic, model.PageFooter.Height).Should().Be(("Demo Council · Box 990", true, 14d));
        model.ReportHeader.Cells.Should().ContainSingle(c => c.Kind == ElementKind.Image && c.ImageData!.StartsWith("data:"));
    }

    [Fact]
    public void Without_a_theme_or_with_an_empty_one_nothing_changes()
    {
        RenderModel plain = new ReportEngine().Build(Input(null));
        RenderModel empty = new ReportEngine().Build(Input(new ReportHouseStyle()));
        foreach (RenderModel model in new[] { plain, empty })
        {
            model.PageFooter.Should().BeNull();
            model.ReportHeader!.Cells.Should().HaveCount(2);
            model.ReportHeader.Cells.Single(c => c.Text == "Customer list").Foreground.Should().Be("#000000");
            model.PageHeader!.Cells.Single(c => c.Text == "Name").Background.Should().Be("#DDDDDD");
        }
    }

    [Fact]
    public void The_logo_is_not_placed_twice_when_the_layout_places_it()
    {
        ReportDefinition def = Listing();
        def.Bands.Single(b => b.Type == BandType.ReportHeader).Elements.Add(new ReportElement { Kind = ElementKind.Image, Expression = "CompanyLogo", X = 300, Y = 0, Width = 80, Height = 30 });
        RenderModel model = new ReportEngine().Build(new ReportInput { Definition = def, Rows = [], HouseStyle = new ReportHouseStyle { ShowLogo = true }, CompanyLogo = [9, 9] });
        model.ReportHeader!.Cells.Count(c => c.Kind == ElementKind.Image).Should().Be(1);
    }

    [Fact]
    public void Json_round_trips_and_bad_colours_are_named()
    {
        ReportHouseStyle theme = new() { FontFamily = "Arial", TitleColour = "#123456", FooterText = "x" };
        ReportHouseStyle back = ReportHouseStyle.Parse(theme.ToJson())!;
        (back.FontFamily, back.TitleColour, back.FooterText, back.BaseFontSize).Should().Be(("Arial", "#123456", "x", (double?)null));
        ReportHouseStyle.Parse(null).Should().BeNull();
        ReportHouseStyle.Parse("{}").Should().BeNull("an empty theme is no theme");
        ReportHouseStyle.Parse("not json").Should().BeNull();
        new ReportHouseStyle { TitleColour = "green", HeadingBackground = "#12345" }.BadColours().Should().Equal("Title colour", "Heading background");
        ReportHouseStyle.IsColour("#A1b2C3").Should().BeTrue();
    }
}
