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
        List<(ReportElement Element, ReportBand Band)> chosen = Resolve(names);
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
        => Find(name).Element is ReportElement element && Property(property) is PropertyInfo info
            ? Convert.ToString(info.GetValue(element), CultureInfo.InvariantCulture)
            : null;

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

    /// <summary>Applies a theme: the font everywhere, sizes by band, and the page header's colours.</summary>
    /// <param name="theme">The theme.</param>
    public void ApplyTheme(ReportTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        Checkpoint();
        foreach (ReportBand band in Definition.Bands)
        {
            foreach (ReportElement element in band.Elements.Where(e => e.Kind is not (ElementKind.Line or ElementKind.Rectangle or ElementKind.Image)))
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
