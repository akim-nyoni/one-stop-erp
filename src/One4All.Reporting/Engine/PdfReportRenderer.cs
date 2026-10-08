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
    public byte[] Render(RenderModel model)
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
        }).GeneratePdf();
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
        if (cell.BorderThickness > 0 || cell.Kind == ElementKind.Rectangle)
        {
            container = container.Border((float)Math.Max(cell.BorderThickness, 0.5)).BorderColor(cell.BorderColor);
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

        IContainer aligned = cell.HAlign switch
        {
            ReportHAlign.Right => container.AlignRight(),
            ReportHAlign.Center => container.AlignCenter(),
            _ => container.AlignLeft(),
        };

        aligned.Text(text =>
        {
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
