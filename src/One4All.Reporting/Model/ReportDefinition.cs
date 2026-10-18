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

    /// <summary>
    /// An expression giving the watermark for each run (brief §13.4: "CONFIDENTIAL/DRAFT" driven by expression) - evaluated against the parameters,
    /// the first row's fields, the user and the date, e.g. <c>"Draft".Equals(Param("Status")) ? "DRAFT" : ""</c>; blank falls back to <see cref="Watermark"/>.
    /// </summary>
    public string? WatermarkExpression { get; set; }

    /// <summary>Gets or sets the number of columns the detail rows flow across (labels, cards); 1 for an ordinary report.</summary>
    public int Columns { get; set; } = 1;

    /// <summary>Gets or sets the gap between columns, in points.</summary>
    public double ColumnGap { get; set; } = 12;

    /// <summary>Gets or sets a picture (base64 or a data URI) printed behind every page - letterhead, a form, a certificate border.</summary>
    public string? BackgroundImage { get; set; }

    /// <summary>Gets or sets the printer tray the report prefers (Auto, Manual, Cassette, Tractor…), offered first in the print dialog.</summary>
    public string? Tray { get; set; }

    /// <summary>Gets or sets whether the report prefers printing on both sides.</summary>
    public DuplexMode Duplex { get; set; }

    /// <summary>A copy of the page setup with another watermark.</summary>
    /// <param name="watermark">The watermark.</param>
    /// <returns>The copy.</returns>
    public PageSetup WithWatermark(string? watermark)
    {
        PageSetup copy = (PageSetup)MemberwiseClone();
        copy.Watermark = watermark;
        return copy;
    }
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

    /// <summary>Gets or sets the parameters this one's list depends on (cascading): their values are bound into <see cref="LookupQuery"/>.</summary>
    public List<string> DependsOn { get; set; } = [];
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

/// <summary>A record sort key (brief §13.1 "sorting separate from grouping"): an expression and its direction.</summary>
public sealed class ReportSort
{
    /// <summary>The field or expression sorted on.</summary>
    public string Expression { get; set; } = string.Empty;

    /// <summary>Whether the sort is descending.</summary>
    public bool Descending { get; set; }
}

/// <summary>A grouping level for a report (brief §13.1).</summary>
public sealed class ReportGroup
{
    /// <summary>The field or expression to group on.</summary>
    public string Expression { get; set; } = string.Empty;

    /// <summary>Whether to sort descending.</summary>
    public bool Descending { get; set; }

    /// <summary>Gets or sets how many groups to keep at this level within each parent group, the largest first (null: all of them).</summary>
    public int? TopCount { get; set; }

    /// <summary>Gets or sets the field whose total ranks the groups for <see cref="TopCount"/> (blank: the number of rows).</summary>
    public string? TopBy { get; set; }

    /// <summary>Gets or sets the group the rest are gathered into, printed after the top groups (blank: the rest are left out).</summary>
    public string? OthersLabel { get; set; }
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

    /// <summary>Gets or sets a value indicating whether a group header stays on the page with the band after it (never left alone at a page's foot).</summary>
    public bool KeepTogether { get; set; }

    /// <summary>Gets or sets a value indicating whether a new page starts before the band.</summary>
    public bool PageBreakBefore { get; set; }

    /// <summary>Gets or sets a value indicating whether a new page starts after the band.</summary>
    public bool PageBreakAfter { get; set; }

    /// <summary>An expression that, when true, suppresses the whole band.</summary>
    public string? SuppressWhen { get; set; }

    /// <summary>Gets or sets the background every second detail row is shaded with (#RRGGBB; blank: no banding).</summary>
    public string? AlternateBackground { get; set; }

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

    /// <summary>A permission the user must hold to run the report (on top of the general reporting right and the module's reports right).</summary>
    public string? RequiredPermission { get; set; }

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

    /// <summary>How the records are sorted, apart from grouping: applied before grouping, so within each group the rows keep this order.</summary>
    public List<ReportSort> Sorts { get; set; } = [];

    /// <summary>The bands, in render order.</summary>
    public List<ReportBand> Bands { get; set; } = [];

    /// <summary>Gets or sets a cross-tab layout: when set, the rows are pivoted and the header and detail bands are generated from the columns found.</summary>
    public CrossTab? CrossTab { get; set; }

    /// <summary>Gets or sets label translations: culture (e.g. "pt" or "pt-MZ") → element name → text.</summary>
    public Dictionary<string, Dictionary<string, string>> LabelTranslations { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns the first band of a given type, or null.</summary>
    /// <param name="type">The band type.</param>
    /// <returns>The band, or null.</returns>
    public ReportBand? BandOfType(BandType type) => Bands.FirstOrDefault(b => b.Type == type);
}

/// <summary>A cross-tab (pivot): one output row per row-field value, one column per column-field value, each cell the aggregate of the value field.</summary>
public sealed class CrossTab
{
    /// <summary>Gets or sets the field down the side.</summary>
    public string RowField { get; set; } = string.Empty;

    /// <summary>Gets or sets the field across the top.</summary>
    public string ColumnField { get; set; } = string.Empty;

    /// <summary>Gets or sets the field summarised in the cells.</summary>
    public string ValueField { get; set; } = string.Empty;

    /// <summary>Gets or sets how cells summarise (Sum, Count, Average, Min, Max).</summary>
    public AggregateFunction Function { get; set; }

    /// <summary>Gets or sets the number format of the cells.</summary>
    public string? Format { get; set; } = "N2";

    /// <summary>Gets or sets a value indicating whether a total column is added.</summary>
    public bool RowTotals { get; set; } = true;
}
