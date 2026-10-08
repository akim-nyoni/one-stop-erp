using FluentAssertions;
using One4All.Reporting.Design;
using One4All.Reporting.Engine;
using One4All.Reporting.Expressions;
using One4All.Reporting.Model;
using Xunit;

namespace One4All.UnitTests.Reporting;

/// <summary>
/// Covers the band and group behaviour the designer offers (brief §13.1): suppress-when on a band, groups sorted by value (numbers and
/// dates as such) and descending, a group header's aggregates over its own group, First / Last and grand totals, page breaks before and
/// after, group headers kept with their first row and repeated on the pages their group continues on, Legal paper, page numbers from
/// expressions, and the Case / FiscalPeriod / DateDiffIn functions.
/// </summary>
public sealed class BandFlowAndGroupingTests
{
    private static readonly ReportEngine Engine = new();

    [Fact]
    public void A_band_is_suppressed_when_its_expression_holds()
    {
        ReportDefinition def = Definition(detailSuppress: "Amount == 0");
        RenderModel model = Engine.Build(Input(def, Row("A", 5m), Row("B", 0m), Row("C", 7m)));
        model.Body.Select(b => b.Cells[0].Text).Should().Equal("A", "C");
    }

    [Fact]
    public void Groups_sort_by_value_and_direction_and_headers_total_their_own_group()
    {
        ReportDefinition def = Definition(group: "Region");
        def.Bands.Add(new ReportBand
        {
            Type = BandType.GroupHeader, Height = 20,
            Elements =
            [
                new ReportElement { Kind = ElementKind.Field, Name = "key", FieldName = "Region", Width = 100, Height = 16 },
                new ReportElement { Kind = ElementKind.Aggregate, Name = "sum", Aggregate = AggregateFunction.Sum, AggregateField = "Amount", X = 100, Width = 80, Height = 16 },
                new ReportElement { Kind = ElementKind.Aggregate, Name = "first", Aggregate = AggregateFunction.First, AggregateField = "Amount", X = 180, Width = 80, Height = 16 },
                new ReportElement { Kind = ElementKind.Aggregate, Name = "last", Aggregate = AggregateFunction.Last, AggregateField = "Amount", X = 260, Width = 80, Height = 16 },
                new ReportElement
                {
                    Kind = ElementKind.Aggregate, Name = "all", Aggregate = AggregateFunction.Sum, AggregateField = "Amount", AggregateScope = AggregateScope.GrandTotal, X = 340, Width = 80,
                    Height = 16,
                },
            ],
        });

        ReportInput input = Input(def, Row("x", 1m, 10), Row("y", 2m, 9), Row("z", 4m, 100), Row("w", 8m, 9));
        List<RenderBand> headers = Engine.Build(input).Body.Where(b => b.Type == BandType.GroupHeader).ToList();
        headers.Select(h => h.Cells[0].Text).Should().Equal(["9", "10", "100"], "numbers sort as numbers, not text");
        headers[0].Cells.Skip(1).Select(c => c.Text).Should().Equal("10", "2", "8", "15");

        def.Groups[0].Descending = true;
        Engine.Build(input).Body.Where(b => b.Type == BandType.GroupHeader).Select(h => h.Cells[0].Text).Should().Equal("100", "10", "9");
    }

    [Fact]
    public void Dates_group_in_date_order()
    {
        ReportDefinition def = Definition(group: "On");
        def.Bands.Add(new ReportBand { Type = BandType.GroupHeader, Height = 20, Elements = [new ReportElement { Kind = ElementKind.Field, Name = "on", FieldName = "On", Format = "yyyy-MM-dd", Width = 100, Height = 16 }] });
        ReportInput input = Input(def,
            new Dictionary<string, object?> { ["Name"] = "a", ["Amount"] = 1m, ["On"] = new DateOnly(2026, 10, 2) },
            new Dictionary<string, object?> { ["Name"] = "b", ["Amount"] = 1m, ["On"] = new DateOnly(2026, 9, 30) });
        Engine.Build(input).Body.Where(b => b.Type == BandType.GroupHeader).Select(h => h.Cells[0].Text).Should().Equal("2026-09-30", "2026-10-02");
    }

    [Fact]
    public void Page_breaks_keep_together_and_repeated_headers_shape_the_pages()
    {
        RenderModel model = new() { Page = new PageSetup { PaperSize = PaperSize.Custom, CustomWidth = 300, CustomHeight = 140, MarginTop = 10, MarginBottom = 10 } };
        RenderBand header = new() { Type = BandType.GroupHeader, Height = 20, GroupLevel = 0, RepeatOnNewPage = true, KeepTogether = true, Cells = [Cell("Region A")] };
        model.Body.Add(header);
        for (int i = 0; i < 6; i++)
        {
            model.Body.Add(new RenderBand { Type = BandType.Detail, Height = 20, Cells = [Cell($"row {i}")] });
        }

        model.Body.Add(new RenderBand { Type = BandType.GroupFooter, Height = 20, GroupLevel = 0, PageBreakAfter = true, Cells = [Cell("total A")] });
        model.Body.Add(new RenderBand { Type = BandType.GroupHeader, Height = 20, GroupLevel = 0, RepeatOnNewPage = true, KeepTogether = true, Cells = [Cell("Region B")] });
        model.Body.Add(new RenderBand { Type = BandType.Detail, Height = 20, Cells = [Cell("row B")] });

        IReadOnlyList<LayoutPage> pages = ReportLayout.Paginate(model);
        string[][] texts = [.. pages.Select(p => p.Bands.Select(b => b.Band.Cells[0].Text).ToArray())];
        texts[0].Should().Equal("Region A", "row 0", "row 1", "row 2", "row 3", "row 4");
        texts[1].Should().Equal(["Region A", "row 5", "total A"], "the header repeats where its group carries on");
        texts[2].Should().Equal(["Region B", "row B"], "the footer breaks the page after it");

        // A kept-together header that would sit alone at the foot of a page moves over with its first row.
        RenderModel tight = new() { Page = model.Page };
        for (int i = 0; i < 5; i++)
        {
            tight.Body.Add(new RenderBand { Type = BandType.Detail, Height = 20, Cells = [Cell($"r{i}")] });
        }

        tight.Body.Add(new RenderBand { Type = BandType.GroupHeader, Height = 20, KeepTogether = true, Cells = [Cell("H")] });
        tight.Body.Add(new RenderBand { Type = BandType.Detail, Height = 20, Cells = [Cell("d")] });
        ReportLayout.Paginate(tight)[1].Bands.Select(b => b.Band.Cells[0].Text).Should().Equal("H", "d");
    }

    [Fact]
    public void Legal_paper_and_page_numbers_from_expressions()
    {
        ReportLayout.PaperSize(new PageSetup { PaperSize = PaperSize.Legal }).Should().Be((612d, 1008d));
        ReportLayout.PaperSize(new PageSetup { PaperSize = PaperSize.Legal, Orientation = ReportOrientation.Landscape }).Should().Be((1008d, 612d));

        ReportDefinition def = Definition();
        def.Bands.Add(new ReportBand
        {
            Type = BandType.PageFooter, Height = 20,
            Elements = [new ReportElement { Kind = ElementKind.Expression, Name = "pages", Expression = "\"Page \" + PageNumber + \" of \" + TotalPages", Width = 200, Height = 16 }],
        });
        def.Page = new PageSetup { PaperSize = PaperSize.Custom, CustomWidth = 300, CustomHeight = 100, MarginTop = 0, MarginBottom = 0 };
        RenderModel model = Engine.Build(Input(def, Row("a", 1m), Row("b", 1m), Row("c", 1m), Row("d", 1m), Row("e", 1m)));
        IReadOnlyList<LayoutPage> pages = ReportLayout.Paginate(model);
        pages.Should().HaveCount(2);
        pages[1].Bands.Last().Band.Cells.Single().Text.Should().Be("Page 2 of 2");
    }

    [Fact]
    public void Case_fiscal_period_and_date_differences_in_units()
    {
        ReportExpressionEngine expressions = new();
        ReportExpressionContext ctx = new() { Fields = new Dictionary<string, object?> { ["Days"] = 45 } };
        expressions.Evaluate("Case2(Days > 90, \"90+\", Days > 30, \"31-90\", \"current\")", ctx).Should().Be("31-90");
        expressions.Evaluate("Case(Days > 90, \"old\", \"recent\")", ctx).Should().Be("recent");
        expressions.Evaluate("FiscalPeriod(new DateTime(2026, 7, 15), 7)", ctx).Should().Be(1);
        expressions.Evaluate("FiscalPeriod(new DateTime(2026, 6, 30), 7)", ctx).Should().Be(12);
        expressions.Evaluate("FiscalYear(new DateTime(2026, 7, 1), 7)", ctx).Should().Be(2027);
        expressions.Evaluate("DateDiffIn(\"m\", new DateTime(2026, 1, 31), new DateTime(2026, 3, 30))", ctx).Should().Be(1);
        expressions.Evaluate("DateDiffIn(\"y\", new DateTime(1990, 5, 20), new DateTime(2026, 5, 19))", ctx).Should().Be(35);
        expressions.Evaluate("DateDiffIn(\"w\", new DateTime(2026, 1, 1), new DateTime(2026, 1, 15))", ctx).Should().Be(2);
    }

    [Fact]
    public void The_designer_sets_band_flow_options_with_undo()
    {
        ReportDefinition def = Definition(group: "Region");
        def.Bands.Add(new ReportBand { Type = BandType.GroupHeader, GroupLevel = 0, Height = 20 });
        ReportDesignSession session = new(def);

        session.SetBandOptions(BandType.GroupHeader, 0, " Amount < 0 ", repeatOnNewPage: true, keepTogether: true, pageBreakBefore: false, pageBreakAfter: true).Should().BeTrue();
        ReportBand band = session.Definition.Bands.Single(b => b.Type == BandType.GroupHeader);
        (band.SuppressWhen, band.RepeatOnNewPage, band.KeepTogether, band.PageBreakAfter).Should().Be(("Amount < 0", true, true, true));
        session.SetBandOptions(BandType.GroupFooter, 0, null, false, false, false, false).Should().BeFalse("there is no such band");

        session.Undo();
        session.Definition.Bands.Single(b => b.Type == BandType.GroupHeader).RepeatOnNewPage.Should().BeFalse();
    }

    private static ReportDefinition Definition(string? detailSuppress = null, string? group = null)
    {
        ReportDefinition def = new()
        {
            Code = "FLOW", Name = "Flow",
            Bands =
            [
                new ReportBand
                {
                    Type = BandType.Detail, Height = 20, SuppressWhen = detailSuppress,
                    Elements = [new ReportElement { Kind = ElementKind.Field, Name = "name", FieldName = "Name", Width = 100, Height = 16 }],
                },
            ],
        };
        if (group is not null)
        {
            def.Groups.Add(new ReportGroup { Expression = group });
        }

        return def;
    }

    private static ReportInput Input(ReportDefinition def, params Dictionary<string, object?>[] rows) => new() { Definition = def, Rows = rows };

    private static Dictionary<string, object?> Row(string name, decimal amount, int region = 1)
        => new() { ["Name"] = name, ["Amount"] = amount, ["Region"] = region };

    private static RenderCell Cell(string text) => new() { Kind = ElementKind.Label, Text = text, Width = 100, Height = 16 };
}
