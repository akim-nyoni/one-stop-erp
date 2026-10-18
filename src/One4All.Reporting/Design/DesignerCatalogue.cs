using One4All.Reporting.Model;

namespace One4All.Reporting.Design;

/// <summary>A tool of the designer's toolbox: the pointer, or an element kind placed where the user clicks.</summary>
/// <param name="Key">The key.</param>
/// <param name="Caption">The caption on the toolbar and the Toolbox menu.</param>
/// <param name="Hint">What the tool does.</param>
/// <param name="Kind">The element kind placed, or null for the pointer.</param>
public sealed record DesignerTool(string Key, string Caption, string Hint, ElementKind? Kind);

/// <summary>A task of the Dynamic Help pane ("What would you like to do?").</summary>
/// <param name="Key">The key the designer acts on.</param>
/// <param name="Title">The link text.</param>
public sealed record DesignerTask(string Key, string Title);

/// <summary>
/// What the report designer shows around the sections, laid out as the Sage Report Designer is: the toolbox tools, the Dynamic Help tasks, the
/// property grid's categories (Appearance, Behaviour, Data, Design, Layout) with a description of each property for the pane under the grid, and the
/// numbered section captions ("1. Page Header", "4. CustomerCode header").
/// </summary>
public static class DesignerCatalogue
{
    /// <summary>The toolbox, the pointer first.</summary>
    public static IReadOnlyList<DesignerTool> Tools { get; } =
    [
        new("Pointer", "Pointer", "Select, move and resize elements (Esc)", null),
        new("Text", "Add Text", "Place a text label where you click", ElementKind.Label),
        new("Line", "Draw Line", "Place a line where you click", ElementKind.Line),
        new("Box", "Draw Box", "Place a box (a bordered rectangle) where you click; set CornerRadius for round corners", ElementKind.Rectangle),
        new("Ellipse", "Draw Ellipse", "Place an ellipse (a circle when square) where you click", ElementKind.Ellipse),
        new("Image", "Add Image / Logo", "Place a picture from a file where you click", ElementKind.Image),
        new("Logo", "Add Company Logo", "Place the company logo (set under System Manager) where you click", ElementKind.Image),
        new("DataField", "Add Data Field", "Place the variable selected in the Variables pane where you click", ElementKind.Field),
        new("Barcode", "Add Barcode", "Place a barcode of the selected variable where you click (Code 128; set BarcodeSymbology for QR, Code 39, EAN-13 or PDF417)", ElementKind.Barcode),
        new("Expression", "Add Expression", "Place the expression from the expression editor where you click", ElementKind.Expression),
        new("SubReport", "Add Subreport", "Place another report run inside this section where you click (set SubReportCode and SubReportLinks)", ElementKind.SubReport),
        new("Checkbox", "Add Tick Box", "Place a tick box of the selected variable where you click", ElementKind.Checkbox),
        new("Signature", "Add Signature Line", "Place a captioned signature line where you click", ElementKind.Signature),
        new("Chart", "Add Chart", "Place a chart of the selected variable where you click (set its category field in the properties)", ElementKind.Chart),
        new("RichText", "Add Rich Text", "Place text with **bold**, *italic*, __underline__, line breaks and {Field} values where you click", ElementKind.RichText),
    ];

    /// <summary>The Dynamic Help tasks.</summary>
    public static IReadOnlyList<DesignerTask> HelpTasks { get; } =
    [
        new("ReportInfo", "Set the report name or description"),
        new("AddText", "Add text or data field"),
        new("AddImage", "Add an image or logo"),
        new("AddLine", "Add a line or box"),
        new("Restrict", "Restrict the information included in the report"),
        new("Sort", "Sort information in the report"),
        new("Email", "Set up email"),
        new("PageSetup", "Set up the page, paper and margins"),
    ];

    /// <summary>The property grid's categories, in the order shown.</summary>
    public static IReadOnlyList<string> Categories { get; } = ["Appearance", "Behaviour", "Data", "Design", "Layout"];

    /// <summary>The category of a property.</summary>
    /// <param name="property">The property.</param>
    /// <returns>The category.</returns>
    public static string Category(string property) => property switch
    {
        "X" or "Y" or "Width" or "Height" or "Rotation" or "Padding" or "AnchorRight" or "AnchorBottom" or "CornerRadius" => "Layout",
        "FontFamily" or "FontSize" or "Bold" or "Italic" or "Underline" or "Foreground" or "Background" or "BackgroundGradient" or "GradientVertical" or "HAlign" or "VAlign"
            or "BorderThickness" or "BorderColor" or "BorderLeft" or "BorderTop" or "BorderRight" or "BorderBottom" or "WordWrap" or "AutoShrink" or "Format" or "NullText" => "Appearance",
        "SuppressWhen" or "DrillTarget" or "DrillField" or "Tooltip" or "GroupName" => "Behaviour",
        "Name" => "Design",
        _ => "Data",
    };

    /// <summary>What a property does, for the pane under the property grid.</summary>
    /// <param name="property">The property.</param>
    /// <returns>The description.</returns>
    public static string Describe(string property) => Descriptions.TryGetValue(property, out string? text) ? text : $"The {property} of the element.";

    /// <summary>The numbered caption of a section, as the designer shows it above the section.</summary>
    /// <param name="number">The section's number from the top (1 first).</param>
    /// <param name="band">The section.</param>
    /// <param name="definition">The report, for the group expressions.</param>
    /// <returns>The caption, e.g. "1. Page Header" or "3. CustomerCode header".</returns>
    public static string BandCaption(int number, ReportBand band, ReportDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(band);
        ArgumentNullException.ThrowIfNull(definition);
        string title = band.Type switch
        {
            BandType.ReportHeader => "Report Header",
            BandType.PageHeader => "Page Header",
            BandType.GroupHeader => GroupName(band, definition) + " header",
            BandType.Detail => "Details",
            BandType.GroupFooter => GroupName(band, definition) + " footer",
            BandType.PageFooter => "Page Footer",
            BandType.ReportFooter => "Report Footer",
            _ => band.Type.ToString(),
        };
        return $"{number}. {title}";
    }

    private static string GroupName(ReportBand band, ReportDefinition definition)
    {
        int index = Math.Max(1, band.GroupLevel) - 1;
        return index < definition.Groups.Count && definition.Groups[index].Expression.Length > 0 ? definition.Groups[index].Expression : $"Group {Math.Max(1, band.GroupLevel)}";
    }

    private static readonly Dictionary<string, string> Descriptions = new(StringComparer.Ordinal)
    {
        ["Name"] = "The name of the element; it is what the Report Explorer and the alignment tools refer to.",
        ["Text"] = "The text of a label, rich text or signature caption. Rich text takes **bold**, *italic*, __underline__ and {Field} values.",
        ["FieldName"] = "The data field the element prints.",
        ["Expression"] = "An expression evaluated per record, e.g. Quantity * UnitPrice, Upper(Name) or PageNumber.",
        ["Format"] = "A .NET format for numbers and dates, e.g. N2, #,##0.00, dd MMM yyyy.",
        ["Aggregate"] = "The total kept by the element: Sum, Count, Average, Min, Max, First, Last.",
        ["AggregateField"] = "The field the aggregate is taken over.",
        ["AggregateScope"] = "Where the aggregate resets: the report, the page, or the group.",
        ["X"] = "Left edge, in points from the left of the printable area.",
        ["Y"] = "Top edge, in points from the top of its section.",
        ["Width"] = "Width in points (72 points to an inch, 2.83 to a millimetre).",
        ["Height"] = "Height in points.",
        ["FontFamily"] = "The font. Use a monospaced font (Consolas, Courier New) for the Line field of a text-backed document.",
        ["FontSize"] = "The font size in points.",
        ["Bold"] = "True prints the text bold.",
        ["Italic"] = "True prints the text italic.",
        ["Underline"] = "True underlines the text.",
        ["Foreground"] = "The text colour, as #RRGGBB.",
        ["Background"] = "The fill colour, as #RRGGBB; blank for none.",
        ["BackgroundGradient"] = "A second colour the fill fades to, as #RRGGBB; blank for a plain fill.",
        ["GradientVertical"] = "True fades the fill top to bottom instead of left to right.",
        ["HAlign"] = "How the text sits across the element: Left, Center or Right.",
        ["VAlign"] = "How the text sits down the element: Top, Middle or Bottom.",
        ["BorderThickness"] = "The border width in points on every side; 0 for no border.",
        ["BorderColor"] = "The border colour, as #RRGGBB.",
        ["BorderLeft"] = "A width for the left border alone; blank uses BorderThickness.",
        ["BorderTop"] = "A width for the top border alone; blank uses BorderThickness.",
        ["BorderRight"] = "A width for the right border alone; blank uses BorderThickness.",
        ["BorderBottom"] = "A width for the bottom border alone; blank uses BorderThickness.",
        ["Padding"] = "The space between the border and the text, in points.",
        ["WordWrap"] = "True wraps long text onto more lines (the section grows when it can).",
        ["AutoShrink"] = "True shrinks the font so the text fits the element.",
        ["Rotation"] = "Degrees to turn the text clockwise, any angle (90, 180 and 270 turn the box with it).",
        ["CornerRadius"] = "How round a box's corners are, in points; 0 for square corners.",
        ["NullText"] = "What to print when the value is empty.",
        ["SuppressWhen"] = "An expression; when it is true the element is not printed, e.g. Balance = 0.",
        ["DrillTarget"] = "What a click opens in the viewer: Document, Account, Customer, Supplier, Item, Stand, Payslip … (blank for no drill-through).",
        ["DrillField"] = "The field whose value identifies the record the click opens.",
        ["ChartCategoryField"] = "The field that names each bar or slice of a chart.",
        ["ChartColor"] = "The chart's colour, as #RRGGBB.",
        ["ChartType"] = "Bar, Column, Line or Pie.",
        ["ChartSeriesField"] = "A field that splits the chart into series.",
        ["BarcodeSymbology"] = "The barcode kind: Code128, Code39, EAN13, QR or PDF417.",
        ["Tooltip"] = "Text shown when the pointer rests on the element in the viewer.",
        ["ImageSource"] = "A file path or http(s) URL the picture is read from at print time (instead of a picture kept in the layout).",
        ["GroupName"] = "Elements with the same group name move together; blank for none.",
        ["AnchorRight"] = "True keeps the element at the right of its column when columns are laid out.",
        ["AnchorBottom"] = "True keeps the element at the bottom of its section when the section grows.",
        ["SubReportCode"] = "The code of the report run inside this section.",
        ["SubReportLinks"] = "How the subreport's parameters take this report's fields: Parameter=Field; Other=Field.",
    };
}
