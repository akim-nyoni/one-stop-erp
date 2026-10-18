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
    private int _rowNumber;
    private int _groupRowNumber;

    /// <summary>Builds the render model from the input.</summary>
    /// <param name="input">The report input (definition + data + context).</param>
    /// <returns>The resolved render model.</returns>
    public RenderModel Build(ReportInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        // A sub-report builds through here too: it numbers its own rows and hands the parent's numbering back when done.
        (int rowNumber, int groupRowNumber) = (_rowNumber, _groupRowNumber);
        _rowNumber = _groupRowNumber = 0;
        try
        {
            return BuildModel(input);
        }
        finally
        {
            (_rowNumber, _groupRowNumber) = (rowNumber, groupRowNumber);
        }
    }

    private RenderModel BuildModel(ReportInput input)
    {
        if (input.Definition.CrossTab is CrossTab crossTab)
        {
            input = CrossTabs.Pivot(input, crossTab);
        }

        ReportDefinition def = input.Definition;
        input = Sorted(def, input);
        RenderModel model = new() { ReportName = def.Name, Page = ResolvePage(def, input), RightToLeft = input.Culture?.TextInfo.IsRightToLeft ?? false };

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
                    AddDetail(model, def, detail, input, row);
                }
            }

            return ReportHouseStyles.Apply(Columns.Arrange(model), input);
        }

        BuildGrouped(def, input, detail, model);
        return ReportHouseStyles.Apply(Columns.Arrange(model), input);
    }

    // The rows in the definition's sort order (brief §13.1: sorting apart from grouping); a stable sort, so equal keys keep the data's order and
    // grouping afterwards keeps this order inside each group.
    private ReportInput Sorted(ReportDefinition def, ReportInput input)
    {
        if (def.Sorts.Count == 0 || input.Rows.Count < 2)
        {
            return input;
        }

        List<(IReadOnlyDictionary<string, object?> Row, object?[] Keys)> keyed = [.. input.Rows.Select(row => (Row: row, Keys: def.Sorts.Select(s => Evaluate(s.Expression, input, row)).ToArray()))];
        IOrderedEnumerable<(IReadOnlyDictionary<string, object?> Row, object?[] Keys)>? ordered = null;
        for (int level = 0; level < def.Sorts.Count; level++)
        {
            int at = level;
            Func<(IReadOnlyDictionary<string, object?> Row, object?[] Keys), object?> key = x => x.Keys[at];
            ordered = (ordered, def.Sorts[at].Descending) switch
            {
                (null, false) => keyed.OrderBy(key, GroupKeyComparer.Instance),
                (null, true) => keyed.OrderByDescending(key, GroupKeyComparer.Instance),
                (_, false) => ordered.ThenBy(key, GroupKeyComparer.Instance),
                _ => ordered.ThenByDescending(key, GroupKeyComparer.Instance),
            };
        }

        return new ReportInput
        {
            Definition = input.Definition, Rows = [.. ordered!.Select(x => x.Row)], Parameters = input.Parameters, UserName = input.UserName, CompanyName = input.CompanyName, PrintDate = input.PrintDate,
            ToBase = input.ToBase, Culture = input.Culture, Terminology = input.Terminology, SubReports = input.SubReports, Depth = input.Depth, CompanyLogo = input.CompanyLogo, HouseStyle = input.HouseStyle,
        };
    }

    // The page with its watermark for this run: an expression's text when it gives one, else the fixed watermark.
    private PageSetup ResolvePage(ReportDefinition def, ReportInput input)
    {
        if (string.IsNullOrWhiteSpace(def.Page.WatermarkExpression))
        {
            return def.Page;
        }

        string? text = ValueToString(Evaluate(def.Page.WatermarkExpression, input, input.Rows.Count > 0 ? input.Rows[0] : EmptyRow));
        return def.Page.WithWatermark(string.IsNullOrWhiteSpace(text) ? def.Page.Watermark : text);
    }

    private void BuildGrouped(ReportDefinition def, ReportInput input, ReportBand? detail, RenderModel model)
    {
        // A key per level for each row; rows are ordered level by level on the keys' values (numbers and dates as such, text ignoring
        // case), each level ascending or descending, keeping the data's order within a group.
        List<(IReadOnlyDictionary<string, object?> Row, string[] Keys, object?[] Values)> evaluated = input.Rows
            .Select(row =>
            {
                object?[] values = [.. def.Groups.Select(g => Evaluate(g.Expression, input, row))];
                return (Row: row, Keys: values.Select(ValueToString).ToArray(), Values: values);
            })
            .ToList();
        evaluated = TopGroups(def, evaluated);
        IOrderedEnumerable<(IReadOnlyDictionary<string, object?> Row, string[] Keys, object?[] Values)>? ordered = null;
        for (int level = 0; level < def.Groups.Count; level++)
        {
            int at = level;
            Func<(IReadOnlyDictionary<string, object?> Row, string[] Keys, object?[] Values), object?> key = x => x.Values[at];

            // The "others" group of a top-N level follows the groups it was ranked against, whichever way they sort.
            ordered = ordered is null ? evaluated.OrderBy(x => x.Values[at] is OthersKey) : ordered.ThenBy(x => x.Values[at] is OthersKey);
            ordered = (ordered, def.Groups[at].Descending) switch
            {
                (_, false) => ordered.ThenBy(key, GroupKeyComparer.Instance),
                _ => ordered.ThenByDescending(key, GroupKeyComparer.Instance),
            };
        }

        List<(IReadOnlyDictionary<string, object?> Row, string[] Keys)> keyed = [.. (ordered ?? evaluated.OrderBy(_ => 0)).Select(x => (x.Row, x.Keys))];

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
                    AddBand(model, footer, input, keyed[i - 1].Row, scope);
                }
            }

            // Open headers for levels that changed (outermost first); a header's aggregates cover its own group.
            foreach (ReportBand header in groupHeaders.Where(h => h.GroupLevel >= changedFrom))
            {
                AddBand(model, header, input, row, ScopeFor(keyed, keys, header.GroupLevel));
            }

            if (current is null || changedFrom < keys.Length)
            {
                _groupRowNumber = 0;
            }

            if (detail is not null)
            {
                AddDetail(model, def, detail, input, row);
            }

            current = keys;
        }

        // Close all remaining footers at the end.
        if (current is not null)
        {
            foreach (ReportBand footer in groupFooters)
            {
                IReadOnlyList<IReadOnlyDictionary<string, object?>> scope = ScopeFor(keyed, current, footer.GroupLevel);
                AddBand(model, footer, input, keyed[^1].Row, scope);
            }
        }
    }

    // Top-N groups (brief §13.1): at a level with a top count, only the largest groups within each parent stay, ranked by the total of a field
    // (or their rows); the rest are left out or gathered into an "others" group, whose group field reads the others label.
    private static List<(IReadOnlyDictionary<string, object?> Row, string[] Keys, object?[] Values)> TopGroups(ReportDefinition def,
        List<(IReadOnlyDictionary<string, object?> Row, string[] Keys, object?[] Values)> evaluated)
    {
        for (int level = 0; level < def.Groups.Count; level++)
        {
            ReportGroup group = def.Groups[level];
            if (group.TopCount is not int top || top <= 0)
            {
                continue;
            }

            int at = level;
            string Parent((IReadOnlyDictionary<string, object?> Row, string[] Keys, object?[] Values) x) => string.Join("\u001f", x.Keys.Take(at));
            decimal Weight(IEnumerable<(IReadOnlyDictionary<string, object?> Row, string[] Keys, object?[] Values)> rows)
                => string.IsNullOrWhiteSpace(group.TopBy) ? rows.Count() : rows.Sum(x => ToDecimal(x.Row.GetValueOrDefault(group.TopBy)));
            HashSet<(string Parent, string Key)> kept = [];
            foreach (var parent in evaluated.GroupBy(Parent, StringComparer.Ordinal))
            {
                foreach (var g in parent.GroupBy(x => x.Keys[at], StringComparer.Ordinal).OrderByDescending(g => Weight(g)).Take(top))
                {
                    kept.Add((parent.Key, g.Key));
                }
            }

            string? others = string.IsNullOrWhiteSpace(group.OthersLabel) ? null : group.OthersLabel.Trim();
            string field = group.Expression.Trim();
            List<(IReadOnlyDictionary<string, object?> Row, string[] Keys, object?[] Values)> next = [];
            foreach (var x in evaluated)
            {
                if (kept.Contains((Parent(x), x.Keys[at])))
                {
                    next.Add(x);
                }
                else if (others is not null)
                {
                    x.Keys[at] = others;
                    x.Values[at] = new OthersKey(others);
                    IReadOnlyDictionary<string, object?> row = x.Row.ContainsKey(field) ? new Dictionary<string, object?>(x.Row) { [field] = others } : x.Row;
                    next.Add((row, x.Keys, x.Values));
                }
            }

            evaluated = next;
        }

        return evaluated;
    }

    // A detail row: its number counted, the band (shaded on every second row when the band asks), then the detail-child bands for the same row.
    private void AddDetail(RenderModel model, ReportDefinition def, ReportBand detail, ReportInput input, IReadOnlyDictionary<string, object?> row)
    {
        _rowNumber++;
        _groupRowNumber++;
        if (!Suppressed(detail, input, row))
        {
            RenderBand rendered = ResolveBand(detail, input, row, input.Rows);
            if (!string.IsNullOrWhiteSpace(detail.AlternateBackground) && _rowNumber % 2 == 0)
            {
                (double paperWidth, double _) = ReportLayout.PaperSize(def.Page);
                rendered.Cells.Insert(0, new RenderCell
                {
                    Kind = ElementKind.Label, X = 0, Y = 0, Width = Math.Max(1, paperWidth - def.Page.MarginLeft - def.Page.MarginRight), Height = rendered.Height,
                    Background = detail.AlternateBackground.Trim(), Padding = 0,
                });
            }

            model.Body.Add(rendered);
        }

        foreach (ReportBand child in def.Bands.Where(b => b.Type == BandType.DetailChild))
        {
            AddBand(model, child, input, row, input.Rows);
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
        band is null || Suppressed(band, input, row) ? null : ResolveBand(band, input, row, scope);

    // A body band unless its suppress-when expression holds for the row.
    private void AddBand(RenderModel model, ReportBand band, ReportInput input, IReadOnlyDictionary<string, object?>? row, IReadOnlyList<IReadOnlyDictionary<string, object?>> scope)
    {
        if (!Suppressed(band, input, row))
        {
            model.Body.Add(ResolveBand(band, input, row, scope));
        }
    }

    private bool Suppressed(ReportBand band, ReportInput input, IReadOnlyDictionary<string, object?>? row)
        => !string.IsNullOrWhiteSpace(band.SuppressWhen) && _expressions.EvaluateBool(band.SuppressWhen, Context(input, row));

    private RenderBand ResolveBand(ReportBand band, ReportInput input, IReadOnlyDictionary<string, object?>? row, IReadOnlyList<IReadOnlyDictionary<string, object?>> scope)
    {
        RenderBand rendered = new()
        {
            Type = band.Type, Height = band.Height, GroupLevel = band.GroupLevel, RepeatOnNewPage = band.RepeatOnNewPage, KeepTogether = band.KeepTogether,
            PageBreakBefore = band.PageBreakBefore, PageBreakAfter = band.PageBreakAfter,
        };
        ReportExpressionContext ctx = Context(input, row);
        double designHeight = band.Height;

        foreach (ReportElement element in band.Elements)
        {
            if (!string.IsNullOrEmpty(element.SuppressWhen) && _expressions.EvaluateBool(element.SuppressWhen, ctx))
            {
                continue;
            }

            if (element.Kind == ElementKind.Chart)
            {
                rendered.Cells.AddRange(ChartImages.Cells(element, scope));
                continue;
            }

            if (element.Kind is ElementKind.Table or ElementKind.CrossTab)
            {
                rendered.Cells.AddRange(element.Kind == ElementKind.Table ? ReportGrids.Table(element, scope) : ReportGrids.CrossTab(element, scope));
                continue;
            }

            if (element.Kind == ElementKind.SubReport)
            {
                PlaceSubReport(rendered, element, input, row);
                continue;
            }

            (string? imageData, string? barcodeValue) = element.Kind switch
            {
                ElementKind.Image => (ImageFor(element, ctx, input), null),
                ElementKind.Barcode => BarcodeFor(element, ctx),
                _ => ((string?)null, (string?)null),
            };

            IReadOnlyList<RichRun>? runs = element.Kind == ElementKind.RichText
                ? RichText.Parse(Label(element, input), name => ctx.Fields.TryGetValue(name, out object? value) ? value : input.Parameters.GetValueOrDefault(name), element.Format, input.Culture)
                : null;

            string text = element.Kind switch
            {
                ElementKind.Checkbox => Truthy(ctx.Fields.GetValueOrDefault(element.FieldName ?? string.Empty)) ? "☑" : "☐",
                ElementKind.Signature => Label(element, input) is { Length: > 0 } caption ? caption : "Signature",
                ElementKind.Aggregate => NumberToWords.Format(ComputeAggregate(element, scope, row, input.Rows), element.Format, input.Culture),
                ElementKind.RichText => RichText.Plain(runs!),
                ElementKind.Image => string.Empty,
                ElementKind.Barcode => barcodeValue ?? string.Empty,
                _ => ResolveText(element, ctx, scope, input),
            };
            if (element.Kind is ElementKind.Field or ElementKind.Expression && element.NullText is string nullText && string.IsNullOrEmpty(text))
            {
                text = nullText;
            }

            (string foreground, string? background, bool bold) = ApplyConditionalFormats(element, ctx);
            double fontSize = element.AutoShrink && runs is null
                ? Layouts.FittedFontSize(text, element.FontSize, Math.Max(1, (element.Rotation % 180 == 0 ? element.Width : element.Height) - (2 * element.Padding)),
                    Math.Max(1, (element.Rotation % 180 == 0 ? element.Height : element.Width) - (2 * element.Padding)), element.WordWrap)
                : element.FontSize;
            (string? drillTarget, string? drillKey) = DrillThrough.For(element, row);

            rendered.Cells.Add(new RenderCell
            {
                Kind = element.Kind == ElementKind.Signature ? ElementKind.Label : element.Kind,
                Text = element.Kind == ElementKind.Signature ? "______________________________\n" + text : text,
                X = element.X,
                Y = element.Y,
                Width = element.Width,
                Height = element.Height,
                FontFamily = element.FontFamily,
                FontSize = fontSize,
                Bold = bold,
                Italic = element.Italic,
                Underline = element.Underline,
                Foreground = foreground,
                Background = background,
                HAlign = element.HAlign,
                BorderThickness = element.BorderThickness,
                BorderColor = element.BorderColor,
                VAlign = element.VAlign,
                BorderLeft = element.BorderLeft,
                BorderTop = element.BorderTop,
                BorderRight = element.BorderRight,
                BorderBottom = element.BorderBottom,
                Padding = element.Padding,
                WordWrap = element.WordWrap,
                Rotation = ((element.Rotation % 360) + 360) % 360,
                CornerRadius = Math.Max(0, element.CornerRadius),
                DrillTarget = drillTarget,
                DrillKey = drillKey,
                Runs = runs,
                ImageData = imageData,
                Tooltip = string.IsNullOrWhiteSpace(element.Tooltip) ? null : element.Tooltip,
                BackgroundGradient = string.IsNullOrWhiteSpace(element.BackgroundGradient) ? null : element.BackgroundGradient,
                GradientVertical = element.GradientVertical,
                AnchorRight = element.AnchorRight,
                AnchorBottom = element.AnchorBottom,
            });
        }

        // Anchoring: a band that grew (a sub-report laid into it) carries its bottom-anchored cells down with its bottom edge.
        if (rendered.Height > designHeight)
        {
            double grown = rendered.Height - designHeight;
            for (int i = 0; i < rendered.Cells.Count; i++)
            {
                if (rendered.Cells[i].AnchorBottom)
                {
                    rendered.Cells[i] = rendered.Cells[i] with { Y = rendered.Cells[i].Y + grown };
                }
            }
        }

        return rendered;
    }

    // An image: the element's own picture, else a field holding one (bytes, a data URI or base64), else the company logo when the
    // element's expression is CompanyLogo.
    private string? ImageFor(ReportElement element, ReportExpressionContext ctx, ReportInput input)
    {
        if (!string.IsNullOrWhiteSpace(element.ImageSource) && ImageSources.Resolve(element.ImageSource) is string sourced)
        {
            return sourced;
        }

        if (!string.IsNullOrWhiteSpace(element.ImageData))
        {
            return element.ImageData.StartsWith("data:", StringComparison.Ordinal) ? element.ImageData : "data:image/png;base64," + element.ImageData;
        }

        if (element.FieldName is string field && ctx.Fields.GetValueOrDefault(field) is object value)
        {
            return value switch
            {
                byte[] bytes => Barcodes.DataUri(bytes),
                string text when text.StartsWith("data:", StringComparison.Ordinal) => text,
                string text when text.Length > 0 => "data:image/png;base64," + text,
                _ => null,
            };
        }

        return string.Equals(element.Expression?.Trim(), "CompanyLogo", StringComparison.OrdinalIgnoreCase) && input.CompanyLogo is { Length: > 0 } logo
            ? Barcodes.DataUri(logo)
            : null;
    }

    // A barcode of the field's value, else the expression's, else the element's text.
    private (string? Image, string? Value) BarcodeFor(ReportElement element, ReportExpressionContext ctx)
    {
        string? value = element.FieldName is string field && ctx.Fields.GetValueOrDefault(field) is object raw ? Convert.ToString(raw, System.Globalization.CultureInfo.InvariantCulture)
            : !string.IsNullOrWhiteSpace(element.Expression) ? Convert.ToString(_expressions.Evaluate(element.Expression, ctx), System.Globalization.CultureInfo.InvariantCulture)
            : element.Text;
        return (Barcodes.Png(element.BarcodeSymbology, value) is byte[] png ? Barcodes.DataUri(png) : null, value);
    }

    // The child report built with the linked parameters and laid into the element's box; the band grows to hold it.
    private void PlaceSubReport(RenderBand band, ReportElement element, ReportInput input, IReadOnlyDictionary<string, object?>? row)
    {
        ReportInput? child = input.Depth >= SubReports.MaxDepth || input.SubReports is null
            ? null
            : input.SubReports(element.SubReportCode ?? string.Empty, SubReports.ParametersFor(element, input.Parameters, row));
        if (child is null)
        {
            band.Cells.Add(new RenderCell
            {
                Kind = ElementKind.Label, Text = $"(sub-report {element.SubReportCode} not available)", X = element.X, Y = element.Y, Width = element.Width, Height = element.Height,
                FontFamily = element.FontFamily, FontSize = element.FontSize, Italic = true, Foreground = "#808080",
            });
            return;
        }

        RenderModel built = Build(new ReportInput
        {
            Definition = child.Definition, Rows = child.Rows, Parameters = child.Parameters, UserName = input.UserName, CompanyName = input.CompanyName, PrintDate = input.PrintDate,
            ToBase = child.ToBase ?? input.ToBase, Culture = child.Culture ?? input.Culture, Terminology = child.Terminology ?? input.Terminology, SubReports = child.SubReports ?? input.SubReports,
            Depth = input.Depth + 1, CompanyLogo = input.CompanyLogo, HouseStyle = input.HouseStyle,
        });
        (List<RenderCell> cells, double height) = SubReports.Place(element, built);
        band.Cells.AddRange(cells);
        band.Height = Math.Max(band.Height, element.Y + Math.Max(height, 0));
    }

    private string ResolveText(ReportElement element, ReportExpressionContext ctx, IReadOnlyList<IReadOnlyDictionary<string, object?>> scope, ReportInput input) => element.Kind switch
    {
        ElementKind.Label => Label(element, input),
        ElementKind.Field => NumberToWords.Format(ctx.Fields.GetValueOrDefault(element.FieldName ?? string.Empty), element.Format, input.Culture),
        ElementKind.Expression => NumberToWords.Format(_expressions.Evaluate(element.Expression ?? string.Empty, ctx), element.Format, input.Culture),
        ElementKind.Aggregate => NumberToWords.Format(ComputeAggregate(element, scope, null, scope), element.Format, input.Culture),
        _ => string.Empty,
    };

    // A label in the report's language (its translation for the culture, then the language), with the company's terms.
    private static string Label(ReportElement element, ReportInput input)
    {
        string text = element.Text ?? string.Empty;
        Dictionary<string, Dictionary<string, string>> translations = input.Definition.LabelTranslations;
        if (input.Culture is System.Globalization.CultureInfo culture && translations.Count > 0
            && ((translations.GetValueOrDefault(culture.Name)?.TryGetValue(element.Name, out string? specific) ?? false ? specific : null)
                ?? (translations.GetValueOrDefault(culture.TwoLetterISOLanguageName)?.TryGetValue(element.Name, out string? neutral) ?? false ? neutral : null)) is string translated)
        {
            text = translated;
        }

        return Core.Common.Terminology.Apply(text, input.Terminology);
    }

    private static decimal ComputeAggregate(ReportElement element, IReadOnlyList<IReadOnlyDictionary<string, object?>> scope, IReadOnlyDictionary<string, object?>? row,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> all)
    {
        string field = element.AggregateField ?? string.Empty;
        if (element.AggregateScope == AggregateScope.GrandTotal)
        {
            scope = all;
        }

        List<decimal> values = [.. scope
            .Select(r => r.GetValueOrDefault(field))
            .Where(v => v is not null)
            .Select(ToDecimal)];

        decimal Value(IReadOnlyDictionary<string, object?> r) => r.GetValueOrDefault(field) is object v ? ToDecimal(v) : 0m;
        return element.Aggregate switch
        {
            AggregateFunction.RunningTotal when row is not null => scope.TakeWhile(r => !ReferenceEquals(r, row)).Sum(Value) + Value(row),
            AggregateFunction.Median => Statistics.Median(values),
            AggregateFunction.StdDev => Statistics.SampleStdDev(values),
            AggregateFunction.PercentOfTotal when row is not null && ReferenceEquals(scope, all) =>
                values.Sum() is decimal total && total != 0m ? Math.Round(Value(row) * 100m / total, 2) : 0m,
            AggregateFunction.PercentOfTotal => all.Where(r => r.GetValueOrDefault(field) is not null).Sum(Value) is decimal grand && grand != 0m ? Math.Round(values.Sum() * 100m / grand, 2) : 0m,
            AggregateFunction.RankOf when row is not null => 1 + values.Count(v => v > Value(row)),
            AggregateFunction.Sum => values.Sum(),
            AggregateFunction.Average => values.Any() ? values.Average() : 0m,
            AggregateFunction.Count => scope.Count,
            AggregateFunction.DistinctCount => scope.Select(r => r.GetValueOrDefault(field)?.ToString()).Distinct().Count(),
            AggregateFunction.Min => values.Any() ? values.Min() : 0m,
            AggregateFunction.Max => values.Any() ? values.Max() : 0m,
            AggregateFunction.First => values.Count > 0 ? values[0] : 0m,
            AggregateFunction.Last => values.Count > 0 ? values[^1] : 0m,
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

    private ReportExpressionContext Context(ReportInput input, IReadOnlyDictionary<string, object?>? row) => new()
    {
        Fields = row ?? EmptyRow,
        RowNumber = _rowNumber,
        GroupRowNumber = _groupRowNumber,
        Parameters = input.Parameters,
        PrintDate = input.PrintDate,
        UserName = input.UserName,
        CompanyName = input.CompanyName,
        ToBase = input.ToBase,
    };

    private static bool Truthy(object? value) => value switch
    {
        bool b => b,
        string s => s.Equals("true", StringComparison.OrdinalIgnoreCase) || s.Equals("yes", StringComparison.OrdinalIgnoreCase) || s == "1" || s.Equals("y", StringComparison.OrdinalIgnoreCase),
        null => false,
        _ => ToDecimal(value) != 0m,
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

    // The key of a top-N level's "others" group.
    private sealed record OthersKey(string Label)
    {
        public override string ToString() => Label;
    }

    // Orders group keys by value: numbers numerically, dates by date, everything else as text ignoring case; nulls first.
    private sealed class GroupKeyComparer : IComparer<object?>
    {
        public static readonly GroupKeyComparer Instance = new();

        public int Compare(object? x, object? y)
        {
            if (x is null || y is null)
            {
                return x is null ? (y is null ? 0 : -1) : 1;
            }

            if (Number(x) is decimal a && Number(y) is decimal b)
            {
                return a.CompareTo(b);
            }

            if (Moment(x) is DateTime p && Moment(y) is DateTime q)
            {
                return p.CompareTo(q);
            }

            return StringComparer.OrdinalIgnoreCase.Compare(ValueToString(x), ValueToString(y));
        }

        private static decimal? Number(object value) => value switch
        {
            decimal d => d,
            int i => i,
            long l => l,
            short s => s,
            double db => (decimal)db,
            float f => (decimal)f,
            _ => null,
        };

        private static DateTime? Moment(object value) => value switch
        {
            DateTime d => d,
            DateOnly d => d.ToDateTime(TimeOnly.MinValue),
            DateTimeOffset d => d.UtcDateTime,
            _ => null,
        };
    }
}
