using System.Globalization;
using System.Reflection;
using One4All.Reporting.Engine;
using One4All.Reporting.Expressions;
using One4All.Reporting.Model;

namespace One4All.Reporting.Design;

/// <summary>How selected elements line up.</summary>
public enum DesignAlignment
{
    /// <summary>Left edges.</summary>
    Left = 1,

    /// <summary>Right edges.</summary>
    Right = 2,

    /// <summary>Top edges.</summary>
    Top = 3,

    /// <summary>Bottom edges.</summary>
    Bottom = 4,

    /// <summary>Horizontal centres.</summary>
    CenterHorizontally = 5,

    /// <summary>Vertical centres.</summary>
    CenterVertically = 6,
}

/// <summary>A house style applied across a report (brief §13.2 themes): fonts, sizes, and the header band's colours.</summary>
/// <param name="Name">The theme name.</param>
/// <param name="FontFamily">The font for every text element.</param>
/// <param name="TitleSize">The report header's text size.</param>
/// <param name="HeaderSize">Page and group header text size.</param>
/// <param name="DetailSize">Detail and footer text size.</param>
/// <param name="HeaderForeground">The page header's text colour.</param>
/// <param name="HeaderBackground">The page header's background (null: none).</param>
public sealed record ReportTheme(string Name, string FontFamily, double TitleSize, double HeaderSize, double DetailSize, string HeaderForeground, string? HeaderBackground)
{
    /// <summary>Gets the built-in themes.</summary>
    public static IReadOnlyList<ReportTheme> BuiltIn { get; } =
    [
        new ReportTheme("Corporate", "Segoe UI", 16, 9, 9, "#FFFFFF", "#1B5E20"),
        new ReportTheme("Classic", "Times New Roman", 16, 10, 10, "#000000", null),
        new ReportTheme("Compact", "Arial", 13, 8, 7.5, "#000000", "#E0E0E0"),
        new ReportTheme("Council", "Calibri", 15, 9, 9, "#FFFFFF", "#1A3A6E"),
    ];
}

/// <summary>
/// The banded report designer's model (brief §13.2): a working copy of a definition with snap-to-grid moves and resizes, alignment,
/// equal sizing and distribution of selected elements, typed property edits, band heights, themes, and undo / redo of every change.
/// Elements are addressed by name (made unique when the session opens) so selections survive undo.
/// </summary>
public sealed class ReportDesignSession
{
    /// <summary>The properties the property grid edits, by name.</summary>
    public static readonly IReadOnlyList<string> EditableProperties =
    [
        nameof(ReportElement.Text), nameof(ReportElement.FieldName), nameof(ReportElement.Expression), nameof(ReportElement.Format), nameof(ReportElement.Aggregate),
        nameof(ReportElement.AggregateField), nameof(ReportElement.AggregateScope), nameof(ReportElement.X), nameof(ReportElement.Y), nameof(ReportElement.Width), nameof(ReportElement.Height),
        nameof(ReportElement.FontFamily), nameof(ReportElement.FontSize), nameof(ReportElement.Bold), nameof(ReportElement.Italic), nameof(ReportElement.Underline),
        nameof(ReportElement.Foreground), nameof(ReportElement.Background), nameof(ReportElement.HAlign), nameof(ReportElement.BorderThickness), nameof(ReportElement.BorderColor),
        nameof(ReportElement.SuppressWhen), nameof(ReportElement.DrillTarget), nameof(ReportElement.DrillField),
        nameof(ReportElement.ChartCategoryField), nameof(ReportElement.ChartColor), nameof(ReportElement.ChartType), nameof(ReportElement.ChartSeriesField),
        nameof(ReportElement.VAlign), nameof(ReportElement.Padding), nameof(ReportElement.BorderLeft), nameof(ReportElement.BorderTop), nameof(ReportElement.BorderRight),
        nameof(ReportElement.BorderBottom), nameof(ReportElement.WordWrap), nameof(ReportElement.AutoShrink), nameof(ReportElement.Rotation), nameof(ReportElement.NullText), nameof(ReportElement.SubReportCode), nameof(ReportElement.SubReportLinks),
        nameof(ReportElement.BarcodeSymbology), nameof(ReportElement.Tooltip), nameof(ReportElement.ImageSource), nameof(ReportElement.GroupName), nameof(ReportElement.AnchorRight),
        nameof(ReportElement.AnchorBottom), nameof(ReportElement.BackgroundGradient), nameof(ReportElement.GradientVertical),
    ];

    private const int MaxUndo = 200;
    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();

    /// <summary>Opens a session on a copy of a definition.</summary>
    /// <param name="definition">The definition (not changed).</param>
    /// <param name="grid">The snap grid in points (0: no snapping).</param>
    public ReportDesignSession(ReportDefinition definition, double grid = 4)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Definition = ReportDefinitionSerializer.Deserialize(ReportDefinitionSerializer.Serialize(definition));
        Grid = grid;
        NameElements();
    }

    /// <summary>Gets the definition being designed.</summary>
    public ReportDefinition Definition { get; private set; }

    /// <summary>Gets or sets the snap grid in points.</summary>
    public double Grid { get; set; }

    /// <summary>Gets a value indicating whether there is something to undo.</summary>
    public bool CanUndo => _undo.Count > 0;

    /// <summary>Gets a value indicating whether there is something to redo.</summary>
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Gets the printable width in points (paper less margins).</summary>
    public double PrintableWidth => ReportLayout.PaperSize(Definition.Page).Width - Definition.Page.MarginLeft - Definition.Page.MarginRight;

    /// <summary>Finds an element by name.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The element and its band, or nulls.</returns>
    public (ReportElement? Element, ReportBand? Band) Find(string name)
    {
        foreach (ReportBand band in Definition.Bands)
        {
            if (band.Elements.FirstOrDefault(e => e.Name == name) is ReportElement element)
            {
                return (element, band);
            }
        }

        return (null, null);
    }

    /// <summary>Adds a band of a type (one of each, except group bands per level), at its natural place in the order.</summary>
    /// <param name="type">The band type.</param>
    /// <param name="height">Its height.</param>
    /// <returns>The band.</returns>
    public ReportBand AddBand(BandType type, double height = 20)
    {
        if (Definition.BandOfType(type) is ReportBand existing && type is not (BandType.GroupHeader or BandType.GroupFooter))
        {
            return existing;
        }

        Checkpoint();
        ReportBand band = new() { Type = type, Height = Math.Max(4, height) };
        Definition.Bands.Add(band);
        Definition.Bands.Sort((a, b) => ((int)a.Type).CompareTo((int)b.Type));
        return band;
    }

    /// <summary>Adds an element to a band (the band is added if missing), snapped and kept inside the printable width.</summary>
    /// <param name="bandType">The band.</param>
    /// <param name="kind">The element kind.</param>
    /// <param name="x">Left, in points.</param>
    /// <param name="y">Top within the band, in points.</param>
    /// <param name="textOrField">A label's text, a field's name, or an expression.</param>
    /// <returns>The new element's name.</returns>
    public string AddElement(BandType bandType, ElementKind kind, double x, double y, string? textOrField = null)
    {
        ReportBand band = Definition.BandOfType(bandType) ?? AddBand(bandType);
        Checkpoint();
        ReportElement element = new()
        {
            Kind = kind, Name = UniqueName(kind.ToString()), X = Snap(x), Y = Snap(y), Width = kind == ElementKind.Line ? 200 : 100, Height = kind == ElementKind.Line ? 1 : 16,
        };
        switch (kind)
        {
            case ElementKind.Field:
                element.FieldName = textOrField;
                break;
            case ElementKind.Expression:
                element.Expression = textOrField;
                break;
            default:
                element.Text = textOrField ?? (kind == ElementKind.Label ? "Label" : null);
                break;
        }

        Contain(element, band);
        band.Elements.Add(element);
        return element.Name;
    }

    /// <summary>Moves elements by an offset, each snapped and kept inside its band and the printable width.</summary>
    /// <param name="names">The elements.</param>
    /// <param name="dx">Horizontal offset.</param>
    /// <param name="dy">Vertical offset.</param>
    public void Move(IEnumerable<string> names, double dx, double dy)
    {
        List<(ReportElement Element, ReportBand Band)> chosen = Resolve(WithGroups(names));
        if (chosen.Count == 0)
        {
            return;
        }

        Checkpoint();
        foreach ((ReportElement element, ReportBand band) in chosen)
        {
            element.X = Snap(element.X + dx);
            element.Y = Snap(element.Y + dy);
            Contain(element, band);
        }
    }

    /// <summary>Resizes an element (snapped; never below 2 points).</summary>
    /// <param name="name">The element.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    public void Resize(string name, double width, double height)
    {
        if (Find(name) is not (ReportElement element, ReportBand band))
        {
            return;
        }

        Checkpoint();
        element.Width = Math.Max(2, Snap(width));
        element.Height = Math.Max(element.Kind == ElementKind.Line ? 1 : 2, Snap(height));
        Contain(element, band);
    }

    /// <summary>Lines elements up with the first one named.</summary>
    /// <param name="names">The elements (the first is the anchor).</param>
    /// <param name="alignment">How.</param>
    public void Align(IReadOnlyList<string> names, DesignAlignment alignment)
    {
        List<(ReportElement Element, ReportBand Band)> chosen = Resolve(names);
        if (chosen.Count < 2)
        {
            return;
        }

        Checkpoint();
        ReportElement anchor = chosen[0].Element;
        foreach ((ReportElement element, ReportBand band) in chosen.Skip(1))
        {
            switch (alignment)
            {
                case DesignAlignment.Left: element.X = anchor.X; break;
                case DesignAlignment.Right: element.X = anchor.X + anchor.Width - element.Width; break;
                case DesignAlignment.Top: element.Y = anchor.Y; break;
                case DesignAlignment.Bottom: element.Y = anchor.Y + anchor.Height - element.Height; break;
                case DesignAlignment.CenterHorizontally: element.X = anchor.X + ((anchor.Width - element.Width) / 2); break;
                default: element.Y = anchor.Y + ((anchor.Height - element.Height) / 2); break;
            }

            Contain(element, band);
        }
    }

    /// <summary>Gives elements the first one's width and / or height.</summary>
    /// <param name="names">The elements (the first is the pattern).</param>
    /// <param name="width">Match the width.</param>
    /// <param name="height">Match the height.</param>
    public void SameSize(IReadOnlyList<string> names, bool width, bool height)
    {
        List<(ReportElement Element, ReportBand Band)> chosen = Resolve(names);
        if (chosen.Count < 2)
        {
            return;
        }

        Checkpoint();
        foreach ((ReportElement element, ReportBand band) in chosen.Skip(1))
        {
            element.Width = width ? chosen[0].Element.Width : element.Width;
            element.Height = height ? chosen[0].Element.Height : element.Height;
            Contain(element, band);
        }
    }

    /// <summary>Spaces three or more elements evenly between the leftmost and rightmost.</summary>
    /// <param name="names">The elements.</param>
    public void DistributeHorizontally(IEnumerable<string> names)
    {
        List<ReportElement> chosen = [.. Resolve(names).Select(c => c.Element).OrderBy(e => e.X)];
        if (chosen.Count < 3)
        {
            return;
        }

        Checkpoint();
        double span = chosen[^1].X + chosen[^1].Width - chosen[0].X, used = chosen.Sum(e => e.Width);
        double gap = (span - used) / (chosen.Count - 1), x = chosen[0].X;
        foreach (ReportElement element in chosen)
        {
            element.X = Math.Round(x, 2);
            x += element.Width + gap;
        }
    }

    /// <summary>Deletes elements.</summary>
    /// <param name="names">The elements.</param>
    public void Delete(IEnumerable<string> names)
    {
        List<(ReportElement Element, ReportBand Band)> chosen = Resolve(names);
        if (chosen.Count == 0)
        {
            return;
        }

        Checkpoint();
        chosen.ForEach(c => c.Band.Elements.Remove(c.Element));
    }

    /// <summary>
    /// Brings elements in front of the others in their band, or sends them behind (an element later in its band draws over those before it).
    /// The chosen keep their order among themselves.
    /// </summary>
    /// <param name="names">The elements.</param>
    /// <param name="toFront">True: to the front; false: to the back.</param>
    public void Restack(IEnumerable<string> names, bool toFront)
    {
        List<(ReportElement Element, ReportBand Band)> chosen = Resolve(names);
        if (chosen.Count == 0)
        {
            return;
        }

        Checkpoint();
        foreach (IGrouping<ReportBand, ReportElement> band in chosen.GroupBy(c => c.Band, c => c.Element))
        {
            List<ReportElement> moving = [.. band.Key.Elements.Where(e => band.Contains(e))];
            List<ReportElement> staying = [.. band.Key.Elements.Where(e => !moving.Contains(e))];
            band.Key.Elements.Clear();
            band.Key.Elements.AddRange(toFront ? staying.Concat(moving) : moving.Concat(staying));
        }
    }

    /// <summary>Copies elements to the same band, offset by one grid step (or 8 points).</summary>
    /// <param name="names">The elements.</param>
    /// <returns>The copies' names.</returns>
    public IReadOnlyList<string> Duplicate(IEnumerable<string> names)
    {
        List<(ReportElement Element, ReportBand Band)> chosen = Resolve(names);
        if (chosen.Count == 0)
        {
            return [];
        }

        Checkpoint();
        List<string> copies = [];
        double step = Grid > 0 ? Grid * 2 : 8;
        foreach ((ReportElement element, ReportBand band) in chosen)
        {
            ReportElement copy = ReportDefinitionSerializer.Deserialize(ReportDefinitionSerializer.Serialize(new ReportDefinition { Bands = [new ReportBand { Elements = [element] }] }))
                .Bands[0].Elements[0];
            copy.Name = UniqueName(element.Kind.ToString());
            copy.X += step;
            copy.Y += step;
            Contain(copy, band);
            band.Elements.Add(copy);
            copies.Add(copy.Name);
        }

        return copies;
    }

    /// <summary>The value of an editable property, as text.</summary>
    /// <param name="name">The element.</param>
    /// <param name="property">The property.</param>
    /// <returns>The text, or null.</returns>
    public string? GetProperty(string name, string property)
        => Find(name).Element is not ReportElement element ? null
            : property == nameof(ReportElement.SubReportLinks) ? FormatLinks(element.SubReportLinks)
            : Property(property) is PropertyInfo info ? Convert.ToString(info.GetValue(element), CultureInfo.InvariantCulture)
            : null;

    /// <summary>Sub-report links as text: <c>ChildParameter=ParentField</c> pairs separated by semicolons.</summary>
    /// <param name="links">The links.</param>
    /// <returns>The text.</returns>
    public static string FormatLinks(IEnumerable<SubReportLink> links) => string.Join("; ", links.Select(l => $"{l.ChildParameter}={l.ParentField}"));

    /// <summary>Reads sub-report links from text (<c>ChildParameter=ParentField; ...</c>; a bare name links a parameter to the field of the same name).</summary>
    /// <param name="text">The text.</param>
    /// <returns>The links, or why the text is not valid.</returns>
    public static (List<SubReportLink> Links, string? Problem) ParseLinks(string? text)
    {
        List<SubReportLink> links = [];
        foreach (string part in (text ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] sides = part.Split('=', StringSplitOptions.TrimEntries);
            if (sides.Length > 2 || sides.Any(s => s.Length == 0 || !s.All(c => char.IsLetterOrDigit(c) || c == '_')))
            {
                return ([], $"\"{part}\" is not Parameter=Field.");
            }

            links.Add(new SubReportLink { ChildParameter = sides[0], ParentField = sides.Length == 2 ? sides[1] : sides[0] });
        }

        return (links, null);
    }

    /// <summary>Sets an editable property from text, converting to its type.</summary>
    /// <param name="name">The element.</param>
    /// <param name="property">The property.</param>
    /// <param name="value">The value (blank clears an optional text).</param>
    /// <returns>Null when set, or why not.</returns>
    public string? SetProperty(string name, string property, string? value)
    {
        if (Find(name) is not (ReportElement element, ReportBand band))
        {
            return $"No element {name}.";
        }

        if (property == nameof(ReportElement.SubReportLinks))
        {
            (List<SubReportLink> links, string? problem) = ParseLinks(value);
            if (problem is not null)
            {
                return problem;
            }

            Checkpoint();
            element.SubReportLinks = links;
            return null;
        }

        if (Property(property) is not PropertyInfo info)
        {
            return $"{property} cannot be edited here.";
        }

        object? converted;
        try
        {
            Type type = Nullable.GetUnderlyingType(info.PropertyType) ?? info.PropertyType;
            converted = string.IsNullOrWhiteSpace(value) && (type == typeof(string) || Nullable.GetUnderlyingType(info.PropertyType) is not null) ? null
                : type.IsEnum ? Enum.Parse(type, value!, ignoreCase: true)
                : type == typeof(string) ? value
                : Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidCastException or OverflowException)
        {
            return $"\"{value}\" is not a valid {info.Name}.";
        }

        if (converted is double number && number < 0 && info.Name is not (nameof(ReportElement.X) or nameof(ReportElement.Y)))
        {
            return $"{info.Name} cannot be negative.";
        }

        if (info.PropertyType == typeof(string) && converted is null && info.Name is nameof(ReportElement.FontFamily) or nameof(ReportElement.Foreground) or nameof(ReportElement.BorderColor))
        {
            return $"{info.Name} needs a value.";
        }

        Checkpoint();
        info.SetValue(element, converted);
        Contain(element, band);
        return null;
    }

    /// <summary>Sets a band's height (elements inside are kept within it).</summary>
    /// <param name="type">The band.</param>
    /// <param name="height">The height.</param>
    public void SetBandHeight(BandType type, double height)
    {
        if (Definition.BandOfType(type) is not ReportBand band)
        {
            return;
        }

        Checkpoint();
        band.Height = Math.Max(4, Snap(height));
        band.Elements.ForEach(e => Contain(e, band));
    }

    /// <summary>Sets how a band flows across pages (brief §13.1): suppress-when, repeat on a new page, keep-together and page breaks.</summary>
    /// <param name="type">The band type.</param>
    /// <param name="groupLevel">The group level (group headers and footers).</param>
    /// <param name="suppressWhen">The expression that hides the band (blank: never).</param>
    /// <param name="repeatOnNewPage">Repeat a group header on each page its group continues on.</param>
    /// <param name="keepTogether">Keep a group header with the band after it.</param>
    /// <param name="pageBreakBefore">Start a new page before the band.</param>
    /// <param name="pageBreakAfter">Start a new page after the band.</param>
    /// <returns>Whether the band exists.</returns>
    public bool SetBandOptions(BandType type, int groupLevel, string? suppressWhen, bool repeatOnNewPage, bool keepTogether, bool pageBreakBefore, bool pageBreakAfter)
    {
        ReportBand? band = Definition.Bands.FirstOrDefault(b => b.Type == type && (type is not (BandType.GroupHeader or BandType.GroupFooter) || b.GroupLevel == groupLevel));
        if (band is null)
        {
            return false;
        }

        Checkpoint();
        band.SuppressWhen = string.IsNullOrWhiteSpace(suppressWhen) ? null : suppressWhen.Trim();
        (band.RepeatOnNewPage, band.KeepTogether, band.PageBreakBefore, band.PageBreakAfter) = (repeatOnNewPage, keepTogether, pageBreakBefore, pageBreakAfter);
        return true;
    }

    /// <summary>Replaces the page setup (paper, margins, columns, watermark, background, tray, duplex), as one undoable change.</summary>
    /// <param name="page">The new page setup.</param>
    public void SetPage(PageSetup page)
    {
        ArgumentNullException.ThrowIfNull(page);
        Checkpoint();
        Definition.Page = page;
        foreach (ReportBand band in Definition.Bands)
        {
            band.Elements.ForEach(e => Contain(e, band));
        }
    }

    /// <summary>Applies a theme: the font everywhere, sizes by band, and the page header's colours.</summary>
    /// <param name="theme">The theme.</param>
    public void ApplyTheme(ReportTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        Checkpoint();
        foreach (ReportBand band in Definition.Bands)
        {
            foreach (ReportElement element in band.Elements.Where(e => e.Kind is not (ElementKind.Line or ElementKind.Rectangle or ElementKind.Ellipse or ElementKind.Image)))
            {
                element.FontFamily = theme.FontFamily;
                element.FontSize = band.Type switch
                {
                    BandType.ReportHeader => theme.TitleSize,
                    BandType.PageHeader or BandType.GroupHeader => theme.HeaderSize,
                    _ => theme.DetailSize,
                };
                if (band.Type == BandType.PageHeader)
                {
                    element.Foreground = theme.HeaderForeground;
                    element.Background = theme.HeaderBackground;
                }
            }
        }
    }

    /// <summary>Undoes the last change.</summary>
    /// <returns>True when something was undone.</returns>
    public bool Undo() => Swap(_undo, _redo);

    /// <summary>Redoes the last undone change.</summary>
    /// <returns>True when something was redone.</returns>
    public bool Redo() => Swap(_redo, _undo);

    /// <summary>Evaluates an expression against a sample row, for the expression editor's live preview.</summary>
    /// <param name="expression">The expression.</param>
    /// <param name="row">The sample row.</param>
    /// <param name="parameters">The parameters.</param>
    /// <returns>The value as text, or the error.</returns>
    public static string Preview(string expression, IReadOnlyDictionary<string, object?>? row, IReadOnlyDictionary<string, object?>? parameters = null)
    {
        try
        {
            object? value = new ReportExpressionEngine().Evaluate(expression, new ReportExpressionContext
            {
                Fields = row ?? new Dictionary<string, object?>(), Parameters = parameters ?? new Dictionary<string, object?>(), CompanyName = "Sample Company", UserName = "designer",
            });
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "(empty)";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return $"Error: {ex.Message}";
        }
    }

    /// <summary>Elements as clipboard text, to paste into this or another report (<see cref="Paste"/>).</summary>
    /// <param name="names">The elements.</param>
    /// <returns>The text (JSON), empty when nothing was chosen.</returns>
    public string Copy(IEnumerable<string> names)
    {
        List<(ReportElement Element, ReportBand Band)> chosen = Resolve(names);
        return chosen.Count == 0 ? string.Empty
            : ReportDefinitionSerializer.Serialize(new ReportDefinition { Code = "clipboard", Name = "clipboard", Bands = [new ReportBand { Elements = [.. chosen.Select(c => c.Element)] }] });
    }

    /// <summary>Pastes elements copied with <see cref="Copy"/> into a band, each renamed uniquely, stepped off its place and kept inside the band.</summary>
    /// <param name="bandType">The band to paste into.</param>
    /// <param name="clipboard">The clipboard text.</param>
    /// <returns>The pasted elements' names; empty when the text is not a copy.</returns>
    public IReadOnlyList<string> Paste(BandType bandType, string clipboard)
    {
        ReportDefinition copied;
        try
        {
            copied = ReportDefinitionSerializer.Deserialize(clipboard ?? string.Empty);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or ArgumentException or NotSupportedException)
        {
            return [];
        }

        List<ReportElement> elements = [.. copied.Bands.SelectMany(b => b.Elements)];
        if (elements.Count == 0)
        {
            return [];
        }

        ReportBand band = Definition.BandOfType(bandType) ?? AddBand(bandType);
        Checkpoint();
        List<string> names = [];
        double step = Grid > 0 ? Grid * 2 : 8;
        foreach (ReportElement element in elements)
        {
            element.Name = UniqueName(element.Kind.ToString());
            element.X = Snap(element.X + step);
            element.Y = Snap(element.Y + step);
            Contain(element, band);
            band.Elements.Add(element);
            names.Add(element.Name);
        }

        return names;
    }

    /// <summary>The fields a data source offers, from a sample row and the fields the report already uses.</summary>
    /// <param name="sample">A sample row (may be null when there is no data yet).</param>
    /// <returns>The field names, sorted.</returns>
    public IReadOnlyList<string> Fields(IReadOnlyDictionary<string, object?>? sample)
        => [.. (sample?.Keys ?? []).Concat(Definition.Bands.SelectMany(b => b.Elements).Select(e => e.FieldName ?? e.AggregateField).OfType<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];

    private static PropertyInfo? Property(string property)
        => EditableProperties.Contains(property, StringComparer.Ordinal) ? typeof(ReportElement).GetProperty(property) : null;

    private bool Swap(Stack<string> from, Stack<string> to)
    {
        if (from.Count == 0)
        {
            return false;
        }

        to.Push(ReportDefinitionSerializer.Serialize(Definition));
        Definition = ReportDefinitionSerializer.Deserialize(from.Pop());
        return true;
    }

    private void Checkpoint()
    {
        _undo.Push(ReportDefinitionSerializer.Serialize(Definition));
        _redo.Clear();
        if (_undo.Count > MaxUndo)
        {
            // Keep the newest changes: rebuild without the oldest.
            List<string> keep = [.. _undo.Take(MaxUndo)];
            _undo.Clear();
            for (int i = keep.Count - 1; i >= 0; i--)
            {
                _undo.Push(keep[i]);
            }
        }
    }

    private double Snap(double value) => Grid > 0 ? Math.Round(value / Grid) * Grid : Math.Round(value, 2);

    // Inside the band's height and the printable width.
    private void Contain(ReportElement element, ReportBand band)
    {
        double width = PrintableWidth;
        element.Width = Math.Min(element.Width, width);
        element.X = Math.Clamp(element.X, 0, Math.Max(0, width - element.Width));
        if (element.Height > band.Height)
        {
            band.Height = element.Height;
        }

        element.Y = Math.Clamp(element.Y, 0, Math.Max(0, band.Height - element.Height));
    }

    /// <summary>Groups the named elements: they move together from now on (brief §13.1 group / ungroup).</summary>
    /// <param name="names">The elements (two or more).</param>
    /// <returns>The group's name, or null when fewer than two elements were named.</returns>
    public string? Group(IEnumerable<string> names)
    {
        List<(ReportElement Element, ReportBand Band)> chosen = Resolve(names);
        if (chosen.Count < 2)
        {
            return null;
        }

        Checkpoint();
        string group = UniqueGroupName();
        foreach ((ReportElement element, _) in chosen)
        {
            element.GroupName = group;
        }

        return group;
    }

    /// <summary>Ungroups the named elements (every member of a group one of them belongs to).</summary>
    /// <param name="names">The elements.</param>
    /// <returns>How many elements were released.</returns>
    public int Ungroup(IEnumerable<string> names)
    {
        List<(ReportElement Element, ReportBand Band)> chosen = Resolve(WithGroups(names));
        List<ReportElement> grouped = [.. chosen.Select(c => c.Element).Where(e => !string.IsNullOrEmpty(e.GroupName))];
        if (grouped.Count == 0)
        {
            return 0;
        }

        Checkpoint();
        foreach (ReportElement element in grouped)
        {
            element.GroupName = null;
        }

        return grouped.Count;
    }

    /// <summary>The names given plus every element sharing a group with one of them.</summary>
    /// <param name="names">The names.</param>
    /// <returns>The widened names.</returns>
    public IReadOnlyList<string> WithGroups(IEnumerable<string> names)
    {
        List<string> listed = [.. names];
        HashSet<string> groups = [.. Resolve(listed).Select(c => c.Element.GroupName).Where(g => !string.IsNullOrEmpty(g))!];
        if (groups.Count == 0)
        {
            return listed;
        }

        HashSet<string> widened = new(listed, StringComparer.Ordinal);
        foreach (ReportElement element in Definition.Bands.SelectMany(b => b.Elements).Where(e => e.GroupName is string g && groups.Contains(g)))
        {
            widened.Add(element.Name);
        }

        return [.. widened];
    }

    /// <summary>The record sorts as text: <c>Expression</c> or <c>Expression desc</c>, separated by semicolons.</summary>
    /// <returns>The text (empty when the data's own order is kept).</returns>
    public string SortsText() => string.Join("; ", Definition.Sorts.Select(s => s.Descending ? $"{s.Expression} desc" : s.Expression));

    /// <summary>Sets the record sorts from text (see <see cref="SortsText"/>); blank clears them.</summary>
    /// <param name="text">The text.</param>
    /// <returns>Null, or why the text was refused.</returns>
    public string? SetSorts(string? text)
    {
        List<ReportSort> sorts = [];
        foreach (string part in (text ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            bool descending = part.EndsWith(" desc", StringComparison.OrdinalIgnoreCase);
            string expression = (descending ? part[..^5] : part.EndsWith(" asc", StringComparison.OrdinalIgnoreCase) ? part[..^4] : part).Trim();
            if (expression.Length == 0 || expression.Equals("desc", StringComparison.OrdinalIgnoreCase) || expression.Equals("asc", StringComparison.OrdinalIgnoreCase))
            {
                return "Each sort needs a field or expression, optionally followed by desc.";
            }

            sorts.Add(new ReportSort { Expression = expression, Descending = descending });
        }

        Checkpoint();
        Definition.Sorts = sorts;
        return null;
    }

    /// <summary>The groups as text: "Field; Other desc".</summary>
    /// <returns>The text.</returns>
    public string GroupsText() => string.Join("; ", Definition.Groups.Select(g => g.Descending ? $"{g.Expression} desc" : g.Expression));

    /// <summary>Sets the groups from text (see <see cref="GroupsText"/>); blank clears them. Group sections beyond the new count are removed.</summary>
    /// <param name="text">The text.</param>
    /// <returns>Null, or why the text was refused.</returns>
    public string? SetGroups(string? text)
    {
        List<ReportGroup> groups = [];
        foreach (string part in (text ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            bool descending = part.EndsWith(" desc", StringComparison.OrdinalIgnoreCase);
            string expression = (descending ? part[..^5] : part.EndsWith(" asc", StringComparison.OrdinalIgnoreCase) ? part[..^4] : part).Trim();
            if (expression.Length == 0 || expression.Equals("desc", StringComparison.OrdinalIgnoreCase) || expression.Equals("asc", StringComparison.OrdinalIgnoreCase))
            {
                return "Each group needs a field or expression, optionally followed by desc.";
            }

            ReportGroup existing = Definition.Groups.FirstOrDefault(g => string.Equals(g.Expression, expression, StringComparison.OrdinalIgnoreCase)) ?? new ReportGroup { Expression = expression };
            existing.Descending = descending;
            groups.Add(existing);
        }

        Checkpoint();
        Definition.Groups = groups;
        Definition.Bands.RemoveAll(b => b.Type is BandType.GroupHeader or BandType.GroupFooter && b.GroupLevel > groups.Count);
        return null;
    }

    /// <summary>Adds a group section for a group, adding the group when it is new (a header gets a field element for the group expression).</summary>
    /// <param name="type">GroupHeader or GroupFooter.</param>
    /// <param name="expression">The group's field or expression.</param>
    /// <returns>The section.</returns>
    public ReportBand AddGroupBand(BandType type, string expression)
    {
        if (type is not (BandType.GroupHeader or BandType.GroupFooter))
        {
            throw new ArgumentException("A group section is a GroupHeader or a GroupFooter.", nameof(type));
        }

        string key = (expression ?? string.Empty).Trim();
        if (key.Length == 0)
        {
            throw new ArgumentException("A group needs a field or expression.", nameof(expression));
        }

        Checkpoint();
        int level = Definition.Groups.FindIndex(g => string.Equals(g.Expression, key, StringComparison.OrdinalIgnoreCase)) + 1;
        if (level == 0)
        {
            Definition.Groups.Add(new ReportGroup { Expression = key });
            level = Definition.Groups.Count;
        }

        if (Definition.Bands.FirstOrDefault(b => b.Type == type && b.GroupLevel == level) is ReportBand existing)
        {
            return existing;
        }

        ReportBand band = new() { Type = type, GroupLevel = level, Height = 18 };
        if (type == BandType.GroupHeader)
        {
            band.Elements.Add(new ReportElement { Kind = ElementKind.Field, Name = UniqueName("Field"), FieldName = key, X = 0, Y = 1, Width = 160, Height = 16, Bold = true });
        }

        Definition.Bands.Add(band);
        Definition.Bands.Sort((a, b) => a.Type != b.Type ? ((int)a.Type).CompareTo((int)b.Type) : a.Type == BandType.GroupFooter ? b.GroupLevel.CompareTo(a.GroupLevel) : a.GroupLevel.CompareTo(b.GroupLevel));
        return band;
    }

    /// <summary>Sets the report's name and description (undoable).</summary>
    /// <param name="name">The name.</param>
    /// <param name="description">The description, or null to leave it.</param>
    /// <returns>Null, or why it was refused.</returns>
    public string? SetReportInfo(string? name, string? description)
    {
        string title = (name ?? string.Empty).Trim();
        if (title.Length == 0)
        {
            return "The report needs a name.";
        }

        Checkpoint();
        Definition.Name = title;
        if (description is not null)
        {
            Definition.Description = description.Trim().Length == 0 ? null : description.Trim();
        }

        return null;
    }

    /// <summary>Sets one property on several elements as one undoable step (the formatting toolbar).</summary>
    /// <param name="names">The elements.</param>
    /// <param name="property">The property.</param>
    /// <param name="value">The value as text.</param>
    /// <returns>Null, or the first problem.</returns>
    public string? SetPropertyOnAll(IEnumerable<string> names, string property, string? value)
    {
        List<string> list = [.. names];
        if (list.Count == 0)
        {
            return "Select an element first.";
        }

        int depth = _undo.Count;
        string? problem = null;
        foreach (string name in list)
        {
            problem ??= SetProperty(name, property, value);
        }

        // One undo step for the whole toolbar action: each element's checkpoint collapses into the first (the state before the action).
        while (_undo.Count > depth + 1)
        {
            _undo.Pop();
        }

        return problem;
    }

    private string UniqueGroupName()
    {
        HashSet<string> used = [.. Definition.Bands.SelectMany(b => b.Elements).Select(e => e.GroupName).Where(g => g is not null)!];
        int n = 1;
        while (used.Contains($"Group{n}"))
        {
            n++;
        }

        return $"Group{n}";
    }

    private List<(ReportElement Element, ReportBand Band)> Resolve(IEnumerable<string> names)
        => [.. names.Select(Find).Where(f => f.Element is not null).Select(f => (f.Element!, f.Band!))];

    private string UniqueName(string stem)
    {
        HashSet<string> used = [.. Definition.Bands.SelectMany(b => b.Elements).Select(e => e.Name)];
        int n = 1;
        while (used.Contains($"{stem}{n}"))
        {
            n++;
        }

        return $"{stem}{n}";
    }

    private void NameElements()
    {
        HashSet<string> used = new(StringComparer.Ordinal);
        foreach (ReportElement element in Definition.Bands.SelectMany(b => b.Elements))
        {
            if (string.IsNullOrWhiteSpace(element.Name) || !used.Add(element.Name))
            {
                element.Name = UniqueName(element.Kind.ToString());
                used.Add(element.Name);
            }
        }
    }
}
