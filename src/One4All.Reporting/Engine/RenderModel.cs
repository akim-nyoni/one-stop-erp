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
}

/// <summary>A resolved, positioned cell ready to render.</summary>
public sealed class RenderCell
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
}

/// <summary>A resolved band ready to render.</summary>
public sealed class RenderBand
{
    /// <summary>The band type.</summary>
    public BandType Type { get; init; }

    /// <summary>The band height in points.</summary>
    public double Height { get; init; }

    /// <summary>The resolved cells.</summary>
    public List<RenderCell> Cells { get; init; } = [];
}

/// <summary>The fully resolved render model: page setup plus bands in flow order.</summary>
public sealed class RenderModel
{
    /// <summary>The report name.</summary>
    public string ReportName { get; init; } = string.Empty;

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
}
