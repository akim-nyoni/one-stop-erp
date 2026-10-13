using System.Globalization;
using One4All.Reporting.Model;

namespace One4All.Reporting.Engine;

/// <summary>
/// Table and cross-tab elements (brief §13.1 "table/grid element, cross-tab as an element"): the rows in scope laid out as a grid inside the
/// element's box, as labels and ruled cells every renderer already draws.
/// </summary>
public static class ReportGrids
{
    private const string HeaderFill = "#E8EEE8";

    /// <summary>A table element's cells.</summary>
    /// <param name="element">The element (columns in <see cref="ReportElement.Text"/>: "Field[:Header[:width%]]; …").</param>
    /// <param name="scope">The rows.</param>
    /// <returns>The cells.</returns>
    public static IReadOnlyList<RenderCell> Table(ReportElement element, IReadOnlyList<IReadOnlyDictionary<string, object?>> scope)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(scope);
        List<(string Field, string Header, double Share)> columns = Columns(element.Text, scope);
        if (columns.Count == 0)
        {
            return [];
        }

        double rowHeight = Math.Max(8, element.FontSize * 1.6);
        int fit = Math.Max(0, (int)Math.Floor(element.Height / rowHeight) - 1);
        bool more = scope.Count > fit;
        int shown = more ? Math.Max(0, fit - 1) : scope.Count;
        List<RenderCell> cells = [];
        double x = element.X;
        foreach ((string field, string header, double share) in columns)
        {
            double width = element.Width * share;
            cells.Add(Cell(element, header, x, element.Y, width, rowHeight, bold: true, fill: HeaderFill));
            for (int r = 0; r < shown; r++)
            {
                object? value = scope[r].GetValueOrDefault(field);
                cells.Add(Cell(element, Text(value, element.Format), x, element.Y + ((r + 1) * rowHeight), width, rowHeight, bold: false, fill: null,
                    align: value is decimal or double or int or long ? ReportHAlign.Right : ReportHAlign.Left));
            }

            x += width;
        }

        if (more)
        {
            cells.Add(Cell(element, $"… {scope.Count - shown} more row(s)", element.X, element.Y + ((shown + 1) * rowHeight), element.Width, rowHeight, bold: false, fill: null));
        }

        return cells;
    }

    /// <summary>A cross-tab element's cells: a row per category, a column per series, the value summed in each, with totals.</summary>
    /// <param name="element">The element.</param>
    /// <param name="scope">The rows.</param>
    /// <returns>The cells.</returns>
    public static IReadOnlyList<RenderCell> CrossTab(ReportElement element, IReadOnlyList<IReadOnlyDictionary<string, object?>> scope)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(scope);
        (List<string> rows, List<string> series, decimal[,] values) = ChartImages.Summarise(element, scope);
        if (rows.Count == 0)
        {
            return [];
        }

        double rowHeight = Math.Max(8, element.FontSize * 1.6);
        double labelWidth = element.Width * 0.3, columnWidth = (element.Width - labelWidth) / (series.Count + 1);
        string format = element.Format ?? "N2";
        List<RenderCell> cells = [Cell(element, string.Empty, element.X, element.Y, labelWidth, rowHeight, bold: true, fill: HeaderFill)];
        for (int s = 0; s < series.Count; s++)
        {
            cells.Add(Cell(element, series[s].Length == 0 ? "Value" : series[s], element.X + labelWidth + (s * columnWidth), element.Y, columnWidth, rowHeight, bold: true, fill: HeaderFill,
                align: ReportHAlign.Right));
        }

        cells.Add(Cell(element, "Total", element.X + labelWidth + (series.Count * columnWidth), element.Y, columnWidth, rowHeight, bold: true, fill: HeaderFill, align: ReportHAlign.Right));
        int fit = Math.Max(0, (int)Math.Floor(element.Height / rowHeight) - 2);
        decimal[] columnTotals = new decimal[series.Count];
        for (int r = 0; r < rows.Count; r++)
        {
            decimal rowTotal = 0m;
            for (int s = 0; s < series.Count; s++)
            {
                rowTotal += values[r, s];
                columnTotals[s] += values[r, s];
            }

            if (r >= fit)
            {
                continue;
            }

            double y = element.Y + ((r + 1) * rowHeight);
            cells.Add(Cell(element, rows[r], element.X, y, labelWidth, rowHeight, bold: false, fill: null));
            for (int s = 0; s < series.Count; s++)
            {
                cells.Add(Cell(element, values[r, s].ToString(format, CultureInfo.InvariantCulture), element.X + labelWidth + (s * columnWidth), y, columnWidth, rowHeight, bold: false, fill: null,
                    align: ReportHAlign.Right));
            }

            cells.Add(Cell(element, rowTotal.ToString(format, CultureInfo.InvariantCulture), element.X + labelWidth + (series.Count * columnWidth), y, columnWidth, rowHeight, bold: true, fill: null,
                align: ReportHAlign.Right));
        }

        double totalY = element.Y + ((Math.Min(rows.Count, fit) + 1) * rowHeight);
        cells.Add(Cell(element, "Total", element.X, totalY, labelWidth, rowHeight, bold: true, fill: HeaderFill));
        for (int s = 0; s < series.Count; s++)
        {
            cells.Add(Cell(element, columnTotals[s].ToString(format, CultureInfo.InvariantCulture), element.X + labelWidth + (s * columnWidth), totalY, columnWidth, rowHeight, bold: true, fill: HeaderFill,
                align: ReportHAlign.Right));
        }

        cells.Add(Cell(element, columnTotals.Sum().ToString(format, CultureInfo.InvariantCulture), element.X + labelWidth + (series.Count * columnWidth), totalY, columnWidth, rowHeight, bold: true,
            fill: HeaderFill, align: ReportHAlign.Right));
        return cells;
    }

    // The columns: as written, else every field of the first row in equal shares.
    private static List<(string Field, string Header, double Share)> Columns(string? text, IReadOnlyList<IReadOnlyDictionary<string, object?>> scope)
    {
        List<(string Field, string Header, double? Percent)> written = [];
        foreach (string part in (text ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] bits = part.Split(':', StringSplitOptions.TrimEntries);
            double? percent = bits.Length > 2 && double.TryParse(bits[2].TrimEnd('%'), NumberStyles.Number, CultureInfo.InvariantCulture, out double p) && p > 0 ? p : null;
            written.Add((bits[0], bits.Length > 1 && bits[1].Length > 0 ? bits[1] : bits[0], percent));
        }

        if (written.Count == 0 && scope.Count > 0)
        {
            written = [.. scope[0].Keys.Take(8).Select(k => (k, k, (double?)null))];
        }

        double given = written.Sum(c => c.Percent ?? 0), open = Math.Max(0, 100 - given);
        int unsized = written.Count(c => c.Percent is null);
        double scale = given > 100 ? 100 / given : 1;
        return [.. written.Select(c => (c.Field, c.Header, ((c.Percent * scale) ?? (unsized == 0 ? 0 : open / unsized)) / 100))];
    }

    private static RenderCell Cell(ReportElement element, string text, double x, double y, double width, double height, bool bold, string? fill, ReportHAlign align = ReportHAlign.Left) => new()
    {
        Kind = ElementKind.Label, Text = text, X = x, Y = y, Width = width, Height = height, FontFamily = element.FontFamily, FontSize = element.FontSize, Bold = bold,
        Background = fill, BorderThickness = 0.5, BorderColor = "#B0B8B0", HAlign = align, VAlign = ReportVAlign.Middle, Padding = 2, WordWrap = false,
    };

    private static string Text(object? value, string? format) => value switch
    {
        null => string.Empty,
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        decimal number => number.ToString(format ?? "N2", CultureInfo.InvariantCulture),
        double number => number.ToString(format ?? "N2", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
