using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using One4All.Reporting.Engine;
using One4All.Reporting.Model;

namespace One4All.Desktop.Reporting;

/// <summary>
/// Builds a WPF <see cref="FixedDocument"/> from a report's laid-out model (brief §13.3 on-screen preview and direct printing): each page
/// sized to the paper, cells placed where the designer put them, the watermark across each page, and values that drill through to their
/// source shown as links.
/// </summary>
public static class FixedReportDocument
{
    private const double PointsToDips = 96d / 72d;

    /// <summary>Builds the document.</summary>
    /// <param name="model">The laid-out model.</param>
    /// <param name="drill">Called with the target and key when a drill-through value is clicked (null: no links).</param>
    /// <returns>The document and its pages.</returns>
    public static (FixedDocument Document, IReadOnlyList<LayoutPage> Pages) Build(RenderModel model, Action<string, string>? drill)
    {
        ArgumentNullException.ThrowIfNull(model);
        IReadOnlyList<LayoutPage> pages = ReportLayout.Paginate(model);
        double content = (model.PageHeader?.Height ?? 0) + (model.PageFooter?.Height ?? 0) + (model.ReportHeader?.Height ?? 0) + (model.ReportFooter?.Height ?? 0) + model.Body.Sum(b => b.Height);
        (double width, double height) = ReportLayout.PaperSize(model.Page, content);
        FixedDocument document = new();
        document.DocumentPaginator.PageSize = new Size(width * PointsToDips, height * PointsToDips);

        foreach (LayoutPage page in pages)
        {
            FixedPage fixedPage = new()
            {
                Width = width * PointsToDips, Height = height * PointsToDips, Background = Brushes.White, FlowDirection = model.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
            };
            if (!string.IsNullOrWhiteSpace(model.Page.Watermark))
            {
                fixedPage.Children.Add(Watermark(model.Page.Watermark, width * PointsToDips, height * PointsToDips));
            }

            foreach (PlacedBand placed in page.Bands)
            {
                foreach (RenderCell cell in placed.Band.Cells)
                {
                    UIElement element = Cell(cell, drill);
                    FixedPage.SetLeft(element, (model.Page.MarginLeft + cell.X) * PointsToDips);
                    FixedPage.SetTop(element, (model.Page.MarginTop + placed.Top + cell.Y) * PointsToDips);
                    fixedPage.Children.Add(element);
                }
            }

            PageContent pageContent = new();
            ((System.Windows.Markup.IAddChild)pageContent).AddChild(fixedPage);
            document.Pages.Add(pageContent);
        }

        return (document, pages);
    }

    private static UIElement Cell(RenderCell cell, Action<string, string>? drill)
    {
        double w = Math.Max(1, cell.Width * PointsToDips), h = Math.Max(1, cell.Height * PointsToDips);
        if (cell.Kind == ElementKind.Line)
        {
            return new System.Windows.Shapes.Line { X1 = 0, Y1 = 0, X2 = w, Y2 = 0, Stroke = Brush(cell.BorderColor) ?? Brushes.Black, StrokeThickness = Math.Max(0.75, cell.BorderThickness) };
        }

        if ((cell.ImageData ?? (cell.Kind == ElementKind.Image ? cell.Text : null)) is string picture && TryImage(picture) is BitmapImage image)
        {
            return new Image { Source = image, Width = w, Height = h, Stretch = Stretch.Uniform };
        }

        TextBlock text = new()
        {
            Text = cell.Text, FontFamily = new FontFamily(cell.FontFamily), FontSize = Math.Max(4, cell.FontSize * PointsToDips), FontWeight = cell.Bold ? FontWeights.Bold : FontWeights.Normal,
            FontStyle = cell.Italic ? FontStyles.Italic : FontStyles.Normal, Foreground = Brush(cell.Foreground) ?? Brushes.Black, TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = cell.HAlign switch { ReportHAlign.Center => TextAlignment.Center, ReportHAlign.Right => TextAlignment.Right, _ => TextAlignment.Left },
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (cell.Underline)
        {
            text.TextDecorations = TextDecorations.Underline;
        }

        if (drill is not null && cell.DrillTarget is string target && cell.DrillKey is string key)
        {
            text.Foreground = Brushes.RoyalBlue;
            text.TextDecorations = TextDecorations.Underline;
            text.Cursor = Cursors.Hand;
            text.ToolTip = $"Open {target} {key}";
            text.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                drill(target, key);
            };
        }

        return new Border
        {
            Width = w, Height = h, Child = text, Background = Brush(cell.Background), BorderBrush = Brush(cell.BorderColor),
            BorderThickness = new Thickness(cell.BorderThickness > 0 || cell.Kind == ElementKind.Rectangle ? Math.Max(0.5, cell.BorderThickness) * PointsToDips : 0),
            Padding = new Thickness(1, 0, 1, 0),
        };
    }

    private static UIElement Watermark(string text, double width, double height)
    {
        TextBlock mark = new()
        {
            Text = text, FontSize = Math.Min(width, height) / 6, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromArgb(40, 200, 0, 0)),
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(-35), Width = width, TextAlignment = TextAlignment.Center,
        };
        FixedPage.SetTop(mark, (height / 2) - (mark.FontSize / 2));
        return mark;
    }

    private static Brush? Brush(string? color)
    {
        try
        {
            return string.IsNullOrWhiteSpace(color) ? null : (Brush?)new BrushConverter().ConvertFromString(color);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static BitmapImage? TryImage(string? base64)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(base64))
            {
                return null;
            }

            BitmapImage image = new();
            image.BeginInit();
            image.StreamSource = new MemoryStream(Convert.FromBase64String(base64[(base64.IndexOf(',', StringComparison.Ordinal) + 1)..]));
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or IOException)
        {
            return null;
        }
    }
}
