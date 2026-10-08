using System.IO.Compression;
using FluentAssertions;
using One4All.Reporting.Engine;
using One4All.Reporting.Model;
using Xunit;

namespace One4All.UnitTests.Reporting;

/// <summary>
/// Covers the added barcode symbologies (Code 39 with its start/stop asterisks; EAN-13 with its check digit and parity) and the drawn chart
/// types (column, stacked column, line, area, pie, donut) — checked pixel by pixel in the PNG they produce, with legends and category labels.
/// </summary>
public sealed class BarcodeAndChartTypeTests
{
    [Fact]
    public void Code39_frames_the_value_with_asterisks()
    {
        IReadOnlyList<bool> modules = Barcodes.Code39Modules("ab-1")!;
        modules.Count.Should().Be((6 * 15) + 5, "six characters (with the asterisks) of 15 modules and five gaps");
        modules.Take(15).Select(m => m ? '1' : '0').Should().Equal("100010111011101".ToCharArray(), "the start asterisk: n-bar W-space n-bar n-space W-bar n-space W-bar n-space n-bar");
        Barcodes.Code39Modules("a*b").Should().BeNull();
        Barcodes.Code39Modules("é").Should().BeNull();
        Barcodes.Png("Code 39", "INV-7")!.Take(4).Should().Equal([0x89, 0x50, 0x4E, 0x47]);
    }

    [Fact]
    public void Ean13_checks_its_digit_and_encodes_the_parity()
    {
        Barcodes.Ean13CheckDigit("400638133393").Should().Be(1);
        string modules = Barcodes.Ean13Modules("400638133393")!;
        modules.Should().HaveLength(95);
        modules.Should().StartWith("101").And.EndWith("101");
        modules.Substring(45, 5).Should().Be("01010");
        modules.Substring(3, 7).Should().Be("0001101", "the first digit 4 sets L parity for the second digit, 0");
        modules.Substring(10, 7).Should().Be("0100111", "and G parity for the third digit, 0");
        Barcodes.Ean13Modules("4006381333931").Should().Be(modules);
        Barcodes.Ean13Modules("4006381333932").Should().BeNull("a wrong check digit");
        Barcodes.Ean13Modules("40063813339X").Should().BeNull();
        Barcodes.Png("EAN13", "4006381333931").Should().NotBeNull();
    }

    [Fact]
    public void Columns_and_stacks_draw_each_series_in_its_colour()
    {
        Rows rows = [Row("Jan", "Cash", 10m), Row("Jan", "Card", 30m), Row("Feb", "Cash", 20m)];
        ReportElement column = Chart(ChartType.StackedColumn, series: "Kind");
        IReadOnlyList<RenderCell> cells = ChartImages.Cells(column, rows);
        Image image = Image.Of(cells[0]);
        image.At(image.Width / 4, image.Height - 4).Should().Be((0x2E, 0x7D, 0x32), "Jan's cash sits at the bottom of its stack in the chart's colour");
        image.At(image.Width / 4, image.Height / 4).Should().Be((0x15, 0x65, 0xC0), "Jan's card is stacked above it in the next colour");
        image.At(image.Width * 3 / 4, image.Height / 4).Should().Be((255, 255, 255), "Feb (20) does not reach as high as Jan (40)");
        cells.Where(c => c.Kind == ElementKind.Label).Select(c => c.Text).Should().Contain(["Jan", "Feb", "Cash", "Card"]);
    }

    [Fact]
    public void Pies_and_donuts_slice_by_category()
    {
        Rows rows = [Row("A", "", 75m), Row("B", "", 25m)];
        Image pie = Image.Of(ChartImages.Cells(Chart(ChartType.Pie), rows)[0]);
        pie.At((pie.Width / 2) + (pie.Height / 4), pie.Height / 2).Should().Be((0x2E, 0x7D, 0x32), "A's 75% runs clockwise from twelve past three o'clock");
        pie.At((pie.Width / 2) - (pie.Height / 4), pie.Height / 2 - (pie.Height / 8)).Should().Be((0x15, 0x65, 0xC0), "B's quarter lies between nine and twelve");

        Image donut = Image.Of(ChartImages.Cells(Chart(ChartType.Donut), rows)[0]);
        donut.At(donut.Width / 2, donut.Height / 2).Should().Be((255, 255, 255), "a donut's middle is empty");
        ChartImages.Cells(Chart(ChartType.Pie), rows).Where(c => c.Kind == ElementKind.Label).Select(c => c.Text).Should().Contain(["A 75", "B 25"]);
    }

    [Fact]
    public void Lines_and_areas_rise_with_the_values()
    {
        Rows rows = [Row("Q1", "", 10m), Row("Q2", "", 100m)];
        Image line = Image.Of(ChartImages.Cells(Chart(ChartType.Line), rows)[0]);
        line.At(line.Width / 4, line.Height / 2).Should().Be((255, 255, 255), "a line is not filled beneath");
        Image area = Image.Of(ChartImages.Cells(Chart(ChartType.Area), rows)[0]);
        area.At(area.Width * 3 / 4, area.Height - 6).Should().NotBe((255, 255, 255), "an area is shaded down to the axis");
        ChartImages.Cells(Chart(ChartType.Line), []).Should().BeEmpty();
        ChartImages.Cells(Chart(ChartType.Bar), rows).Should().Contain(c => c.Kind == ElementKind.Rectangle, "bars are still drawn as cells");
    }

    private static ReportElement Chart(ChartType type, string? series = null) => new()
    {
        Kind = ElementKind.Chart, Name = "chart", ChartType = type, ChartCategoryField = "Period", ChartSeriesField = series, AggregateField = "Amount", Width = 300, Height = 160,
    };

    private static Dictionary<string, object?> Row(string period, string kind, decimal amount) => new() { ["Period"] = period, ["Kind"] = kind, ["Amount"] = amount };

    private sealed class Rows : List<IReadOnlyDictionary<string, object?>>
    {
        public void Add(Dictionary<string, object?> row) => base.Add(row);
    }

    // Reads back the RGB PNGs the charts draw (one IDAT, filter 0 on every row).
    private sealed record Image(int Width, int Height, byte[] Pixels)
    {
        public static Image Of(RenderCell cell)
        {
            byte[] png = Convert.FromBase64String(cell.ImageData![(cell.ImageData.IndexOf(',', StringComparison.Ordinal) + 1)..]);
            int width = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19], height = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
            png[25].Should().Be(2, "an RGB image");
            int length = (png[33] << 24) | (png[34] << 16) | (png[35] << 8) | png[36];
            using ZLibStream zlib = new(new MemoryStream(png, 41, length), CompressionMode.Decompress);
            using MemoryStream raw = new();
            zlib.CopyTo(raw);
            return new Image(width, height, raw.ToArray());
        }

        public (int R, int G, int B) At(int x, int y)
        {
            int at = (y * ((Width * 3) + 1)) + 1 + (x * 3);
            return (Pixels[at], Pixels[at + 1], Pixels[at + 2]);
        }
    }
}
