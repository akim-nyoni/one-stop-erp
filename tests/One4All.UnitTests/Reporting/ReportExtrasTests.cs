using FluentAssertions;
using One4All.Reporting.Engine;
using One4All.Reporting.Model;
using Xunit;

namespace One4All.UnitTests.Reporting;

/// <summary>
/// Covers the richer report content: median, standard deviation, percent of total (row and group), rank and running total; tick boxes
/// and signature blocks; bar charts drawn as cells; and cross-tabs pivoting rows into generated columns.
/// </summary>
public sealed class ReportExtrasTests
{
    private static readonly List<IReadOnlyDictionary<string, object?>> Sales =
    [
        new Dictionary<string, object?> { ["Branch"] = "North", ["Month"] = "Jan", ["Amount"] = 100m, ["Paid"] = true },
        new Dictionary<string, object?> { ["Branch"] = "North", ["Month"] = "Feb", ["Amount"] = 300m, ["Paid"] = false },
        new Dictionary<string, object?> { ["Branch"] = "South", ["Month"] = "Jan", ["Amount"] = 200m, ["Paid"] = "yes" },
        new Dictionary<string, object?> { ["Branch"] = "South", ["Month"] = "Jan", ["Amount"] = 400m, ["Paid"] = 0 },
    ];

    private static ReportDefinition Definition(params ReportElement[] detail) => new()
    {
        Code = "X", Name = "X", Bands = [new ReportBand { Type = BandType.Detail, Height = 16, Elements = [.. detail] }],
    };

    private static ReportElement Aggregate(string name, AggregateFunction function) => new() { Kind = ElementKind.Aggregate, Name = name, AggregateField = "Amount", Aggregate = function, Format = "0.##" };

    [Fact]
    public void Statistics_are_right()
    {
        Statistics.Median([5m, 1m, 3m]).Should().Be(3m);
        Statistics.Median([4m, 1m, 3m, 2m]).Should().Be(2.5m);
        Statistics.Median([]).Should().Be(0m);
        Statistics.SampleStdDev([100m, 300m, 200m, 400m]).Should().Be(129.0994m);
        Statistics.SampleStdDev([7m]).Should().Be(0m);
    }

    [Fact]
    public void Row_level_aggregates_use_the_current_row()
    {
        RenderModel model = new ReportEngine().Build(new ReportInput
        {
            Definition = Definition(Aggregate("pct", AggregateFunction.PercentOfTotal), Aggregate("rank", AggregateFunction.RankOf), Aggregate("run", AggregateFunction.RunningTotal),
                Aggregate("median", AggregateFunction.Median), Aggregate("sd", AggregateFunction.StdDev),
                new ReportElement { Kind = ElementKind.Checkbox, Name = "paid", FieldName = "Paid" }, new ReportElement { Kind = ElementKind.Signature, Name = "sig", Text = "Manager" }),
            Rows = Sales,
        });

        model.Body.Select(b => b.Cells[0].Text).Should().Equal("10", "30", "20", "40");
        model.Body.Select(b => b.Cells[1].Text).Should().Equal("4", "2", "3", "1");
        model.Body.Select(b => b.Cells[2].Text).Should().Equal("100", "400", "600", "1000");
        model.Body[0].Cells[3].Text.Should().Be("250");
        model.Body[0].Cells[4].Text.Should().Be("129.1");
        model.Body.Select(b => b.Cells[5].Text).Should().Equal("☑", "☐", "☑", "☐");
        model.Body[0].Cells[6].Should().Match<RenderCell>(c => c.Kind == ElementKind.Label && c.Text.EndsWith("\nManager", StringComparison.Ordinal) && c.Text.StartsWith("____", StringComparison.Ordinal));
    }

    [Fact]
    public void A_group_footer_percent_is_the_group_against_the_report()
    {
        ReportDefinition definition = new()
        {
            Code = "G", Name = "G", Groups = [new ReportGroup { Expression = "Branch" }],
            Bands = [new ReportBand { Type = BandType.GroupFooter, Height = 16, Elements = [Aggregate("share", AggregateFunction.PercentOfTotal)] }],
        };
        new ReportEngine().Build(new ReportInput { Definition = definition, Rows = Sales }).Body.Select(b => b.Cells[0].Text).Should().Equal("40", "60");
    }

    [Fact]
    public void Charts_are_drawn_as_bars_and_labels()
    {
        ReportElement chart = new() { Kind = ElementKind.Chart, Name = "chart", AggregateField = "Amount", ChartCategoryField = "Branch", X = 10, Y = 0, Width = 300, Height = 40, Format = "N0" };
        IReadOnlyList<RenderCell> cells = Charts.Bars(chart, Sales);
        cells.Should().HaveCount(6);
        cells.Where(c => c.Kind == ElementKind.Label && c.HAlign == ReportHAlign.Left).Select(c => c.Text).Should().Equal("North", "South");
        RenderCell[] bars = [.. cells.Where(c => c.Kind == ElementKind.Rectangle)];
        bars[0].Width.Should().BeApproximately(bars[1].Width * 400 / 600, 0.01, "bars scale to the largest");
        cells.Where(c => c.HAlign == ReportHAlign.Right).Select(c => c.Text).Should().Equal("400", "600");

        RenderModel model = new ReportEngine().Build(new ReportInput
        {
            Definition = new ReportDefinition { Code = "C", Name = "C", Bands = [new ReportBand { Type = BandType.ReportFooter, Height = 60, Elements = [chart] }] }, Rows = Sales,
        });
        model.ReportFooter!.Cells.Should().HaveCount(6);
    }

    [Fact]
    public void Cross_tabs_pivot_rows_into_columns()
    {
        ReportDefinition definition = new()
        {
            Code = "CT", Name = "Sales by branch and month",
            CrossTab = new CrossTab { RowField = "Branch", ColumnField = "Month", ValueField = "Amount", Function = AggregateFunction.Sum, Format = "0" },
            Bands = [new ReportBand { Type = BandType.ReportHeader, Height = 20, Elements = [new ReportElement { Kind = ElementKind.Label, Name = "t", Text = "Pivot" }] }],
        };
        ReportInput pivoted = CrossTabs.Pivot(new ReportInput { Definition = definition, Rows = Sales }, definition.CrossTab!);
        pivoted.Rows.Should().HaveCount(2);
        pivoted.Rows[0].Should().Contain(new KeyValuePair<string, object?>("Branch", "North")).And.Contain(new KeyValuePair<string, object?>("Feb", 300m))
            .And.Contain(new KeyValuePair<string, object?>("Jan", 100m)).And.Contain(new KeyValuePair<string, object?>("Total", 400m));
        pivoted.Rows[1]["Feb"].Should().Be(0m);
        pivoted.Rows[1]["Jan"].Should().Be(600m);

        RenderModel model = new ReportEngine().Build(new ReportInput { Definition = definition, Rows = Sales });
        model.PageHeader!.Cells.Select(c => c.Text).Should().Equal("Branch", "Feb", "Jan", "Total");
        model.Body.Select(b => string.Join("|", b.Cells.Select(c => c.Text))).Should().Equal("North|300|100|400", "South|0|600|600");
        model.ReportHeader!.Cells[0].Text.Should().Be("Pivot", "other bands are kept");
        definition.CrossTab.Should().NotBeNull("the stored definition is not changed");
    }
}
