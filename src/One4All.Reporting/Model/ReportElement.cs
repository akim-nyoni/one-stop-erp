namespace One4All.Reporting.Model;

/// <summary>A conditional-formatting rule: when <see cref="Condition"/> evaluates true, apply the style.</summary>
public sealed class ConditionalFormat
{
    /// <summary>The boolean expression that triggers the format.</summary>
    public string Condition { get; set; } = string.Empty;

    /// <summary>Foreground colour to apply (hex), or null to leave unchanged.</summary>
    public string? Foreground { get; set; }

    /// <summary>Background colour to apply (hex), or null.</summary>
    public string? Background { get; set; }

    /// <summary>Whether to bold the text when the condition holds.</summary>
    public bool Bold { get; set; }
}

/// <summary>
/// One element on a report band (brief §13.1). A single flexible type (rather than a deep class
/// hierarchy) keyed by <see cref="Kind"/> keeps the definition easy to serialize as <c>.mrpt</c> JSON and
/// to edit in the property grid. Only the properties relevant to a kind are used.
/// </summary>
public sealed class ReportElement
{
    /// <summary>The element kind.</summary>
    public ElementKind Kind { get; set; }

    /// <summary>A unique element name within the report.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>X position within the band, in points.</summary>
    public double X { get; set; }

    /// <summary>Y position within the band, in points.</summary>
    public double Y { get; set; }

    /// <summary>Width in points.</summary>
    public double Width { get; set; } = 100;

    /// <summary>Height in points.</summary>
    public double Height { get; set; } = 16;

    /// <summary>Static text for a <see cref="ElementKind.Label"/>.</summary>
    public string? Text { get; set; }

    /// <summary>The bound field name for a <see cref="ElementKind.Field"/>.</summary>
    public string? FieldName { get; set; }

    /// <summary>
    /// Gets or sets what clicking the value opens (Journal, Account, Customer, Supplier, Item...), with <see cref="DrillField"/> giving the key; when
    /// empty, a field named like a journal number, account, customer, supplier or item code drills to that record by itself.
    /// </summary>
    public string? DrillTarget { get; set; }

    /// <summary>Gets or sets the field whose value is the drill key (default: the element's own field).</summary>
    public string? DrillField { get; set; }

    /// <summary>Gets or sets a chart's category field (the bars' labels).</summary>
    public string? ChartCategoryField { get; set; }

    /// <summary>Gets or sets a chart's bar colour.</summary>
    public string ChartColor { get; set; } = "#2E7D32";

    /// <summary>Gets or sets the kind of chart.</summary>
    public ChartType ChartType { get; set; }

    /// <summary>Gets or sets a chart's series field: one bar / line / stack segment per value (blank: one series).</summary>
    public string? ChartSeriesField { get; set; }

    /// <summary>Gets or sets the field a combination chart draws as a line over the columns (on its own scale).</summary>
    public string? ChartLineField { get; set; }

    /// <summary>The expression for a <see cref="ElementKind.Expression"/> element.</summary>
    public string? Expression { get; set; }

    /// <summary>The aggregate function for an <see cref="ElementKind.Aggregate"/> element.</summary>
    public AggregateFunction Aggregate { get; set; }

    /// <summary>The scope an aggregate resets at.</summary>
    public AggregateScope AggregateScope { get; set; }

    /// <summary>The field an aggregate operates on.</summary>
    public string? AggregateField { get; set; }

    /// <summary>A .NET format string (e.g. <c>N2</c>, <c>yyyy-MM-dd</c>, <c>C</c>).</summary>
    public string? Format { get; set; }

    /// <summary>Font family.</summary>
    public string FontFamily { get; set; } = "Segoe UI";

    /// <summary>Font size in points.</summary>
    public double FontSize { get; set; } = 9;

    /// <summary>Bold.</summary>
    public bool Bold { get; set; }

    /// <summary>Italic.</summary>
    public bool Italic { get; set; }

    /// <summary>Underline.</summary>
    public bool Underline { get; set; }

    /// <summary>Foreground colour (hex).</summary>
    public string Foreground { get; set; } = "#000000";

    /// <summary>Background colour (hex), or null for transparent.</summary>
    public string? Background { get; set; }

    /// <summary>Horizontal alignment.</summary>
    public ReportHAlign HAlign { get; set; }

    /// <summary>Vertical alignment.</summary>
    public ReportVAlign VAlign { get; set; }

    /// <summary>Border thickness in points (0 = none). Applies to a rectangle or a bordered field.</summary>
    public double BorderThickness { get; set; }

    /// <summary>Border colour (hex).</summary>
    public string BorderColor { get; set; } = "#000000";

    /// <summary>Gets or sets the left border width in points (null: the border thickness).</summary>
    public double? BorderLeft { get; set; }

    /// <summary>Gets or sets the top border width in points (null: the border thickness).</summary>
    public double? BorderTop { get; set; }

    /// <summary>Gets or sets the right border width in points (null: the border thickness).</summary>
    public double? BorderRight { get; set; }

    /// <summary>Gets or sets the bottom border width in points (null: the border thickness).</summary>
    public double? BorderBottom { get; set; }

    /// <summary>Gets or sets the space between the border and the text, in points.</summary>
    public double Padding { get; set; } = 1;

    /// <summary>Gets or sets a value indicating whether long text wraps onto further lines (otherwise one line, cut short).</summary>
    public bool WordWrap { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the font shrinks (to 5pt at most) until the text fits the box.</summary>
    public bool AutoShrink { get; set; }

    /// <summary>Gets or sets the text's rotation in degrees, clockwise (0, 90, 180 or 270).</summary>
    public int Rotation { get; set; }

    /// <summary>Gets or sets the text shown when a field or expression has no value.</summary>
    public string? NullText { get; set; }

    /// <summary>An expression that, when true, suppresses (hides) this element.</summary>
    public string? SuppressWhen { get; set; }

    /// <summary>Image data as a base64 string or a data URI, for an <see cref="ElementKind.Image"/>.</summary>
    public string? ImageData { get; set; }

    /// <summary>The barcode symbology (e.g. Code128, QR) for a <see cref="ElementKind.Barcode"/>.</summary>
    public string? BarcodeSymbology { get; set; }

    /// <summary>Conditional-formatting rules applied in order.</summary>
    public List<ConditionalFormat> ConditionalFormats { get; set; } = [];

    /// <summary>For a sub-report: the code of the report run inside this element.</summary>
    public string? SubReportCode { get; set; }

    /// <summary>For a sub-report: which of the child's parameters take which parent field (or parent parameter).</summary>
    public List<SubReportLink> SubReportLinks { get; set; } = [];
}

/// <summary>Links one parameter of a sub-report to a field of the parent row (or a parent parameter of that name).</summary>
public sealed class SubReportLink
{
    /// <summary>The child report's parameter.</summary>
    public string ChildParameter { get; set; } = string.Empty;

    /// <summary>The parent row's field (or parameter) whose value it takes.</summary>
    public string ParentField { get; set; } = string.Empty;
}
