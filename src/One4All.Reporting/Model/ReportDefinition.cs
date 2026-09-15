namespace One4All.Reporting.Model;

/// <summary>Page setup for a report (brief §13.1).</summary>
public sealed class PageSetup
{
    /// <summary>The paper size.</summary>
    public PaperSize PaperSize { get; set; } = PaperSize.A4;

    /// <summary>Orientation.</summary>
    public ReportOrientation Orientation { get; set; } = ReportOrientation.Portrait;

    /// <summary>Custom page width in points (used when <see cref="PaperSize"/> is <see cref="PaperSize.Custom"/>).</summary>
    public double CustomWidth { get; set; }

    /// <summary>Custom page height in points.</summary>
    public double CustomHeight { get; set; }

    /// <summary>Left margin in points.</summary>
    public double MarginLeft { get; set; } = 36;

    /// <summary>Right margin in points.</summary>
    public double MarginRight { get; set; } = 36;

    /// <summary>Top margin in points.</summary>
    public double MarginTop { get; set; } = 36;

    /// <summary>Bottom margin in points.</summary>
    public double MarginBottom { get; set; } = 36;

    /// <summary>Optional watermark text.</summary>
    public string? Watermark { get; set; }
}

/// <summary>A report parameter prompted at run time (brief §13.1).</summary>
public sealed class ReportParameter
{
    /// <summary>The parameter name (referenced in queries and expressions as <c>Parameters.Name</c>).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The parameter type.</summary>
    public ParameterType Type { get; set; }

    /// <summary>The prompt shown to the user.</summary>
    public string? Prompt { get; set; }

    /// <summary>Whether a value is required.</summary>
    public bool Required { get; set; } = true;

    /// <summary>Whether multiple values may be selected.</summary>
    public bool MultiSelect { get; set; }

    /// <summary>The default value (as a string; coerced to <see cref="Type"/>).</summary>
    public string? DefaultValue { get; set; }

    /// <summary>An optional query returning the list of valid values.</summary>
    public string? LookupQuery { get; set; }
}

/// <summary>A named data source for a report (brief §13.1).</summary>
public sealed class ReportDataSource
{
    /// <summary>The data-source name (the default/primary source is usually named <c>Main</c>).</summary>
    public string Name { get; set; } = "Main";

    /// <summary>How the data is obtained.</summary>
    public DataSourceKind Kind { get; set; }

    /// <summary>The SQL query, view name, function name, or in-memory key.</summary>
    public string Command { get; set; } = string.Empty;
}

/// <summary>A grouping level for a report (brief §13.1).</summary>
public sealed class ReportGroup
{
    /// <summary>The field or expression to group on.</summary>
    public string Expression { get; set; } = string.Empty;

    /// <summary>Whether to sort descending.</summary>
    public bool Descending { get; set; }
}

/// <summary>A band and the elements placed on it.</summary>
public sealed class ReportBand
{
    /// <summary>The band type.</summary>
    public BandType Type { get; set; }

    /// <summary>The group level this band belongs to (0-based) for group header/footer bands.</summary>
    public int GroupLevel { get; set; }

    /// <summary>The band height in points.</summary>
    public double Height { get; set; } = 20;

    /// <summary>Whether the band grows to fit its content.</summary>
    public bool AutoGrow { get; set; }

    /// <summary>Whether the band repeats on each new page (group headers).</summary>
    public bool RepeatOnNewPage { get; set; }

    /// <summary>An expression that, when true, suppresses the whole band.</summary>
    public string? SuppressWhen { get; set; }

    /// <summary>The elements on the band.</summary>
    public List<ReportElement> Elements { get; set; } = [];
}

/// <summary>
/// The root of a report definition (brief §13.1), persisted as JSON in <c>rpt.report_definition</c> and
/// exportable as an <c>.mrpt</c> file. Contains the page setup, parameters, data sources, grouping and the
/// ordered bands with their elements. System reports are read-only and copied to a user version to edit.
/// </summary>
public sealed class ReportDefinition
{
    /// <summary>The report code (stable identifier, e.g. <c>GL-TB</c>).</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>The report display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>A description of the report's purpose.</summary>
    public string? Description { get; set; }

    /// <summary>The module this report belongs to.</summary>
    public string? Module { get; set; }

    /// <summary>The definition version.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Page setup.</summary>
    public PageSetup Page { get; set; } = new();

    /// <summary>Report parameters.</summary>
    public List<ReportParameter> Parameters { get; set; } = [];

    /// <summary>Data sources.</summary>
    public List<ReportDataSource> DataSources { get; set; } = [];

    /// <summary>Grouping levels, outermost first.</summary>
    public List<ReportGroup> Groups { get; set; } = [];

    /// <summary>The bands, in render order.</summary>
    public List<ReportBand> Bands { get; set; } = [];

    /// <summary>Returns the first band of a given type, or null.</summary>
    /// <param name="type">The band type.</param>
    /// <returns>The band, or null.</returns>
    public ReportBand? BandOfType(BandType type) => Bands.FirstOrDefault(b => b.Type == type);
}
