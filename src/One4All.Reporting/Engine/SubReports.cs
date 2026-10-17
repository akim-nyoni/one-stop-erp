using System.Globalization;
using System.Text;
using One4All.Reporting.Expressions;
using One4All.Reporting.Model;

namespace One4All.Reporting.Engine;

/// <summary>One run of rich text: its words and how they are emphasised. A run with <see cref="LineBreak"/> starts a new line.</summary>
/// <param name="Text">The text of the run.</param>
/// <param name="Bold">Whether it is bold.</param>
/// <param name="Italic">Whether it is italic.</param>
/// <param name="Underline">Whether it is underlined.</param>
/// <param name="LineBreak">Whether this run is a line break (its text is empty).</param>
public sealed record RichRun(string Text, bool Bold = false, bool Italic = false, bool Underline = false, bool LineBreak = false);

/// <summary>
/// Rich text for report elements: a small, safe markup - <c>**bold**</c>, <c>*italic*</c>, <c>__underline__</c>, line breaks, and
/// <c>{Field}</c> placeholders filled from the row (formatted like a field). Anything else is literal text; a placeholder's value is
/// never itself read as markup.
/// </summary>
public static class RichText
{
    /// <summary>Turns markup into runs.</summary>
    /// <param name="markup">The element's text.</param>
    /// <param name="field">Looks up a field's value by name for <c>{Field}</c>.</param>
    /// <param name="format">The element's format string for placeholder values.</param>
    /// <param name="culture">The report's culture.</param>
    /// <returns>The runs in order.</returns>
    public static IReadOnlyList<RichRun> Parse(string? markup, Func<string, object?> field, string? format = null, CultureInfo? culture = null)
    {
        List<RichRun> runs = [];
        if (string.IsNullOrEmpty(markup))
        {
            return runs;
        }

        bool bold = false, italic = false, underline = false;
        StringBuilder current = new();
        void Flush()
        {
            if (current.Length > 0)
            {
                runs.Add(new RichRun(current.ToString(), bold, italic, underline));
                current.Clear();
            }
        }

        string text = markup.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\\n", "\n", StringComparison.Ordinal);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\n')
            {
                Flush();
                runs.Add(new RichRun(string.Empty, LineBreak: true));
            }
            else if (c == '*' && i + 1 < text.Length && text[i + 1] == '*')
            {
                Flush();
                bold = !bold;
                i++;
            }
            else if (c == '_' && i + 1 < text.Length && text[i + 1] == '_')
            {
                Flush();
                underline = !underline;
                i++;
            }
            else if (c == '*')
            {
                Flush();
                italic = !italic;
            }
            else if (c == '{' && text.IndexOf('}', i + 1) is int close && close > i + 1 && text[(i + 1)..close] is string name && name.All(ch => char.IsLetterOrDigit(ch) || ch is '_' or '.'))
            {
                Flush();
                runs.Add(new RichRun(NumberToWords.Format(field(name), format, culture), bold, italic, underline));
                i = close;
            }
            else
            {
                current.Append(c);
            }
        }

        Flush();
        return runs;
    }

    /// <summary>The runs as plain text (line breaks as new lines), for exports and searching.</summary>
    /// <param name="runs">The runs.</param>
    /// <returns>The text.</returns>
    public static string Plain(IEnumerable<RichRun> runs) => string.Concat(runs.Select(r => r.LineBreak ? "\n" : r.Text));
}

/// <summary>
/// Sub-reports: another report placed in a band, run with parameters linked from the parent row. The engine stays free of data
/// access - the host supplies a resolver (<see cref="ReportInput.SubReports"/>) that returns the child's input for a code and
/// parameter set, usually fetched beforehand for every distinct set (see <see cref="Key"/>).
/// </summary>
public static class SubReports
{
    /// <summary>The deepest a sub-report may nest (a sub-report inside a sub-report inside a report).</summary>
    public const int MaxDepth = 3;

    /// <summary>The child's parameters: the parent's, overlaid with each link's value from the row (or the parent's parameter of that name).</summary>
    /// <param name="element">The sub-report element.</param>
    /// <param name="parentParameters">The parent report's parameters.</param>
    /// <param name="row">The parent row, if the element sits in a detail or group band.</param>
    /// <returns>The parameters to run the child with.</returns>
    public static IReadOnlyDictionary<string, object?> ParametersFor(ReportElement element, IReadOnlyDictionary<string, object?> parentParameters, IReadOnlyDictionary<string, object?>? row)
    {
        Dictionary<string, object?> parameters = new(parentParameters, StringComparer.OrdinalIgnoreCase);
        foreach (SubReportLink link in element.SubReportLinks.Where(l => l.ChildParameter.Length > 0))
        {
            parameters[link.ChildParameter] = row is not null && row.TryGetValue(link.ParentField, out object? value) ? value : parentParameters.GetValueOrDefault(link.ParentField);
        }

        return parameters;
    }

    /// <summary>A stable key for a code and parameter set, so a host can fetch each distinct child once.</summary>
    /// <param name="code">The child report's code.</param>
    /// <param name="parameters">The child's parameters.</param>
    /// <returns>The key.</returns>
    public static string Key(string code, IReadOnlyDictionary<string, object?> parameters)
        => code.ToUpperInvariant() + "|" + string.Join("|", parameters.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
            .Select(p => p.Key.ToUpperInvariant() + "=" + Convert.ToString(p.Value, CultureInfo.InvariantCulture)));

    /// <summary>Every sub-report element in a definition, with whether its band repeats per row.</summary>
    /// <param name="definition">The report definition.</param>
    /// <returns>The elements.</returns>
    public static IEnumerable<(ReportElement Element, bool PerRow)> ElementsOf(ReportDefinition definition)
        => definition.Bands.SelectMany(b => b.Elements.Where(e => e.Kind == ElementKind.SubReport && !string.IsNullOrWhiteSpace(e.SubReportCode))
            .Select(e => (e, b.Type is BandType.Detail or BandType.GroupHeader or BandType.GroupFooter)));

    /// <summary>Lays a built child report into the element's box: its bands stacked from the element's top, report header to footer.</summary>
    /// <param name="element">The sub-report element.</param>
    /// <param name="child">The child's render model.</param>
    /// <returns>The cells, positioned in the parent band, and the height they take.</returns>
    public static (List<RenderCell> Cells, double Height) Place(ReportElement element, RenderModel child)
    {
        List<RenderCell> cells = [];
        double y = 0;
        foreach (RenderBand band in new[] { child.ReportHeader, child.PageHeader }.Concat(child.Body).Append(child.ReportFooter).OfType<RenderBand>())
        {
            cells.AddRange(band.Cells.Select(c => Layouts.Moved(c, element.X + c.X, element.Y + y + c.Y)));
            y += band.Height;
        }

        return (cells, y);
    }
}
