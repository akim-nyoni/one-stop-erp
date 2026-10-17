using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using One4All.Reporting.Model;

namespace One4All.Reporting.Engine;

/// <summary>One row of a report laid out as a grid.</summary>
/// <param name="Cells">The cells, one per column (null where the row has nothing).</param>
/// <param name="Band">The band the row came from.</param>
public sealed record GridRow(IReadOnlyList<object?> Cells, BandType Band)
{
    /// <summary>Gets a value indicating whether the row is a heading or total (anything but a detail row), shown bold.</summary>
    public bool Emphasised => Band != BandType.Detail;
}

/// <summary>
/// Lays a rendered report out as a grid - real rows and columns, not pictures (brief §13.3: "XLSX with real columns") - so a spreadsheet,
/// CSV or Word table shows what the report shows: headings, group headers, detail lines and totals in order. Columns come from where the
/// report's text sits across the page (positions within 3pt are one column); each line of text in a band is a row. Figures that read as
/// numbers become numbers; codes with leading zeros stay text.
/// </summary>
public static class LayoutGrid
{
    private const double Tolerance = 3;

    /// <summary>Builds the grid: the report header, the page header once (its column titles), the body, then the report footer.</summary>
    /// <param name="model">The rendered report.</param>
    /// <returns>The rows (every row has one cell per column).</returns>
    public static IReadOnlyList<GridRow> Build(RenderModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        List<RenderBand> bands = [.. new[] { model.ReportHeader, model.PageHeader }.OfType<RenderBand>(), .. model.Body, .. new[] { model.ReportFooter }.OfType<RenderBand>()];
        List<RenderCell> texts = [.. bands.SelectMany(b => b.Cells).Where(IsText)];
        List<double> columns = Anchors(texts.Select(c => c.X));
        List<GridRow> rows = [];
        foreach (RenderBand band in bands)
        {
            foreach (IGrouping<double, RenderCell> line in band.Cells.Where(IsText).GroupBy(c => Snap(c.Y, band.Cells.Where(IsText).Select(x => x.Y))).OrderBy(g => g.Key))
            {
                object?[] cells = new object?[columns.Count];
                foreach (RenderCell cell in line.OrderBy(c => c.X))
                {
                    int column = Nearest(columns, cell.X);
                    cells[column] = cells[column] is null ? Value(cell.Text) : $"{cells[column]} {cell.Text}";
                }

                if (cells.Any(c => c is not null))
                {
                    rows.Add(new GridRow(cells, band.Type));
                }
            }
        }

        return rows;
    }

    /// <summary>The report as an Excel workbook laid out like the report, headings and totals bold.</summary>
    /// <param name="model">The rendered report.</param>
    /// <returns>The .xlsx bytes.</returns>
    public static byte[] ToXlsx(RenderModel model)
    {
        IReadOnlyList<GridRow> rows = Build(model);
        return XlsxGrid.Write(model.ReportName, [.. rows.Select(r => r.Cells)], [.. rows.Select((r, i) => (r, i)).Where(x => x.r.Emphasised).Select(x => x.i)]);
    }

    /// <summary>The report as CSV laid out like the report.</summary>
    /// <param name="model">The rendered report.</param>
    /// <returns>The UTF-8 CSV bytes.</returns>
    public static byte[] ToCsv(RenderModel model)
    {
        StringBuilder csv = new();
        foreach (GridRow row in Build(model))
        {
            csv.AppendJoin(',', row.Cells.Select(c => c switch
            {
                null => string.Empty,
                decimal d => d.ToString(CultureInfo.InvariantCulture),
                _ => Quote(Convert.ToString(c, CultureInfo.InvariantCulture) ?? string.Empty),
            })).Append("\r\n");
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv.ToString())];
    }

    /// <summary>The report as a Word document: its name as the title and the grid as a table, headings and totals bold.</summary>
    /// <param name="model">The rendered report.</param>
    /// <returns>The .docx bytes.</returns>
    public static byte[] ToDocx(RenderModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        XElement Run(string text, bool bold) => new(w + "r", bold ? new XElement(w + "rPr", new XElement(w + "b")) : null,
            new XElement(w + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), Clean(text)));
        XElement Paragraph(string text, bool bold, bool right = false) => new(w + "p", right ? new XElement(w + "pPr", new XElement(w + "jc", new XAttribute(w + "val", "right"))) : null, Run(text, bold));

        IReadOnlyList<GridRow> rows = Build(model);
        XElement border(string side) => new(w + side, new XAttribute(w + "val", "single"), new XAttribute(w + "sz", 4), new XAttribute(w + "color", "BFBFBF"));
        XElement table = new(w + "tbl",
            new XElement(w + "tblPr", new XElement(w + "tblW", new XAttribute(w + "w", 5000), new XAttribute(w + "type", "pct")),
                new XElement(w + "tblBorders", border("top"), border("bottom"), border("insideH"))));
        foreach (GridRow row in rows)
        {
            XElement tr = new(w + "tr");
            foreach (object? cell in row.Cells)
            {
                tr.Add(new XElement(w + "tc", Paragraph(cell switch { null => string.Empty, decimal d => d.ToString("N2", CultureInfo.InvariantCulture), _ => cell.ToString() ?? string.Empty },
                    row.Emphasised, right: cell is decimal)));
            }

            table.Add(tr);
        }

        XElement document = new(w + "document", new XAttribute(XNamespace.Xmlns + "w", w.NamespaceName),
            new XElement(w + "body", Paragraph(model.ReportName, bold: true), table,
                new XElement(w + "sectPr", new XElement(w + "pgSz", new XAttribute(w + "w", Twips(ReportLayout.PaperSize(model.Page).Width)),
                    new XAttribute(w + "h", Twips(ReportLayout.PaperSize(model.Page).Height))))));

        using MemoryStream stream = new();
        using (ZipArchive zip = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Put(zip, "[Content_Types].xml", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>""");
            Put(zip, "_rels/.rels", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>""");
            Put(zip, "word/document.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" + document.ToString(SaveOptions.DisableFormatting));
        }

        return stream.ToArray();
    }

    private static bool IsText(RenderCell cell) => cell.Kind is not (ElementKind.Rectangle or ElementKind.Line or ElementKind.Image) && cell.ImageData is null && cell.Text.Length > 0;

    // Column anchors: the distinct left edges, merging those within the tolerance.
    private static List<double> Anchors(IEnumerable<double> positions)
    {
        List<double> anchors = [];
        foreach (double x in positions.Order())
        {
            if (anchors.Count == 0 || x - anchors[^1] > Tolerance)
            {
                anchors.Add(x);
            }
        }

        return anchors.Count == 0 ? [0] : anchors;
    }

    private static int Nearest(List<double> anchors, double x)
    {
        int best = 0;
        for (int i = 1; i < anchors.Count; i++)
        {
            if (Math.Abs(anchors[i] - x) < Math.Abs(anchors[best] - x))
            {
                best = i;
            }
        }

        return best;
    }

    // The line a top belongs to: the first top within the tolerance of it.
    private static double Snap(double y, IEnumerable<double> tops) => tops.Order().First(t => Math.Abs(t - y) <= Tolerance);

    // A figure as a number (thousands separators and brackets for negatives allowed); a code with a leading zero, or anything else, as text.
    private static object Value(string text)
    {
        string t = text.Trim();
        bool leadingZero = t.Length > 1 && t[0] == '0' && t[1] != '.';
        return !leadingZero && t.Any(char.IsAsciiDigit) && t.All(c => char.IsAsciiDigit(c) || c is ',' or '.' or '-' or '(' or ')' or ' ')
            && decimal.TryParse(t, NumberStyles.Number | NumberStyles.AllowParentheses, CultureInfo.InvariantCulture, out decimal number)
            ? number
            : text;
    }

    private static string Quote(string text) => text.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : text;

    private static string Clean(string text) => new([.. text.Where(c => c is '\t' or '\n' or '\r' || c >= ' ')]);

    private static int Twips(double points) => (int)Math.Round(points * 20);

    private static void Put(ZipArchive zip, string path, string content)
    {
        using Stream entry = zip.CreateEntry(path, CompressionLevel.Optimal).Open();
        byte[] bytes = Encoding.UTF8.GetBytes(content);
        entry.Write(bytes);
    }
}
