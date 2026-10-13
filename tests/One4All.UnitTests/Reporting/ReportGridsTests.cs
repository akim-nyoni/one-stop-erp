using FluentAssertions;
using One4All.Reporting.Engine;
using One4All.Reporting.Model;
using Xunit;

namespace One4All.UnitTests.Reporting;

/// <summary>Covers the table and cross-tab elements and the column-and-line combination chart.</summary>
public sealed class ReportGridsTests
{
    private static readonly IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows =
    [
        new Dictionary<string, object?> { ["Branch"] = "HQ", ["Month"] = "Jan", ["Sales"] = 100m, ["Margin"] = 20m },
        new Dictionary<string, object?> { ["Branch"] = "HQ", ["Month"] = "Feb", ["Sales"] = 150m, ["Margin"] = 30m },
        new Dictionary<string, object?> { ["Branch"] = "Mall", ["Month"] = "Jan", ["Sales"] = 80m, ["Margin"] = 10m },
    ];

    [Fact]
    public void A_table_lays_out_its_columns_and_counts_rows_that_do_not_fit()
    {
        ReportElement table = new() { Kind = ElementKind.Table, Width = 300, Height = 200, FontSize = 9, Text = "Branch:Branch:40%; Sales:Turnover" };
        IReadOnlyList<RenderCell> cells = ReportGrids.Table(table, Rows);
        cells.Where(c => c.Bold).Select(c => c.Text).Should().Equal("Branch", "Turnover");
        cells.Single(c => c.Text == "Branch").Width.Should().Be(120);
        cells.Single(c => c.Text == "Turnover").Width.Should().Be(180);
        cells.Should().Contain(c => c.Text == "150.00" && c.HAlign == ReportHAlign.Right);

        ReportElement small = new() { Kind = ElementKind.Table, Width = 300, Height = 45, FontSize = 9, Text = "Branch; Sales" };
        ReportGrids.Table(small, Rows).Should().Contain(c => c.Text == "… 2 more row(s)");
        ReportGrids.Table(new ReportElement { Kind = ElementKind.Table, Width = 300, Height = 200 }, Rows).Where(c => c.Bold).Should().HaveCount(4, "no columns given: every field");
    }

    [Fact]
    public void A_cross_tab_sums_by_row_and_column_with_totals()
    {
        ReportElement pivot = new() { Kind = ElementKind.CrossTab, Width = 400, Height = 200, ChartCategoryField = "Branch", ChartSeriesField = "Month", AggregateField = "Sales", Format = "N0" };
        List<string> texts = [.. ReportGrids.CrossTab(pivot, Rows).Select(c => c.Text)];
        texts.Should().ContainInOrder("Jan", "Feb", "Total", "HQ", "100", "150", "250", "Mall", "80", "0", "80", "Total", "180", "150", "330");
    }

    [Fact]
    public void A_combination_chart_draws_columns_with_a_line_and_a_two_entry_legend()
    {
        ReportElement chart = new()
        {
            Kind = ElementKind.Chart, ChartType = ChartType.ColumnLine, Width = 300, Height = 150, ChartCategoryField = "Month", AggregateField = "Sales", ChartLineField = "Margin",
        };
        IReadOnlyList<RenderCell> cells = ChartImages.Cells(chart, Rows);
        cells.Should().Contain(c => c.Kind == ElementKind.Image);
        cells.Where(c => c.Kind == ElementKind.Label).Select(c => c.Text).Should().Contain(["Sales", "Margin", "Jan", "Feb"]);
    }
}
