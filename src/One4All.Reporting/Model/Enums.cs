namespace One4All.Reporting.Model;

/// <summary>Paper sizes supported by the report engine (brief §13.1 page setup).</summary>
public enum PaperSize
{
    /// <summary>ISO A4.</summary>
    A4 = 0,

    /// <summary>US Letter.</summary>
    Letter = 1,

    /// <summary>US Legal.</summary>
    Legal = 2,

    /// <summary>ISO A5.</summary>
    A5 = 3,

    /// <summary>58 mm thermal receipt roll.</summary>
    Receipt58 = 10,

    /// <summary>80 mm thermal receipt roll.</summary>
    Receipt80 = 11,

    /// <summary>A custom size defined by width/height on the page setup.</summary>
    Custom = 99,
}

/// <summary>Page orientation.</summary>
public enum ReportOrientation
{
    /// <summary>Portrait.</summary>
    Portrait = 0,

    /// <summary>Landscape.</summary>
    Landscape = 1,
}

/// <summary>The bands of a banded report (brief §13.1).</summary>
public enum BandType
{
    /// <summary>Printed once at the very start of the report.</summary>
    ReportHeader = 0,

    /// <summary>Printed at the top of every page.</summary>
    PageHeader = 1,

    /// <summary>Printed at the start of each group.</summary>
    GroupHeader = 2,

    /// <summary>Printed once per data row.</summary>
    Detail = 3,

    /// <summary>Printed at the end of each group.</summary>
    GroupFooter = 4,

    /// <summary>Printed at the bottom of every page.</summary>
    PageFooter = 5,

    /// <summary>Printed once at the very end of the report.</summary>
    ReportFooter = 6,

    /// <summary>An overlay/watermark band.</summary>
    Overlay = 7,
}

/// <summary>The kinds of element that can be placed on a band (brief §13.1).</summary>
public enum ElementKind
{
    /// <summary>Static text label.</summary>
    Label = 0,

    /// <summary>A bound data field.</summary>
    Field = 1,

    /// <summary>An expression-evaluated field.</summary>
    Expression = 2,

    /// <summary>An aggregate (sum/avg/count/…) value.</summary>
    Aggregate = 3,

    /// <summary>A horizontal or vertical line.</summary>
    Line = 4,

    /// <summary>A rectangle/box.</summary>
    Rectangle = 5,

    /// <summary>An image.</summary>
    Image = 6,

    /// <summary>A barcode/QR.</summary>
    Barcode = 7,

    /// <summary>A tick box showing a true / false field (☑ / ☐).</summary>
    Checkbox = 8,

    /// <summary>A signature block: a line to sign on with the caption (its text) beneath.</summary>
    Signature = 9,

    /// <summary>A bar chart of a value field by a category field over the band's scope (drawn as bars and labels, so every renderer shows it).</summary>
    Chart = 10,

    /// <summary>Text with **bold**, *italic*, __underline__, line breaks and {Field} placeholders.</summary>
    RichText = 11,

    /// <summary>Another report run inside this one, its parameters linked from the parent row.</summary>
    SubReport = 12,
}

/// <summary>Horizontal text alignment.</summary>
public enum ReportHAlign
{
    /// <summary>Left-aligned.</summary>
    Left = 0,

    /// <summary>Centre-aligned.</summary>
    Center = 1,

    /// <summary>Right-aligned (numbers).</summary>
    Right = 2,
}

/// <summary>Vertical text alignment.</summary>
public enum ReportVAlign
{
    /// <summary>Top.</summary>
    Top = 0,

    /// <summary>Middle.</summary>
    Middle = 1,

    /// <summary>Bottom.</summary>
    Bottom = 2,
}

/// <summary>The type of a report parameter.</summary>
public enum ParameterType
{
    /// <summary>Text.</summary>
    String = 0,

    /// <summary>Integer.</summary>
    Integer = 1,

    /// <summary>Decimal.</summary>
    Decimal = 2,

    /// <summary>Date.</summary>
    Date = 3,

    /// <summary>Boolean.</summary>
    Boolean = 4,
}

/// <summary>How a data source is obtained.</summary>
public enum DataSourceKind
{
    /// <summary>A raw SQL query.</summary>
    Sql = 0,

    /// <summary>A database view.</summary>
    View = 1,

    /// <summary>A stored function.</summary>
    Function = 2,

    /// <summary>An in-memory dataset supplied by the caller (e.g. a report DTO).</summary>
    InMemory = 3,
}

/// <summary>Aggregate functions (brief §13.1).</summary>
public enum AggregateFunction
{
    /// <summary>Sum.</summary>
    Sum = 0,

    /// <summary>Average.</summary>
    Average = 1,

    /// <summary>Count.</summary>
    Count = 2,

    /// <summary>Distinct count.</summary>
    DistinctCount = 3,

    /// <summary>Minimum.</summary>
    Min = 4,

    /// <summary>Maximum.</summary>
    Max = 5,

    /// <summary>Running total.</summary>
    RunningTotal = 6,

    /// <summary>The middle value.</summary>
    Median = 7,

    /// <summary>The sample standard deviation.</summary>
    StdDev = 8,

    /// <summary>The value as a percentage of the scope's total (on a detail row: the row; in a group footer: the group against the report).</summary>
    PercentOfTotal = 9,

    /// <summary>The row's rank by value within the scope, highest first.</summary>
    RankOf = 10,

    /// <summary>The first value in the scope (in report order).</summary>
    First = 11,

    /// <summary>The last value in the scope (in report order).</summary>
    Last = 12,
}

/// <summary>The scope an aggregate resets at.</summary>
public enum AggregateScope
{
    /// <summary>Whole report.</summary>
    Report = 0,

    /// <summary>Current group.</summary>
    Group = 1,

    /// <summary>Current page.</summary>
    Page = 2,

    /// <summary>Always the whole report, wherever the element sits (e.g. a grand total in a group footer); Report and Group follow the band.</summary>
    GrandTotal = 3,
}

/// <summary>Printing on one or both sides of the paper.</summary>
public enum DuplexMode
{
    /// <summary>One side.</summary>
    OneSided = 0,

    /// <summary>Both sides, turned on the long edge (book style).</summary>
    LongEdge = 1,

    /// <summary>Both sides, turned on the short edge (calendar style).</summary>
    ShortEdge = 2,
}

/// <summary>The kind of chart an element draws.</summary>
public enum ChartType
{
    /// <summary>Horizontal bars with their values (drawn as cells).</summary>
    Bar = 0,

    /// <summary>Vertical columns (grouped side by side per series).</summary>
    Column = 1,

    /// <summary>Columns with the series stacked.</summary>
    StackedColumn = 2,

    /// <summary>A line per series.</summary>
    Line = 3,

    /// <summary>A line per series with the area beneath it shaded.</summary>
    Area = 4,

    /// <summary>A pie of the categories.</summary>
    Pie = 5,

    /// <summary>A pie with a hole.</summary>
    Donut = 6,
}
