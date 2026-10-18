using One4All.Reporting.Model;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace One4All.Reporting.Engine;

/// <summary>
/// Renders a report to PDF using QuestPDF (brief §13.3). Each band is a fixed-height block whose cells are
/// absolutely positioned in points, matching the HTML renderer's layout.
/// </summary>
public sealed class PdfReportRenderer : IReportRenderer
{
    static PdfReportRenderer() => QuestPDF.Settings.License = LicenseType.Community;

    /// <inheritdoc />
    public string ContentType => "application/pdf";

    /// <inheritdoc />
    public string FileExtension => "pdf";

    /// <inheritdoc />
    public byte[] Render(RenderModel model) => Compose(model).GeneratePdf();

    /// <summary>Renders each page as a JPEG picture (a designed document that is wrapped and protected afterwards).</summary>
    /// <param name="model">The report.</param>
    /// <param name="dpi">The resolution.</param>
    /// <returns>The pages and the page width in points.</returns>
    public (IReadOnlyList<byte[]> Jpegs, double WidthPoints) RenderImages(RenderModel model, int dpi = 150)
    {
        ArgumentNullException.ThrowIfNull(model);
        IEnumerable<byte[]> images = Compose(model).GenerateImages(new ImageGenerationSettings { ImageFormat = ImageFormat.Jpeg, ImageCompressionQuality = ImageCompressionQuality.High, RasterDpi = dpi });
        return ([.. images], PageWidthPoints(model.Page));
    }

    /// <summary>The page width in points for a page setup.</summary>
    /// <param name="setup">The page setup.</param>
    /// <returns>The width.</returns>
    public static double PageWidthPoints(PageSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        bool landscape = setup.Orientation == ReportOrientation.Landscape;
        return setup.PaperSize switch
        {
            PaperSize.A4 => landscape ? PageSizes.A4.Height : PageSizes.A4.Width,
            PaperSize.Letter => landscape ? PageSizes.Letter.Height : PageSizes.Letter.Width,
            PaperSize.Legal => landscape ? PageSizes.Legal.Height : PageSizes.Legal.Width,
            PaperSize.A5 => PageSizes.A5.Width,
            PaperSize.Receipt58 => 164,
            PaperSize.Receipt80 => 227,
            PaperSize.Custom => setup.CustomWidth,
            _ => PageSizes.A4.Width,
        };
    }

    private static Document Compose(RenderModel model)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                ConfigurePageSize(page, model.Page);
                page.MarginLeft((float)model.Page.MarginLeft);
                page.MarginRight((float)model.Page.MarginRight);
                page.MarginTop((float)model.Page.MarginTop);
                page.MarginBottom((float)model.Page.MarginBottom);
                if (Picture(model.Page.BackgroundImage) is byte[] background)
                {
                    page.Background().Image(background).FitArea();
                }

                if (!string.IsNullOrWhiteSpace(model.Page.Watermark))
                {
                    page.Foreground().AlignCenter().AlignMiddle().Text(model.Page.Watermark).FontSize(64).Bold().FontColor(Colors.Grey.Lighten2);
                }

                if (model.PageHeader is not null)
                {
                    page.Header().Element(e => RenderBand(e, model.PageHeader));
                }

                page.Content().Column(col =>
                {
                    if (model.ReportHeader is not null)
                    {
                        col.Item().Element(e => RenderBand(e, model.ReportHeader));
                    }

                    foreach (RenderBand band in model.Body)
                    {
                        if (band.PageBreakBefore)
                        {
                            col.Item().PageBreak();
                        }

                        col.Item().Element(e => RenderBand(e, band));
                        if (band.PageBreakAfter)
                        {
                            col.Item().PageBreak();
                        }
                    }

                    if (model.ReportFooter is not null)
                    {
                        col.Item().Element(e => RenderBand(e, model.ReportFooter));
                    }
                });

                if (model.PageFooter is not null)
                {
                    page.Footer().Element(e => RenderBand(e, model.PageFooter));
                }
            });
        });
    }

    // A base64 picture or data URI as bytes (null when absent or unreadable).
    private static byte[]? Picture(string? data)
    {
        if (string.IsNullOrWhiteSpace(data))
        {
            return null;
        }

        try
        {
            return Convert.FromBase64String(data[(data.IndexOf(',', StringComparison.Ordinal) + 1)..]);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static void ConfigurePageSize(PageDescriptor page, PageSetup setup)
    {
        switch (setup.PaperSize)
        {
            case PaperSize.A4:
                page.Size(setup.Orientation == ReportOrientation.Landscape ? PageSizes.A4.Landscape() : PageSizes.A4);
                break;
            case PaperSize.Letter:
                page.Size(setup.Orientation == ReportOrientation.Landscape ? PageSizes.Letter.Landscape() : PageSizes.Letter);
                break;
            case PaperSize.Legal:
                page.Size(setup.Orientation == ReportOrientation.Landscape ? PageSizes.Legal.Landscape() : PageSizes.Legal);
                break;
            case PaperSize.A5:
                page.Size(PageSizes.A5);
                break;
            case PaperSize.Receipt58:
                page.ContinuousSize(164);
                break;
            case PaperSize.Receipt80:
                page.ContinuousSize(227);
                break;
            case PaperSize.Custom:
                page.Size((float)setup.CustomWidth, (float)setup.CustomHeight);
                break;
            default:
                page.Size(PageSizes.A4);
                break;
        }
    }

    private static void RenderBand(IContainer container, RenderBand band)
    {
        container.Height((float)band.Height).Layers(layers =>
        {
            layers.PrimaryLayer().Height((float)band.Height);

            foreach (RenderCell cell in band.Cells)
            {
                layers.Layer()
                    .PaddingLeft((float)cell.X)
                    .PaddingTop((float)cell.Y)
                    .Width((float)cell.Width)
                    .Element(inner => RenderCell(inner, cell));
            }
        });
    }

    private const string PageToken = "[Page]";
    private const string PagesToken = "[Pages]";

    // The SVG of an ellipse or a round-cornered box filling the cell, in the cell's fill and border colours.
    private static string ShapeSvg(RenderCell cell)
    {
        System.Globalization.CultureInfo inv = System.Globalization.CultureInfo.InvariantCulture;
        double w = Math.Max(cell.Width, 1), h = Math.Max(cell.Height, 1), stroke = Math.Max(cell.BorderThickness, 0.5), half = stroke / 2;
        string shape = cell.Kind == ElementKind.Ellipse
            ? string.Format(inv, "<ellipse cx=\"{0}\" cy=\"{1}\" rx=\"{2}\" ry=\"{3}\"", w / 2, h / 2, Math.Max(0.1, (w / 2) - half), Math.Max(0.1, (h / 2) - half))
            : string.Format(inv, "<rect x=\"{0}\" y=\"{0}\" width=\"{1}\" height=\"{2}\" rx=\"{3}\"", half, Math.Max(0.1, w - stroke), Math.Max(0.1, h - stroke), cell.CornerRadius);
        return string.Format(inv, "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{0}\" height=\"{1}\" viewBox=\"0 0 {0} {1}\">{2} fill=\"{3}\" stroke=\"{4}\" stroke-width=\"{5}\"/></svg>",
            w, h, shape, cell.Background ?? "none", cell.BorderColor, stroke);
    }

    // A run split around "[Page]" and "[Pages]", so the page number and count are drawn by the page.
    private static IEnumerable<RichRun> PageTokens(RichRun run)
    {
        if (run.LineBreak || !run.Text.Contains("[Page", StringComparison.Ordinal))
        {
            yield return run;
            yield break;
        }

        foreach (string part in System.Text.RegularExpressions.Regex.Split(run.Text, @"(\[Pages?\])"))
        {
            if (part.Length > 0)
            {
                yield return run with { Text = part };
            }
        }
    }

    private static void RenderCell(IContainer container, RenderCell cell)
    {
        // An ellipse or a box with round corners is drawn as a picture of the shape.
        if (cell.Kind == ElementKind.Ellipse || (cell.Kind == ElementKind.Rectangle && cell.CornerRadius > 0))
        {
            container.Height((float)Math.Max(cell.Height, 1)).Svg(ShapeSvg(cell));
            return;
        }

        (double left, double top, double right, double bottom) = cell.Borders();
        if (left + top + right + bottom > 0)
        {
            container = container.BorderLeft((float)left).BorderTop((float)top).BorderRight((float)right).BorderBottom((float)bottom).BorderColor(cell.BorderColor);
        }

        if (cell.Background is not null)
        {
            container = container.Background(cell.Background);
        }

        if (cell.Kind is ElementKind.Rectangle or ElementKind.Line)
        {
            container.Height((float)Math.Max(cell.Height, 1));
            return;
        }

        if (cell.ImageData is string image && image.IndexOf(',', StringComparison.Ordinal) is int comma and > 0)
        {
            try
            {
                container.Height((float)Math.Max(cell.Height, 1)).Image(Convert.FromBase64String(image[(comma + 1)..])).FitArea();
                return;
            }
            catch (FormatException)
            {
                // Not a readable picture: fall through to the text.
            }
        }

        container = container.Padding((float)Math.Max(0, cell.Padding));
        container = cell.Rotation switch
        {
            90 => container.RotateRight(),
            180 => container.RotateRight().RotateRight(),
            270 => container.RotateLeft(),
            0 => container,
            _ => container.Rotate(cell.Rotation),
        };
        container = cell.VAlign switch
        {
            ReportVAlign.Middle => container.AlignMiddle(),
            ReportVAlign.Bottom => container.AlignBottom(),
            _ => container.AlignTop(),
        };
        IContainer aligned = cell.HAlign switch
        {
            ReportHAlign.Right => container.AlignRight(),
            ReportHAlign.Center => container.AlignCenter(),
            _ => container.AlignLeft(),
        };

        aligned.Text(text =>
        {
            if (!cell.WordWrap)
            {
                text.ClampLines(1);
            }

            IEnumerable<RichRun> runs = cell.Runs is { Count: > 0 } rich ? rich : [new RichRun(cell.Text, cell.Bold, cell.Italic, cell.Underline)];
            foreach (RichRun run in runs.SelectMany(PageTokens))
            {
                TextSpanDescriptor span = run.Text switch
                {
                    PageToken when !run.LineBreak => text.CurrentPageNumber(),
                    PagesToken when !run.LineBreak => text.TotalPages(),
                    _ => text.Span(run.LineBreak ? "\n" : run.Text),
                };
                span = span.FontSize((float)cell.FontSize).FontColor(cell.Foreground);
                if (run.Bold || cell.Bold)
                {
                    span.Bold();
                }

                if (run.Italic || cell.Italic)
                {
                    span.Italic();
                }

                if (run.Underline || cell.Underline)
                {
                    span.Underline();
                }
            }
        });
    }
}
