using One4All.Reporting.Model;

namespace One4All.Reporting.Engine;

/// <summary>Applies a company's <see cref="ReportHouseStyle"/> to a laid-out report: fonts, heading and title colours, accent lines, the footer line and the logo.</summary>
public static class ReportHouseStyles
{
    private const string StandardFont = "Segoe UI";
    private const double StandardSize = 9;

    /// <summary>Applies the input's theme (when there is one) to the model and returns it.</summary>
    /// <param name="model">The laid-out report.</param>
    /// <param name="input">The run, carrying the theme and the company logo.</param>
    /// <returns>The same model, themed.</returns>
    public static RenderModel Apply(RenderModel model, ReportInput input)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(input);
        if (input.HouseStyle is not ReportHouseStyle theme || theme.IsEmpty)
        {
            return model;
        }

        foreach ((RenderBand band, bool _) in Bands(model))
        {
            for (int i = 0; i < band.Cells.Count; i++)
            {
                band.Cells[i] = Style(band.Cells[i], band.Type, theme);
            }
        }

        double contentWidth = PdfReportRenderer.PageWidthPoints(model.Page) - model.Page.MarginLeft - model.Page.MarginRight;
        if (!string.IsNullOrWhiteSpace(theme.FooterText))
        {
            model.PageFooter ??= new RenderBand { Type = BandType.PageFooter, Height = 0 };
            model.PageFooter.Cells.Add(new RenderCell
            {
                Kind = ElementKind.Label, Text = theme.FooterText.Trim(), X = 0, Y = model.PageFooter.Height, Width = Math.Max(100, contentWidth), Height = 14,
                FontFamily = theme.FontFamily ?? StandardFont, FontSize = 8, Italic = true, Foreground = "#606060", HAlign = ReportHAlign.Center,
            });
            model.PageFooter.Height += 14;
        }

        if (theme.ShowLogo && input.CompanyLogo is { Length: > 0 } logo)
        {
            string picture = Barcodes.DataUri(logo);
            bool placed = Bands(model).Any(b => b.Band.Cells.Any(c => c.Kind == ElementKind.Image && c.ImageData == picture));
            if (!placed)
            {
                RenderBand target = model.ReportHeader ?? (model.PageHeader ??= new RenderBand { Type = BandType.PageHeader, Height = 0 });
                target.Cells.Add(new RenderCell { Kind = ElementKind.Image, ImageData = picture, X = Math.Max(0, contentWidth - 96), Y = 2, Width = 96, Height = 32 });
                target.Height = Math.Max(target.Height, 36);
            }
        }

        return model;
    }

    private static RenderCell Style(RenderCell cell, BandType band, ReportHouseStyle theme)
    {
        if (cell.Kind is ElementKind.Line or ElementKind.Rectangle or ElementKind.Ellipse)
        {
            return theme.AccentColour is string accent ? cell with { Foreground = accent, BorderColor = accent } : cell;
        }

        if (cell.Kind is ElementKind.Image or ElementKind.Barcode or ElementKind.Chart)
        {
            return cell;
        }

        RenderCell styled = cell;
        if (theme.FontFamily is string font && string.Equals(cell.FontFamily, StandardFont, StringComparison.OrdinalIgnoreCase))
        {
            styled = styled with { FontFamily = font };
        }

        if (theme.BaseFontSize is double size && cell.FontSize == StandardSize)
        {
            styled = styled with { FontSize = size };
        }

        if (band == BandType.ReportHeader && cell.Bold && cell.FontSize >= 12 && theme.TitleColour is string title)
        {
            styled = styled with { Foreground = title };
        }

        if (band is BandType.PageHeader or BandType.GroupHeader && cell.Background is not null && cell.Kind == ElementKind.Label)
        {
            styled = styled with { Background = theme.HeadingBackground ?? styled.Background, Foreground = theme.HeadingForeground ?? styled.Foreground };
        }

        return styled;
    }

    private static IEnumerable<(RenderBand Band, bool Body)> Bands(RenderModel model)
    {
        foreach (RenderBand? fixedBand in new[] { model.ReportHeader, model.PageHeader, model.PageFooter, model.ReportFooter })
        {
            if (fixedBand is not null)
            {
                yield return (fixedBand, false);
            }
        }

        foreach (RenderBand band in model.Body)
        {
            yield return (band, true);
        }
    }
}
