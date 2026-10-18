using One4All.Reporting.Model;

namespace One4All.Reporting.Engine;

/// <summary>The data and context fed to the report engine to produce a rendered document.</summary>
public sealed class ReportInput
{
    /// <summary>The report definition.</summary>
    public required ReportDefinition Definition { get; init; }

    /// <summary>The data rows (each a field-name → value map).</summary>
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows { get; init; } = [];

    /// <summary>The report parameter values.</summary>
    public IReadOnlyDictionary<string, object?> Parameters { get; init; } = new Dictionary<string, object?>();

    /// <summary>The user running the report.</summary>
    public string UserName { get; init; } = string.Empty;

    /// <summary>The company name.</summary>
    public string CompanyName { get; init; } = string.Empty;

    /// <summary>The print timestamp.</summary>
    public DateTime PrintDate { get; init; } = DateTime.Now;

    /// <summary>Optional currency-to-base conversion helper.</summary>
    public Func<decimal, string, DateOnly, decimal>? ToBase { get; init; }

    /// <summary>Gets the culture numbers and dates are formatted in, and labels translated to (null: invariant formats, labels as designed).</summary>
    public System.Globalization.CultureInfo? Culture { get; init; }

    /// <summary>Gets the company's terminology applied to labels (term → its word).</summary>
    public IReadOnlyDictionary<string, string>? Terminology { get; init; }

    /// <summary>Supplies a sub-report's input for its code and parameters (null when it cannot be run); without it sub-reports show a note.</summary>
    public Func<string, IReadOnlyDictionary<string, object?>, ReportInput?>? SubReports { get; init; }

    /// <summary>How deep this report is nested as a sub-report (0 for the report itself).</summary>
    public int Depth { get; init; }

    /// <summary>The company's logo (PNG or JPEG), for image elements whose expression is <c>CompanyLogo</c>.</summary>
    public byte[]? CompanyLogo { get; init; }

    /// <summary>The company's house style, applied after layout (null: the report's own design only).</summary>
    public ReportHouseStyle? HouseStyle { get; init; }
}

/// <summary>A resolved, positioned cell ready to render.</summary>
public sealed record RenderCell
{
    /// <summary>The element kind.</summary>
    public ElementKind Kind { get; init; }

    /// <summary>The resolved display text.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>X position in points.</summary>
    public double X { get; init; }

    /// <summary>Y position in points.</summary>
    public double Y { get; init; }

    /// <summary>Width in points.</summary>
    public double Width { get; init; }

    /// <summary>Height in points.</summary>
    public double Height { get; init; }

    /// <summary>Font family.</summary>
    public string FontFamily { get; init; } = "Segoe UI";

    /// <summary>Font size.</summary>
    public double FontSize { get; init; } = 9;

    /// <summary>Bold.</summary>
    public bool Bold { get; init; }

    /// <summary>Italic.</summary>
    public bool Italic { get; init; }

    /// <summary>Underline.</summary>
    public bool Underline { get; init; }

    /// <summary>Foreground colour (hex).</summary>
    public string Foreground { get; init; } = "#000000";

    /// <summary>Background colour (hex) or null.</summary>
    public string? Background { get; init; }

    /// <summary>Horizontal alignment.</summary>
    public ReportHAlign HAlign { get; init; }

    /// <summary>Border thickness in points.</summary>
    public double BorderThickness { get; init; }

    /// <summary>Border colour (hex).</summary>
    public string BorderColor { get; init; } = "#000000";

    /// <summary>Gets the vertical alignment.</summary>
    public ReportVAlign VAlign { get; init; }

    /// <summary>Gets the left border width (null: the border thickness).</summary>
    public double? BorderLeft { get; init; }

    /// <summary>Gets the top border width (null: the border thickness).</summary>
    public double? BorderTop { get; init; }

    /// <summary>Gets the right border width (null: the border thickness).</summary>
    public double? BorderRight { get; init; }

    /// <summary>Gets the bottom border width (null: the border thickness).</summary>
    public double? BorderBottom { get; init; }

    /// <summary>Gets the padding in points.</summary>
    public double Padding { get; init; } = 1;

    /// <summary>Gets a value indicating whether text wraps.</summary>
    public bool WordWrap { get; init; } = true;

    /// <summary>Gets the rotation in degrees, clockwise (0 to 359).</summary>
    public int Rotation { get; init; }

    /// <summary>Gets the corner radius of a box in points.</summary>
    public double CornerRadius { get; init; }

    /// <summary>The border widths, side by side (a rectangle always has at least a hairline).</summary>
    /// <returns>Left, top, right and bottom.</returns>
    public (double Left, double Top, double Right, double Bottom) Borders()
    {
        bool shape = Kind is ElementKind.Rectangle or ElementKind.Ellipse;
        double all = BorderThickness > 0 || shape ? Math.Max(BorderThickness, shape ? 0.5 : 0) : 0;
        return (BorderLeft ?? all, BorderTop ?? all, BorderRight ?? all, BorderBottom ?? all);
    }

    /// <summary>Gets what clicking the cell opens (null: nothing).</summary>
    public string? DrillTarget { get; init; }

    /// <summary>Gets the key of the record it opens.</summary>
    public string? DrillKey { get; init; }

    /// <summary>For rich text: the runs to draw (the plain text is in <see cref="Text"/>).</summary>
    public IReadOnlyList<RichRun>? Runs { get; init; }

    /// <summary>Text shown when the pointer rests on the cell.</summary>
    public string? Tooltip { get; init; }

    /// <summary>The second colour of a background gradient, when any.</summary>
    public string? BackgroundGradient { get; init; }

    /// <summary>Whether the gradient runs top to bottom.</summary>
    public bool GradientVertical { get; init; }

    /// <summary>Whether the cell keeps its right edge at the right of its column.</summary>
    public bool AnchorRight { get; init; }

    /// <summary>Whether the cell keeps its distance from the bottom of a band that grows.</summary>
    public bool AnchorBottom { get; init; }

    /// <summary>For an image or barcode: the picture as a data URI (<c>data:image/png;base64,...</c>); the text keeps the barcode's value.</summary>
    public string? ImageData { get; init; }
}

/// <summary>A resolved band ready to render.</summary>
public sealed class RenderBand
{
    /// <summary>The band type.</summary>
    public BandType Type { get; init; }

    /// <summary>The band height in points.</summary>
    public double Height { get; set; }

    /// <summary>Gets or sets the group level (group headers and footers).</summary>
    public int GroupLevel { get; set; }

    /// <summary>Gets or sets a value indicating whether a group header is repeated at the top of each page its group continues on.</summary>
    public bool RepeatOnNewPage { get; set; }

    /// <summary>Gets or sets a value indicating whether a group header stays with the band after it.</summary>
    public bool KeepTogether { get; set; }

    /// <summary>Gets or sets a value indicating whether a new page starts before the band.</summary>
    public bool PageBreakBefore { get; set; }

    /// <summary>Gets or sets a value indicating whether a new page starts after the band.</summary>
    public bool PageBreakAfter { get; set; }

    /// <summary>The resolved cells.</summary>
    public List<RenderCell> Cells { get; init; } = [];
}

/// <summary>The fully resolved render model: page setup plus bands in flow order.</summary>
public sealed class RenderModel
{
    /// <summary>The report name.</summary>
    public string ReportName { get; init; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the report reads right to left (an RTL culture).</summary>
    public bool RightToLeft { get; set; }

    /// <summary>The page setup.</summary>
    public required PageSetup Page { get; init; }

    /// <summary>The report header band, if any (rendered once at the top).</summary>
    public RenderBand? ReportHeader { get; set; }

    /// <summary>The page header band, if any (rendered at the top of each page).</summary>
    public RenderBand? PageHeader { get; set; }

    /// <summary>The body bands (group headers, details, group footers) in flow order.</summary>
    public List<RenderBand> Body { get; init; } = [];

    /// <summary>The page footer band, if any.</summary>
    public RenderBand? PageFooter { get; set; }

    /// <summary>The report footer band, if any (rendered once at the end).</summary>
    public RenderBand? ReportFooter { get; set; }

    /// <summary>Whether this is the head of a longer report (the first pages shown while the rest are laid out).</summary>
    public bool IsHead { get; init; }

    /// <summary>
    /// The first bands of the report, to show at once while the rest are laid out (brief §13 performance: "renders first page &lt; 5 s, streams thereafter"):
    /// the headers and up to the given number of body bands, without the report footer (its totals belong to the whole report). The whole report when it is short.
    /// </summary>
    /// <param name="maxBodyBands">How many body bands to keep.</param>
    /// <returns>The head, or this model when it is no longer than that.</returns>
    public RenderModel Head(int maxBodyBands)
        => Body.Count <= maxBodyBands ? this : new RenderModel
        {
            ReportName = ReportName, RightToLeft = RightToLeft, Page = Page, ReportHeader = ReportHeader, PageHeader = PageHeader, PageFooter = PageFooter,
            Body = [.. Body.Take(Math.Max(0, maxBodyBands))], IsHead = true,
        };
}
