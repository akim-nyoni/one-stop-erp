using System.Globalization;
using One4All.Reporting.Model;

namespace One4All.Reporting.Engine;

/// <summary>Median and standard deviation for report aggregates.</summary>
public static class Statistics
{
    /// <summary>The middle value (the mean of the two middle values for an even count; 0 when empty).</summary>
    /// <param name="values">The values.</param>
    /// <returns>The median.</returns>
    public static decimal Median(IReadOnlyCollection<decimal> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
        {
            return 0m;
        }

        List<decimal> sorted = [.. values.Order()];
        int middle = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2m;
    }

    /// <summary>The sample standard deviation (0 for fewer than two values), to four decimals.</summary>
    /// <param name="values">The values.</param>
    /// <returns>The standard deviation.</returns>
    public static decimal SampleStdDev(IReadOnlyCollection<decimal> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count < 2)
        {
            return 0m;
        }

        decimal mean = values.Average();
        double variance = (double)values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1);
        return Math.Round((decimal)Math.Sqrt(variance), 4);
    }
}

/// <summary>Pivots rows for a cross-tab and generates its header and detail bands.</summary>
public static class CrossTabs
{
    /// <summary>The total column's name.</summary>
    public const string TotalColumn = "Total";

    /// <summary>Pivots the input: one row per row-field value (in order), one column per column-field value (sorted), and an optional total; the
    /// definition's page header and detail bands are replaced by generated ones (other bands are kept).</summary>
    /// <param name="input">The report input.</param>
    /// <param name="crossTab">The cross-tab.</param>
    /// <returns>The pivoted input.</returns>
    public static ReportInput Pivot(ReportInput input, CrossTab crossTab)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(crossTab);
        string Key(IReadOnlyDictionary<string, object?> row, string field) => Convert.ToString(row.GetValueOrDefault(field), CultureInfo.InvariantCulture) ?? string.Empty;
        List<string> columns = [.. input.Rows.Select(r => Key(r, crossTab.ColumnField)).Distinct().Order(StringComparer.Ordinal)];
        List<IReadOnlyDictionary<string, object?>> pivoted = [];
        foreach (IGrouping<string, IReadOnlyDictionary<string, object?>> line in input.Rows.GroupBy(r => Key(r, crossTab.RowField)))
        {
            Dictionary<string, object?> row = new(StringComparer.Ordinal) { [crossTab.RowField] = line.Key };
            foreach (string column in columns)
            {
                row[column] = Summarise(line.Where(r => Key(r, crossTab.ColumnField) == column), crossTab);
            }

            if (crossTab.RowTotals)
            {
                row[TotalColumn] = Summarise(line, crossTab);
            }

            pivoted.Add(row);
        }

        ReportDefinition definition = input.Definition;
        List<string> fields = [crossTab.RowField, .. columns, .. crossTab.RowTotals ? [TotalColumn] : Array.Empty<string>()];
        double printable = ReportLayout.PaperSize(definition.Page).Width - definition.Page.MarginLeft - definition.Page.MarginRight;
        double width = Math.Max(30, Math.Floor(printable / fields.Count));
        ReportBand header = new() { Type = BandType.PageHeader, Height = 18 };
        ReportBand detail = new() { Type = BandType.Detail, Height = 16 };
        for (int i = 0; i < fields.Count; i++)
        {
            bool label = i == 0;
            header.Elements.Add(new ReportElement { Kind = ElementKind.Label, Name = $"H{i}", Text = label ? fields[i] : fields[i].Length == 0 ? "(blank)" : fields[i], X = i * width, Width = width, Height = 16,
                Bold = true, HAlign = label ? ReportHAlign.Left : ReportHAlign.Right });
            detail.Elements.Add(new ReportElement { Kind = ElementKind.Field, Name = $"D{i}", FieldName = fields[i], X = i * width, Width = width, Height = 14,
                HAlign = label ? ReportHAlign.Left : ReportHAlign.Right, Format = label ? null : crossTab.Format, Bold = !label && fields[i] == TotalColumn && crossTab.RowTotals });
        }

        ReportDefinition pivotedDefinition = ReportDefinitionSerializer.Deserialize(ReportDefinitionSerializer.Serialize(definition));
        pivotedDefinition.CrossTab = null;
        pivotedDefinition.Groups.Clear();
        pivotedDefinition.Bands.RemoveAll(b => b.Type is BandType.PageHeader or BandType.Detail or BandType.GroupHeader or BandType.GroupFooter);
        pivotedDefinition.Bands.Add(header);
        pivotedDefinition.Bands.Add(detail);
        return new ReportInput
        {
            Definition = pivotedDefinition, Rows = pivoted, Parameters = input.Parameters, UserName = input.UserName, CompanyName = input.CompanyName, PrintDate = input.PrintDate, ToBase = input.ToBase,
        };
    }

    private static decimal Summarise(IEnumerable<IReadOnlyDictionary<string, object?>> rows, CrossTab crossTab)
    {
        List<decimal> values = [.. rows.Select(r => r.GetValueOrDefault(crossTab.ValueField)).Where(v => v is not null)
            .Select(v => decimal.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal d) ? d : 0m)];
        return crossTab.Function switch
        {
            AggregateFunction.Count => rows.Count(),
            AggregateFunction.Average => values.Count == 0 ? 0m : Math.Round(values.Average(), 4),
            AggregateFunction.Min => values.Count == 0 ? 0m : values.Min(),
            AggregateFunction.Max => values.Count == 0 ? 0m : values.Max(),
            _ => values.Sum(),
        };
    }
}

/// <summary>Draws a bar chart as cells: a bar (rectangle) per category scaled to the largest value, with its label and value.</summary>
public static class Charts
{
    /// <summary>The cells of a chart element over its scope (categories in first-seen order; values summed per category).</summary>
    /// <param name="element">The chart element (its box, value field in AggregateField or FieldName, category in ChartCategoryField).</param>
    /// <param name="scope">The rows.</param>
    /// <returns>The cells.</returns>
    public static IReadOnlyList<RenderCell> Bars(ReportElement element, IReadOnlyList<IReadOnlyDictionary<string, object?>> scope)
    {
        ArgumentNullException.ThrowIfNull(element);
        string valueField = element.AggregateField ?? element.FieldName ?? string.Empty;
        string categoryField = element.ChartCategoryField ?? string.Empty;
        List<(string Category, decimal Value)> bars = [.. scope
            .GroupBy(r => Convert.ToString(r.GetValueOrDefault(categoryField), CultureInfo.InvariantCulture) ?? string.Empty)
            .Select(g => (g.Key, g.Sum(r => decimal.TryParse(Convert.ToString(r.GetValueOrDefault(valueField), CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal d) ? d : 0m)))
            .Take(30)];
        List<RenderCell> cells = [];
        if (bars.Count == 0)
        {
            return cells;
        }

        decimal max = Math.Max(bars.Max(b => Math.Abs(b.Value)), 0.0001m);
        double rowHeight = Math.Max(8, element.Height / bars.Count);
        double labelWidth = Math.Min(element.Width * 0.3, 120), valueWidth = Math.Min(element.Width * 0.2, 80);
        double barSpace = Math.Max(10, element.Width - labelWidth - valueWidth - 4);
        for (int i = 0; i < bars.Count; i++)
        {
            double y = element.Y + (i * rowHeight);
            double length = Math.Max(1, barSpace * (double)(Math.Abs(bars[i].Value) / max));
            cells.Add(new RenderCell { Kind = ElementKind.Label, Text = bars[i].Category, X = element.X, Y = y, Width = labelWidth, Height = rowHeight, FontSize = Math.Min(element.FontSize, rowHeight * 0.7) });
            cells.Add(new RenderCell { Kind = ElementKind.Rectangle, X = element.X + labelWidth + 2, Y = y + (rowHeight * 0.15), Width = length, Height = rowHeight * 0.7,
                Background = bars[i].Value < 0 ? "#C62828" : element.ChartColor, BorderColor = bars[i].Value < 0 ? "#C62828" : element.ChartColor });
            cells.Add(new RenderCell { Kind = ElementKind.Label, Text = bars[i].Value.ToString(element.Format ?? "N0", CultureInfo.InvariantCulture), X = element.X + labelWidth + 4 + barSpace,
                Y = y, Width = valueWidth, Height = rowHeight, FontSize = Math.Min(element.FontSize, rowHeight * 0.7), HAlign = ReportHAlign.Right });
        }

        return cells;
    }
}

/// <summary>The data command a report's provider is chosen by.</summary>
public static class ReportCommands
{
    /// <summary>The command for a definition: SQL, view and function sources are prefixed (sql:, view:, function:); in-memory ones are their name.</summary>
    /// <param name="definition">The definition.</param>
    /// <returns>The command.</returns>
    public static string For(ReportDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ReportDataSource? source = definition.DataSources.FirstOrDefault();
        return source is null ? definition.Code : source.Kind switch
        {
            DataSourceKind.Sql => "sql:" + source.Command,
            DataSourceKind.View => "view:" + source.Command,
            DataSourceKind.Function => "function:" + source.Command,
            _ => source.Command,
        };
    }
}
