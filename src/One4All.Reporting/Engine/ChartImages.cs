using System.Globalization;
using One4All.Reporting.Model;

namespace One4All.Reporting.Engine;

/// <summary>
/// Draws the chart types beyond horizontal bars (brief §13.1: column, stacked, line, area, pie, donut) as a PNG every renderer can place,
/// with the legend and category labels as ordinary text cells beside it. Values are summed by category (and series when a series field is
/// set); at most 30 categories and 8 series are drawn.
/// </summary>
public static class ChartImages
{
    /// <summary>The colours series (or slices) take in turn, after the chart's own colour.</summary>
    public static readonly IReadOnlyList<string> Palette = ["#2E7D32", "#1565C0", "#EF6C00", "#6A1B9A", "#C62828", "#00838F", "#9E9D24", "#5D4037"];

    private const int Scale = 2; // pixels a point

    /// <summary>The cells for a chart element: the drawn plot and its labels.</summary>
    /// <param name="element">The chart element.</param>
    /// <param name="scope">The rows it summarises.</param>
    /// <returns>The cells.</returns>
    public static IReadOnlyList<RenderCell> Cells(ReportElement element, IReadOnlyList<IReadOnlyDictionary<string, object?>> scope)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(scope);
        if (element.ChartType == ChartType.Bar)
        {
            return Charts.Bars(element, scope);
        }

        (List<string> categories, List<string> series, decimal[,] values) = Summarise(element, scope);
        if (categories.Count == 0)
        {
            return [];
        }

        bool round = element.ChartType is ChartType.Pie or ChartType.Donut;
        bool combo = element.ChartType == ChartType.ColumnLine && !string.IsNullOrWhiteSpace(element.ChartLineField);
        List<string> legend = combo ? [element.AggregateField ?? element.FieldName ?? "Value", element.ChartLineField!] : round ? categories : series.Count > 1 ? series : [];
        double legendWidth = legend.Count > 0 ? Math.Min(element.Width * 0.3, 110) : 0;
        double axisHeight = round ? 0 : Math.Min(14, element.Height * 0.15);
        double plotWidth = Math.Max(10, element.Width - legendWidth), plotHeight = Math.Max(10, element.Height - axisHeight);
        string[] colours = [.. Enumerable.Range(0, Math.Max(2, Math.Max(categories.Count, series.Count))).Select(i => i == 0 ? element.ChartColor : Palette[i % Palette.Count])];

        Canvas canvas = new((int)Math.Round(plotWidth * Scale), (int)Math.Round(plotHeight * Scale));
        switch (element.ChartType)
        {
            case ChartType.Pie:
            case ChartType.Donut:
                DrawPie(canvas, categories.Count, i => Total(values, i, series.Count), colours, element.ChartType == ChartType.Donut);
                break;
            case ChartType.Line:
            case ChartType.Area:
                DrawLines(canvas, values, categories.Count, series.Count, colours, element.ChartType == ChartType.Area);
                break;
            case ChartType.ColumnLine when combo:
                // The columns, then the line field summed by the same categories on its own scale over them.
                decimal[,] columns = new decimal[categories.Count, 1];
                for (int c = 0; c < categories.Count; c++)
                {
                    columns[c, 0] = Total(values, c, series.Count);
                }

                DrawColumns(canvas, columns, categories.Count, 1, colours, stacked: false);
                ReportElement line = new() { AggregateField = element.ChartLineField, ChartCategoryField = element.ChartCategoryField };
                (List<string> lineCategories, _, decimal[,] lineValues) = Summarise(line, scope);
                decimal[,] aligned = new decimal[categories.Count, 1];
                for (int c = 0; c < categories.Count; c++)
                {
                    int at = lineCategories.IndexOf(categories[c]);
                    aligned[c, 0] = at < 0 ? 0m : lineValues[at, 0];
                }

                colours[1] = Palette[2];
                DrawLines(canvas, aligned, categories.Count, 1, [Palette[2]], area: false);
                break;
            default:
                DrawColumns(canvas, values, categories.Count, series.Count, colours, element.ChartType == ChartType.StackedColumn);
                break;
        }

        List<RenderCell> cells =
        [
            new RenderCell { Kind = ElementKind.Image, X = element.X, Y = element.Y, Width = plotWidth, Height = plotHeight, ImageData = Barcodes.DataUri(canvas.ToPng()) },
        ];
        double fontSize = Math.Min(element.FontSize, 8);
        if (!round && categories.Count <= 15)
        {
            double slot = plotWidth / categories.Count;
            for (int c = 0; c < categories.Count; c++)
            {
                cells.Add(new RenderCell
                {
                    Kind = ElementKind.Label, Text = categories[c], X = element.X + (c * slot), Y = element.Y + plotHeight, Width = slot, Height = axisHeight, FontSize = fontSize,
                    HAlign = ReportHAlign.Center,
                });
            }
        }

        double row = legend.Count == 0 ? 0 : Math.Min(14, element.Height / legend.Count);
        for (int i = 0; i < legend.Count; i++)
        {
            double y = element.Y + (i * row);
            string label = round ? $"{legend[i]} {Total(values, i, series.Count).ToString(element.Format ?? "N0", CultureInfo.InvariantCulture)}" : legend[i];
            cells.Add(new RenderCell { Kind = ElementKind.Rectangle, X = element.X + plotWidth + 4, Y = y + (row * 0.25), Width = row * 0.5, Height = row * 0.5, Background = colours[i], BorderColor = colours[i] });
            cells.Add(new RenderCell { Kind = ElementKind.Label, Text = label, X = element.X + plotWidth + 6 + (row * 0.5), Y = y, Width = Math.Max(10, legendWidth - (row * 0.5) - 6), Height = row, FontSize = fontSize });
        }

        return cells;
    }

    /// <summary>Sums the value field by category (in order of first appearance) and series.</summary>
    /// <param name="element">The chart element.</param>
    /// <param name="scope">The rows.</param>
    /// <returns>The categories, the series (one unnamed series when no series field), and the sums [category, series].</returns>
    public static (List<string> Categories, List<string> Series, decimal[,] Values) Summarise(ReportElement element, IReadOnlyList<IReadOnlyDictionary<string, object?>> scope)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(scope);
        string valueField = element.AggregateField ?? element.FieldName ?? string.Empty;
        string Key(IReadOnlyDictionary<string, object?> row, string? field) => field is null ? string.Empty : Convert.ToString(row.GetValueOrDefault(field), CultureInfo.InvariantCulture) ?? string.Empty;
        List<string> categories = [.. scope.Select(r => Key(r, element.ChartCategoryField)).Distinct().Take(30)];
        List<string> series = string.IsNullOrWhiteSpace(element.ChartSeriesField) ? [string.Empty] : [.. scope.Select(r => Key(r, element.ChartSeriesField)).Distinct().Take(8)];
        decimal[,] values = new decimal[categories.Count, series.Count];
        foreach (IReadOnlyDictionary<string, object?> row in scope)
        {
            int c = categories.IndexOf(Key(row, element.ChartCategoryField)), s = series.IndexOf(Key(row, element.ChartSeriesField));
            if (c >= 0 && s >= 0 && decimal.TryParse(Convert.ToString(row.GetValueOrDefault(valueField), CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal v))
            {
                values[c, s] += v;
            }
        }

        return (categories, series, values);
    }

    private static decimal Total(decimal[,] values, int category, int seriesCount)
    {
        decimal total = 0m;
        for (int s = 0; s < seriesCount; s++)
        {
            total += values[category, s];
        }

        return total;
    }

    private static void DrawColumns(Canvas canvas, decimal[,] values, int categories, int seriesCount, string[] colours, bool stacked)
    {
        (decimal low, decimal high) = Range(values, categories, seriesCount, stacked);
        double slot = (double)canvas.Width / categories;
        for (int c = 0; c < categories; c++)
        {
            double left = (c * slot) + (slot * 0.15), width = slot * 0.7;
            decimal positive = 0m, negative = 0m;
            for (int s = 0; s < seriesCount; s++)
            {
                decimal v = values[c, s];
                (decimal from, decimal to) = stacked ? (v >= 0 ? (positive, positive += v) : (negative, negative += v)) : (0m, v);
                double x = stacked ? left : left + (width * s / seriesCount), w = stacked ? width : width / seriesCount;
                int top = canvas.YFor(Math.Max(from, to), low, high), bottom = canvas.YFor(Math.Min(from, to), low, high);
                canvas.Fill((int)x, top, (int)Math.Max(1, w - 1), Math.Max(1, bottom - top), colours[s]);
            }
        }

        canvas.Fill(0, canvas.YFor(0m, low, high), canvas.Width, 1, "#606060");
    }

    private static void DrawLines(Canvas canvas, decimal[,] values, int categories, int seriesCount, string[] colours, bool area)
    {
        (decimal low, decimal high) = Range(values, categories, seriesCount, stacked: false);
        double slot = (double)canvas.Width / categories;
        int baseline = canvas.YFor(Math.Max(low, Math.Min(0m, high)), low, high);
        for (int s = 0; s < seriesCount; s++)
        {
            (int X, int Y)[] points = [.. Enumerable.Range(0, categories).Select(c => ((int)((c * slot) + (slot / 2)), canvas.YFor(values[c, s], low, high)))];
            for (int i = 0; i + 1 < points.Length; i++)
            {
                if (area)
                {
                    for (int x = points[i].X; x <= points[i + 1].X; x++)
                    {
                        double t = (double)(x - points[i].X) / Math.Max(1, points[i + 1].X - points[i].X);
                        int y = (int)Math.Round(points[i].Y + (t * (points[i + 1].Y - points[i].Y)));
                        canvas.Fill(x, Math.Min(y, baseline), 1, Math.Abs(baseline - y) + 1, colours[s], blend: 0.45);
                    }
                }

                canvas.Line(points[i].X, points[i].Y, points[i + 1].X, points[i + 1].Y, colours[s], 2 * Scale / 2);
            }

            foreach ((int x, int y) in points)
            {
                canvas.Fill(x - 2, y - 2, 5, 5, colours[s]);
            }
        }

        canvas.Fill(0, baseline, canvas.Width, 1, "#606060");
    }

    private static void DrawPie(Canvas canvas, int slices, Func<int, decimal> value, string[] colours, bool donut)
    {
        decimal[] amounts = [.. Enumerable.Range(0, slices).Select(i => Math.Max(0m, value(i)))];
        decimal total = amounts.Sum();
        if (total <= 0m)
        {
            return;
        }

        double[] ends = new double[slices];
        double running = 0;
        for (int i = 0; i < slices; i++)
        {
            running += (double)(amounts[i] / total) * 360d;
            ends[i] = running;
        }

        double cx = canvas.Width / 2d, cy = canvas.Height / 2d, radius = (Math.Min(canvas.Width, canvas.Height) / 2d) - 2, hole = donut ? radius * 0.55 : 0;
        for (int y = 0; y < canvas.Height; y++)
        {
            for (int x = 0; x < canvas.Width; x++)
            {
                double dx = x - cx, dy = y - cy, distance = Math.Sqrt((dx * dx) + (dy * dy));
                if (distance > radius || distance < hole)
                {
                    continue;
                }

                // Clockwise from twelve o'clock.
                double angle = (Math.Atan2(dx, -dy) * 180d / Math.PI + 360d) % 360d;
                int slice = Array.FindIndex(ends, e => angle < e);
                canvas.Set(x, y, colours[slice < 0 ? slices - 1 : slice]);
            }
        }
    }

    private static (decimal Low, decimal High) Range(decimal[,] values, int categories, int seriesCount, bool stacked)
    {
        decimal low = 0m, high = 0m;
        for (int c = 0; c < categories; c++)
        {
            decimal positive = 0m, negative = 0m;
            for (int s = 0; s < seriesCount; s++)
            {
                decimal v = values[c, s];
                if (stacked)
                {
                    _ = v >= 0 ? positive += v : negative += v;
                }
                else
                {
                    (low, high) = (Math.Min(low, v), Math.Max(high, v));
                }
            }

            (low, high) = (Math.Min(low, negative), Math.Max(high, positive));
        }

        return high == low ? (low, low + 1m) : (low, high + ((high - low) * 0.05m));
    }

    // A white RGB drawing surface.
    private sealed class Canvas
    {
        private readonly byte[] _rgb;

        public Canvas(int width, int height)
        {
            Width = Math.Max(1, width);
            Height = Math.Max(1, height);
            _rgb = Enumerable.Repeat((byte)255, Width * Height * 3).ToArray();
        }

        public int Width { get; }

        public int Height { get; }

        public int YFor(decimal value, decimal low, decimal high) => (int)Math.Round((Height - 1) * (1 - (double)((value - low) / (high - low))));

        public void Set(int x, int y, string colour, double blend = 1)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height)
            {
                return;
            }

            (byte r, byte g, byte b) = Rgb(colour);
            int at = ((y * Width) + x) * 3;
            _rgb[at] = Mix(_rgb[at], r, blend);
            _rgb[at + 1] = Mix(_rgb[at + 1], g, blend);
            _rgb[at + 2] = Mix(_rgb[at + 2], b, blend);
        }

        public void Fill(int x, int y, int width, int height, string colour, double blend = 1)
        {
            for (int yy = y; yy < y + height; yy++)
            {
                for (int xx = x; xx < x + width; xx++)
                {
                    Set(xx, yy, colour, blend);
                }
            }
        }

        public void Line(int x0, int y0, int x1, int y1, string colour, int thickness)
        {
            int steps = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0));
            for (int i = 0; i <= steps; i++)
            {
                double t = steps == 0 ? 0 : (double)i / steps;
                int x = (int)Math.Round(x0 + (t * (x1 - x0))), y = (int)Math.Round(y0 + (t * (y1 - y0)));
                Fill(x - (thickness / 2), y - (thickness / 2), thickness, thickness, colour);
            }
        }

        public byte[] ToPng() => Barcodes.RgbPng(Width, Height, _rgb);

        private static byte Mix(byte under, byte over, double blend) => (byte)Math.Round((under * (1 - blend)) + (over * blend));

        private static (byte R, byte G, byte B) Rgb(string colour)
        {
            string hex = colour.TrimStart('#');
            return hex.Length == 6 && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int value)
                ? ((byte)(value >> 16), (byte)(value >> 8), (byte)value)
                : ((byte)46, (byte)125, (byte)50);
        }
    }
}
