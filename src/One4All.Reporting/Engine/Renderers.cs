using System.Globalization;
using System.Text;
using One4All.Reporting.Model;

namespace One4All.Reporting.Engine;

/// <summary>Renders a <see cref="RenderModel"/> to a document byte stream (brief §13.3).</summary>
public interface IReportRenderer
{
    /// <summary>The MIME content type produced.</summary>
    string ContentType { get; }

    /// <summary>The conventional file extension (without the dot).</summary>
    string FileExtension { get; }

    /// <summary>Renders the model.</summary>
    /// <param name="model">The render model.</param>
    /// <returns>The document bytes.</returns>
    byte[] Render(RenderModel model);
}

/// <summary>Renders a report to self-contained HTML with absolutely-positioned bands and cells.</summary>
public sealed class HtmlReportRenderer : IReportRenderer
{
    /// <inheritdoc />
    public string ContentType => "text/html";

    /// <inheritdoc />
    public string FileExtension => "html";

    /// <inheritdoc />
    public byte[] Render(RenderModel model) => Encoding.UTF8.GetBytes(RenderToString(model));

    /// <summary>Renders to an HTML string.</summary>
    /// <param name="model">The render model.</param>
    /// <returns>The HTML.</returns>
    public string RenderToString(RenderModel model)
    {
        StringBuilder sb = new();
        sb.Append(model.RightToLeft ? "<!doctype html><html dir=\"rtl\"><head><meta charset=\"utf-8\"><title>" : "<!doctype html><html><head><meta charset=\"utf-8\"><title>")
          .Append(Escape(model.ReportName))
          .Append("</title></head><body style=\"margin:0;background:#eee;font-family:Segoe UI,Arial,sans-serif\">");

        double pageWidth = PageWidthPoints(model.Page);
        string background = string.IsNullOrWhiteSpace(model.Page.BackgroundImage) ? "white"
            : "white url('" + (model.Page.BackgroundImage.StartsWith("data:", StringComparison.Ordinal) ? model.Page.BackgroundImage : "data:image/png;base64," + model.Page.BackgroundImage)
                + "') top left/100% auto no-repeat";
        sb.Append("<div style=\"width:").Append(Px(pageWidth)).Append("px;margin:12px auto;background:").Append(background).Append(";padding:")
          .Append(Px(model.Page.MarginTop)).Append("px ").Append(Px(model.Page.MarginRight)).Append("px ")
          .Append(Px(model.Page.MarginBottom)).Append("px ").Append(Px(model.Page.MarginLeft)).Append("px\">");

        RenderBandHtml(sb, model.ReportHeader);
        RenderBandHtml(sb, model.PageHeader);
        foreach (RenderBand band in model.Body)
        {
            RenderBandHtml(sb, band);
        }

        RenderBandHtml(sb, model.ReportFooter);
        RenderBandHtml(sb, model.PageFooter);

        sb.Append("</div></body></html>");
        return sb.ToString();
    }

    private static void RenderBandHtml(StringBuilder sb, RenderBand? band)
    {
        if (band is null)
        {
            return;
        }

        sb.Append("<div data-band=\"").Append(band.Type).Append("\" style=\"position:relative;height:")
          .Append(Px(band.Height)).Append("px\">");

        foreach (RenderCell cell in band.Cells)
        {
            string align = cell.HAlign switch { ReportHAlign.Right => "right", ReportHAlign.Center => "center", _ => "left" };
            sb.Append("<div");
            if (cell.Tooltip is string tooltip)
            {
                sb.Append(" title=\"").Append(Escape(tooltip).Replace("\"", "&quot;", StringComparison.Ordinal)).Append('"');
            }

            sb.Append(" style=\"position:absolute;left:").Append(Px(cell.X)).Append("px;top:").Append(Px(cell.Y))
              .Append("px;width:").Append(Px(cell.Width)).Append("px;height:").Append(Px(cell.Height))
              .Append("px;font-size:").Append(Px(cell.FontSize)).Append("pt;text-align:").Append(align)
              .Append(";color:").Append(cell.Foreground);

            if (cell.Background is not null && cell.BackgroundGradient is not null)
            {
                sb.Append(";background:linear-gradient(").Append(cell.GradientVertical ? "to bottom" : "to right").Append(',').Append(cell.Background).Append(',').Append(cell.BackgroundGradient).Append(')');
            }
            else if (cell.Background is not null)
            {
                sb.Append(";background:").Append(cell.Background);
            }

            if (cell.Bold)
            {
                sb.Append(";font-weight:bold");
            }

            if (cell.Italic)
            {
                sb.Append(";font-style:italic");
            }

            if (cell.Underline)
            {
                sb.Append(";text-decoration:underline");
            }

            (double left, double top, double right, double bottom) = cell.Borders();
            foreach ((string side, double width) in new[] { ("left", left), ("top", top), ("right", right), ("bottom", bottom) }.Where(b => b.Item2 > 0))
            {
                sb.Append(";border-").Append(side).Append(':').Append(Px(Math.Max(width, 1))).Append("px solid ").Append(cell.BorderColor);
            }

            if (cell.Kind == ElementKind.Ellipse)
            {
                sb.Append(";border-radius:50%");
            }
            else if (cell.CornerRadius > 0)
            {
                sb.Append(";border-radius:").Append(Px(cell.CornerRadius)).Append("px");
            }

            sb.Append(";box-sizing:border-box;padding:").Append(Px(cell.Padding)).Append("pt");
            if (cell.ImageData is null)
            {
                sb.Append(";display:flex;flex-direction:column;justify-content:").Append(cell.VAlign switch { ReportVAlign.Middle => "center", ReportVAlign.Bottom => "flex-end", _ => "flex-start" });
            }

            if (!cell.WordWrap)
            {
                sb.Append(";white-space:nowrap;overflow:hidden;text-overflow:ellipsis");
            }

            if (cell.Rotation != 0)
            {
                sb.Append(";transform:rotate(").Append(cell.Rotation.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append("deg)");
            }

            if (cell.ImageData is string image)
            {
                sb.Append("\"><img src=\"").Append(image).Append("\" alt=\"").Append(Escape(cell.Text)).Append("\" style=\"max-width:100%;max-height:100%\"></div>");
                continue;
            }

            if (cell.Runs is { Count: > 0 } runs)
            {
                sb.Append(";overflow:hidden\">");
                foreach (RichRun run in runs)
                {
                    if (run.LineBreak)
                    {
                        sb.Append("<br>");
                        continue;
                    }

                    string open = (run.Bold ? "<strong>" : string.Empty) + (run.Italic ? "<em>" : string.Empty) + (run.Underline ? "<u>" : string.Empty);
                    string close = (run.Underline ? "</u>" : string.Empty) + (run.Italic ? "</em>" : string.Empty) + (run.Bold ? "</strong>" : string.Empty);
                    sb.Append(open).Append(Escape(run.Text)).Append(close);
                }

                sb.Append("</div>");
                continue;
            }

            sb.Append("\">").Append(Escape(cell.Text)).Append("</div>");
        }

        sb.Append("</div>");
    }

    private static double PageWidthPoints(PageSetup page) => page.PaperSize switch
    {
        PaperSize.A4 => page.Orientation == ReportOrientation.Landscape ? 842 : 595,
        PaperSize.Letter => page.Orientation == ReportOrientation.Landscape ? 792 : 612,
        PaperSize.Legal => page.Orientation == ReportOrientation.Landscape ? 1008 : 612,
        PaperSize.A5 => 420,
        PaperSize.Receipt58 => 164,
        PaperSize.Receipt80 => 227,
        PaperSize.Custom => page.CustomWidth,
        _ => 595,
    };

    private static string Px(double points) => points.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Escape(string? text) => (text ?? string.Empty)
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}

/// <summary>Exports the raw report data rows to CSV (brief §13.3 export).</summary>
public sealed class CsvReportExporter
{
    /// <summary>Exports rows to CSV bytes, with a header row of the field names.</summary>
    /// <param name="rows">The data rows.</param>
    /// <returns>The CSV bytes.</returns>
    public byte[] Export(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        StringBuilder sb = new();
        if (rows.Count == 0)
        {
            return Encoding.UTF8.GetBytes(string.Empty);
        }

        List<string> columns = rows[0].Keys.ToList();
        sb.AppendLine(string.Join(',', columns.Select(EscapeCsv)));

        foreach (IReadOnlyDictionary<string, object?> row in rows)
        {
            sb.AppendLine(string.Join(',', columns.Select(c => EscapeCsv(Convert.ToString(row.GetValueOrDefault(c), CultureInfo.InvariantCulture)))));
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static string EscapeCsv(string? value)
    {
        value ??= string.Empty;
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }
}

/// <summary>
/// Renders a report to monospaced plain text / ESC-POS-friendly output (brief §13.3), positioning cells by
/// approximate character columns. Suitable for thermal receipt and dot-matrix printers.
/// </summary>
public sealed class PlainTextReportRenderer : IReportRenderer
{
    private const double PointsPerChar = 6.0; // ~10 cpi

    /// <inheritdoc />
    public string ContentType => "text/plain";

    /// <inheritdoc />
    public string FileExtension => "txt";

    /// <inheritdoc />
    public byte[] Render(RenderModel model) => Encoding.UTF8.GetBytes(RenderToString(model));

    /// <summary>Renders to a plain-text string.</summary>
    /// <param name="model">The render model.</param>
    /// <returns>The text.</returns>
    public string RenderToString(RenderModel model)
    {
        StringBuilder sb = new();
        RenderBandText(sb, model.ReportHeader);
        RenderBandText(sb, model.PageHeader);
        foreach (RenderBand band in model.Body)
        {
            RenderBandText(sb, band);
        }

        RenderBandText(sb, model.ReportFooter);
        return sb.ToString();
    }

    private static void RenderBandText(StringBuilder sb, RenderBand? band)
    {
        if (band is null)
        {
            return;
        }

        // Group cells into text lines by their Y, ordered by X.
        foreach (IGrouping<int, RenderCell> line in band.Cells
            .Where(c => c.Kind is ElementKind.Label or ElementKind.Field or ElementKind.Expression or ElementKind.Aggregate)
            .GroupBy(c => (int)Math.Round(c.Y / 12.0))
            .OrderBy(g => g.Key))
        {
            StringBuilder row = new();
            foreach (RenderCell cell in line.OrderBy(c => c.X))
            {
                int col = (int)Math.Round(cell.X / PointsPerChar);
                if (row.Length < col)
                {
                    row.Append(new string(' ', col - row.Length));
                }

                row.Append(cell.Text);
            }

            sb.AppendLine(row.ToString().TrimEnd());
        }
    }
}
