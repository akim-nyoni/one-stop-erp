using System.Globalization;
using System.Text.Json;
using One4All.Reporting.Model;

namespace One4All.Reporting.Engine;

/// <summary>Where a band sits on a page.</summary>
/// <param name="Band">The band.</param>
/// <param name="Top">Its top, in points from the top of the page's printable area.</param>
public sealed record PlacedBand(RenderBand Band, double Top);

/// <summary>One page of a laid-out report.</summary>
/// <param name="Number">The page number (from 1).</param>
/// <param name="Bands">The bands on it, in order.</param>
public sealed record LayoutPage(int Number, IReadOnlyList<PlacedBand> Bands);

/// <summary>
/// Paginates a render model for on-screen preview and printing (brief §13.3): the page header and footer on every page, the report header
/// first and the report footer last, body bands flowing down the printable height and breaking before a band that would not fit. Sizes are
/// points; "[Page]" and "[Pages]" in a cell's text become the page number and count.
/// </summary>
public static class ReportLayout
{
    /// <summary>The paper size in points (portrait unless the setup says landscape; receipt rolls are one long page).</summary>
    /// <param name="page">The page setup.</param>
    /// <param name="contentHeight">The height the content needs (for receipt rolls).</param>
    /// <returns>Width and height.</returns>
    public static (double Width, double Height) PaperSize(PageSetup page, double contentHeight = 0)
    {
        ArgumentNullException.ThrowIfNull(page);
        (double w, double h) = page.PaperSize switch
        {
            Model.PaperSize.Letter => (612d, 792d),
            Model.PaperSize.A5 => (420d, 595d),
            Model.PaperSize.Receipt58 => (164d, Math.Max(200d, contentHeight + page.MarginTop + page.MarginBottom)),
            Model.PaperSize.Receipt80 => (227d, Math.Max(200d, contentHeight + page.MarginTop + page.MarginBottom)),
            Model.PaperSize.Custom => (page.CustomWidth, page.CustomHeight),
            _ => (595d, 842d),
        };
        return page.Orientation == ReportOrientation.Landscape && page.PaperSize is not (Model.PaperSize.Receipt58 or Model.PaperSize.Receipt80) ? (h, w) : (w, h);
    }

    /// <summary>Lays the model out on pages.</summary>
    /// <param name="model">The model.</param>
    /// <returns>The pages (at least one).</returns>
    public static IReadOnlyList<LayoutPage> Paginate(RenderModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        List<RenderBand> flow = [.. new[] { model.ReportHeader }.OfType<RenderBand>(), .. model.Body, .. new[] { model.ReportFooter }.OfType<RenderBand>()];
        double header = model.PageHeader?.Height ?? 0, footer = model.PageFooter?.Height ?? 0;
        (double _, double paperHeight) = PaperSize(model.Page, header + footer + flow.Sum(b => b.Height));
        double available = Math.Max(1, paperHeight - model.Page.MarginTop - model.Page.MarginBottom - header - footer);

        List<List<RenderBand>> pages = [[]];
        double used = 0;
        foreach (RenderBand band in flow)
        {
            if (used > 0 && used + band.Height > available)
            {
                pages.Add([]);
                used = 0;
            }

            pages[^1].Add(band);
            used += band.Height;
        }

        List<LayoutPage> laid = [];
        for (int n = 0; n < pages.Count; n++)
        {
            List<PlacedBand> placed = [];
            if (model.PageHeader is not null)
            {
                placed.Add(new PlacedBand(Numbered(model.PageHeader, n + 1, pages.Count), 0));
            }

            double top = header;
            foreach (RenderBand band in pages[n])
            {
                placed.Add(new PlacedBand(Numbered(band, n + 1, pages.Count), top));
                top += band.Height;
            }

            if (model.PageFooter is not null)
            {
                placed.Add(new PlacedBand(Numbered(model.PageFooter, n + 1, pages.Count), header + available));
            }

            laid.Add(new LayoutPage(n + 1, placed));
        }

        return laid;
    }

    /// <summary>Serialises a model for the desktop viewer (format "layout").</summary>
    /// <param name="model">The model.</param>
    /// <returns>The JSON bytes.</returns>
    public static byte[] Serialize(RenderModel model) => JsonSerializer.SerializeToUtf8Bytes(model);

    /// <summary>Reads a serialised model.</summary>
    /// <param name="json">The JSON bytes.</param>
    /// <returns>The model.</returns>
    public static RenderModel Deserialize(byte[] json) => JsonSerializer.Deserialize<RenderModel>(json) ?? throw new InvalidDataException("Not a report layout.");

    /// <summary>Finds cells whose text contains a term (case-insensitive), page by page.</summary>
    /// <param name="pages">The pages.</param>
    /// <param name="term">The term.</param>
    /// <returns>The page numbers and texts that match.</returns>
    public static IReadOnlyList<(int Page, string Text)> Search(IEnumerable<LayoutPage> pages, string term)
        => string.IsNullOrWhiteSpace(term) ? []
            : [.. pages.SelectMany(p => p.Bands.SelectMany(b => b.Band.Cells).Where(c => c.Text.Contains(term.Trim(), StringComparison.OrdinalIgnoreCase)).Select(c => (p.Number, c.Text)))];

    private static RenderBand Numbered(RenderBand band, int page, int pages)
        => band.Cells.Any(c => c.Text.Contains("[Page", StringComparison.Ordinal))
            ? new RenderBand
            {
                Type = band.Type, Height = band.Height,
                Cells = [.. band.Cells.Select(c => c.Text.Contains("[Page", StringComparison.Ordinal) ? With(c, c.Text.Replace("[Pages]", pages.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                    .Replace("[Page]", page.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)) : c)],
            }
            : band;

    private static RenderCell With(RenderCell c, string text) => new()
    {
        Kind = c.Kind, Text = text, X = c.X, Y = c.Y, Width = c.Width, Height = c.Height, FontFamily = c.FontFamily, FontSize = c.FontSize, Bold = c.Bold, Italic = c.Italic,
        Underline = c.Underline, Foreground = c.Foreground, Background = c.Background, HAlign = c.HAlign, BorderThickness = c.BorderThickness, BorderColor = c.BorderColor,
        DrillTarget = c.DrillTarget, DrillKey = c.DrillKey, Runs = c.Runs is null ? null : [new RichRun(text)], ImageData = c.ImageData,
    };
}

/// <summary>Helpers for repositioning rendered cells.</summary>
public static class Layouts
{
    /// <summary>A copy of a cell at a new position.</summary>
    /// <param name="c">The cell.</param>
    /// <param name="x">Its new left.</param>
    /// <param name="y">Its new top.</param>
    /// <returns>The moved cell.</returns>
    public static RenderCell Moved(RenderCell c, double x, double y) => new()
    {
        Kind = c.Kind, Text = c.Text, X = x, Y = y, Width = c.Width, Height = c.Height, FontFamily = c.FontFamily, FontSize = c.FontSize, Bold = c.Bold, Italic = c.Italic,
        Underline = c.Underline, Foreground = c.Foreground, Background = c.Background, HAlign = c.HAlign, BorderThickness = c.BorderThickness, BorderColor = c.BorderColor,
        DrillTarget = c.DrillTarget, DrillKey = c.DrillKey, Runs = c.Runs, ImageData = c.ImageData,
    };
}

/// <summary>Works out what a report value drills through to.</summary>
public static class DrillThrough
{
    /// <summary>The drill target and key for an element on a row: its explicit target, or one inferred from the field's name.</summary>
    /// <param name="element">The element.</param>
    /// <param name="row">The data row (null outside the detail band).</param>
    /// <returns>The target and key, or nulls.</returns>
    public static (string? Target, string? Key) For(ReportElement element, IReadOnlyDictionary<string, object?>? row)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (row is null || element.Kind != ElementKind.Field)
        {
            return (null, null);
        }

        string? target = string.IsNullOrWhiteSpace(element.DrillTarget) ? Infer(element.FieldName) : element.DrillTarget;
        string? keyField = element.DrillField ?? element.FieldName;
        if (target is null || keyField is null)
        {
            return (null, null);
        }

        object? value = row.FirstOrDefault(kv => string.Equals(kv.Key, keyField, StringComparison.OrdinalIgnoreCase)).Value;
        string? key = Convert.ToString(value, CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(key) ? (null, null) : (target, key);
    }

    /// <summary>The target a field name implies (JournalNumber → Journal, AccountCode → Account...).</summary>
    /// <param name="fieldName">The field name.</param>
    /// <returns>The target, or null.</returns>
    public static string? Infer(string? fieldName)
    {
        string name = (fieldName ?? string.Empty).Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        return name switch
        {
            "journalnumber" or "journalno" or "journal" => "Journal",
            "accountcode" or "account" or "glaccount" => "Account",
            "customercode" or "customer" or "debtorcode" => "Customer",
            "suppliercode" or "supplier" or "creditorcode" => "Supplier",
            "itemcode" or "item" or "stockcode" => "Item",
            _ => null,
        };
    }
}
