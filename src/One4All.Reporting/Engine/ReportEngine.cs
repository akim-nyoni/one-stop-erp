using One4All.Reporting.Expressions;
using One4All.Reporting.Model;

namespace One4All.Reporting.Engine;

/// <summary>
/// The report engine: turns a <see cref="ReportDefinition"/> plus data into a resolved <see cref="RenderModel"/>
/// that any renderer can output (brief §13.3). Handles multi-level grouping, aggregates scoped to
/// group/report, expression fields, conditional formatting and element suppression.
/// </summary>
public sealed class ReportEngine
{
    private static readonly Dictionary<string, object?> EmptyRow = new();
    private readonly ReportExpressionEngine _expressions = new();

    /// <summary>Builds the render model from the input.</summary>
    /// <param name="input">The report input (definition + data + context).</param>
    /// <returns>The resolved render model.</returns>
    public RenderModel Build(ReportInput input)
    {
        ReportDefinition def = input.Definition;
        RenderModel model = new() { ReportName = def.Name, Page = def.Page };

        model.ReportHeader = ResolveOptional(def.BandOfType(BandType.ReportHeader), input, null, input.Rows);
        model.PageHeader = ResolveOptional(def.BandOfType(BandType.PageHeader), input, null, input.Rows);
        model.PageFooter = ResolveOptional(def.BandOfType(BandType.PageFooter), input, null, input.Rows);
        model.ReportFooter = ResolveOptional(def.BandOfType(BandType.ReportFooter), input, null, input.Rows);

        ReportBand? detail = def.BandOfType(BandType.Detail);

        if (def.Groups.Count == 0)
        {
            if (detail is not null)
            {
                foreach (IReadOnlyDictionary<string, object?> row in input.Rows)
                {
                    model.Body.Add(ResolveBand(detail, input, row, input.Rows));
                }
            }

            return model;
        }

        BuildGrouped(def, input, detail, model);
        return model;
    }

    private void BuildGrouped(ReportDefinition def, ReportInput input, ReportBand? detail, RenderModel model)
    {
        // Compute a key per level for each row, then order by the concatenated keys.
        List<(IReadOnlyDictionary<string, object?> Row, string[] Keys)> keyed = input.Rows
            .Select(row => (Row: row, Keys: def.Groups.Select(g => ValueToString(Evaluate(g.Expression, input, row))).ToArray()))
            .OrderBy(x => string.Join('', x.Keys), StringComparer.Ordinal)
            .ToList();

        List<ReportBand> groupHeaders = def.Bands.Where(b => b.Type == BandType.GroupHeader).OrderBy(b => b.GroupLevel).ToList();
        List<ReportBand> groupFooters = def.Bands.Where(b => b.Type == BandType.GroupFooter).OrderByDescending(b => b.GroupLevel).ToList();

        string[]? current = null;
        for (int i = 0; i < keyed.Count; i++)
        {
            (IReadOnlyDictionary<string, object?> row, string[] keys) = keyed[i];

            int changedFrom = current is null ? 0 : FirstDifference(current, keys);
            if (current is not null)
            {
                // Close footers for levels that changed (deepest first).
                foreach (ReportBand footer in groupFooters.Where(f => f.GroupLevel >= changedFrom))
                {
                    IReadOnlyList<IReadOnlyDictionary<string, object?>> scope = ScopeFor(keyed, current, footer.GroupLevel);
                    model.Body.Add(ResolveBand(footer, input, row, scope));
                }
            }

            // Open headers for levels that changed (outermost first).
            foreach (ReportBand header in groupHeaders.Where(h => h.GroupLevel >= changedFrom))
            {
                model.Body.Add(ResolveBand(header, input, row, input.Rows));
            }

            if (detail is not null)
            {
                model.Body.Add(ResolveBand(detail, input, row, input.Rows));
            }

            current = keys;
        }

        // Close all remaining footers at the end.
        if (current is not null)
        {
            foreach (ReportBand footer in groupFooters)
            {
                IReadOnlyList<IReadOnlyDictionary<string, object?>> scope = ScopeFor(keyed, current, footer.GroupLevel);
                model.Body.Add(ResolveBand(footer, input, keyed[^1].Row, scope));
            }
        }
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> ScopeFor(
        List<(IReadOnlyDictionary<string, object?> Row, string[] Keys)> keyed, string[] current, int level) =>
        keyed.Where(x => KeysMatch(x.Keys, current, level)).Select(x => x.Row).ToList();

    private static bool KeysMatch(string[] a, string[] b, int level)
    {
        for (int i = 0; i <= level && i < a.Length && i < b.Length; i++)
        {
            if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static int FirstDifference(string[] a, string[] b)
    {
        for (int i = 0; i < a.Length && i < b.Length; i++)
        {
            if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
            {
                return i;
            }
        }

        return a.Length;
    }

    private RenderBand? ResolveOptional(ReportBand? band, ReportInput input, IReadOnlyDictionary<string, object?>? row, IReadOnlyList<IReadOnlyDictionary<string, object?>> scope) =>
        band is null ? null : ResolveBand(band, input, row, scope);

    private RenderBand ResolveBand(ReportBand band, ReportInput input, IReadOnlyDictionary<string, object?>? row, IReadOnlyList<IReadOnlyDictionary<string, object?>> scope)
    {
        RenderBand rendered = new() { Type = band.Type, Height = band.Height };
        ReportExpressionContext ctx = Context(input, row);

        foreach (ReportElement element in band.Elements)
        {
            if (!string.IsNullOrEmpty(element.SuppressWhen) && _expressions.EvaluateBool(element.SuppressWhen, ctx))
            {
                continue;
            }

            string text = ResolveText(element, ctx, scope);
            (string foreground, string? background, bool bold) = ApplyConditionalFormats(element, ctx);
            (string? drillTarget, string? drillKey) = DrillThrough.For(element, row);

            rendered.Cells.Add(new RenderCell
            {
                Kind = element.Kind,
                Text = text,
                X = element.X,
                Y = element.Y,
                Width = element.Width,
                Height = element.Height,
                FontFamily = element.FontFamily,
                FontSize = element.FontSize,
                Bold = bold,
                Italic = element.Italic,
                Underline = element.Underline,
                Foreground = foreground,
                Background = background,
                HAlign = element.HAlign,
                BorderThickness = element.BorderThickness,
                BorderColor = element.BorderColor,
                DrillTarget = drillTarget,
                DrillKey = drillKey,
            });
        }

        return rendered;
    }

    private string ResolveText(ReportElement element, ReportExpressionContext ctx, IReadOnlyList<IReadOnlyDictionary<string, object?>> scope) => element.Kind switch
    {
        ElementKind.Label => element.Text ?? string.Empty,
        ElementKind.Field => NumberToWords.Format(ctx.Fields.GetValueOrDefault(element.FieldName ?? string.Empty), element.Format),
        ElementKind.Expression => NumberToWords.Format(_expressions.Evaluate(element.Expression ?? string.Empty, ctx), element.Format),
        ElementKind.Aggregate => NumberToWords.Format(ComputeAggregate(element, scope), element.Format),
        _ => string.Empty,
    };

    private static decimal ComputeAggregate(ReportElement element, IReadOnlyList<IReadOnlyDictionary<string, object?>> scope)
    {
        string field = element.AggregateField ?? string.Empty;
        IEnumerable<decimal> values = scope
            .Select(r => r.GetValueOrDefault(field))
            .Where(v => v is not null)
            .Select(ToDecimal);

        return element.Aggregate switch
        {
            AggregateFunction.Sum => values.Sum(),
            AggregateFunction.Average => values.Any() ? values.Average() : 0m,
            AggregateFunction.Count => scope.Count,
            AggregateFunction.DistinctCount => scope.Select(r => r.GetValueOrDefault(field)?.ToString()).Distinct().Count(),
            AggregateFunction.Min => values.Any() ? values.Min() : 0m,
            AggregateFunction.Max => values.Any() ? values.Max() : 0m,
            _ => values.Sum(),
        };
    }

    private (string Foreground, string? Background, bool Bold) ApplyConditionalFormats(ReportElement element, ReportExpressionContext ctx)
    {
        string foreground = element.Foreground;
        string? background = element.Background;
        bool bold = element.Bold;

        foreach (ConditionalFormat rule in element.ConditionalFormats)
        {
            if (_expressions.EvaluateBool(rule.Condition, ctx))
            {
                foreground = rule.Foreground ?? foreground;
                background = rule.Background ?? background;
                bold = rule.Bold || bold;
            }
        }

        return (foreground, background, bold);
    }

    private object? Evaluate(string expression, ReportInput input, IReadOnlyDictionary<string, object?> row) =>
        _expressions.Evaluate(expression, Context(input, row));

    private static ReportExpressionContext Context(ReportInput input, IReadOnlyDictionary<string, object?>? row) => new()
    {
        Fields = row ?? EmptyRow,
        Parameters = input.Parameters,
        PrintDate = input.PrintDate,
        UserName = input.UserName,
        CompanyName = input.CompanyName,
        ToBase = input.ToBase,
    };

    private static decimal ToDecimal(object? value) => value switch
    {
        null => 0m,
        decimal d => d,
        int i => i,
        long l => l,
        double db => (decimal)db,
        _ => decimal.TryParse(value.ToString(), out decimal parsed) ? parsed : 0m,
    };

    private static string ValueToString(object? value) => value?.ToString() ?? string.Empty;
}
