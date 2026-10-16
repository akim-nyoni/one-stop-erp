using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace One4All.Reporting.Model;

/// <summary>
/// A company's house style for every report (brief §13; distinct from the designer's ReportTheme palette, which restyles one layout at design time: fonts, colours, logo and footer applied across all reports in one click). Each
/// setting is optional: an unset one leaves the report's own design alone. Applied to a rendered report after layout, so a report's
/// explicit choices (a cell's own font or colour) win over the theme's defaults.
/// </summary>
public sealed partial class ReportHouseStyle
{
    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>Gets or sets the font family for text that uses the standard font.</summary>
    public string? FontFamily { get; set; }

    /// <summary>Gets or sets the size for text that uses the standard size (9 pt).</summary>
    public double? BaseFontSize { get; set; }

    /// <summary>Gets or sets the colour of report titles (bold text of 12 pt or more in the report header), as #RRGGBB.</summary>
    public string? TitleColour { get; set; }

    /// <summary>Gets or sets the background of column-heading strips (coloured labels in page and group headers), as #RRGGBB.</summary>
    public string? HeadingBackground { get; set; }

    /// <summary>Gets or sets the text colour of column-heading strips, as #RRGGBB.</summary>
    public string? HeadingForeground { get; set; }

    /// <summary>Gets or sets the colour of lines and rectangles, as #RRGGBB.</summary>
    public string? AccentColour { get; set; }

    /// <summary>Gets or sets a line printed at the foot of every page (an address, a registration number, a slogan).</summary>
    public string? FooterText { get; set; }

    /// <summary>Gets or sets a value indicating whether the company logo is placed top-right on reports that do not place it themselves.</summary>
    public bool ShowLogo { get; set; }

    /// <summary>Gets a value indicating whether the theme changes anything.</summary>
    [JsonIgnore]
    public bool IsEmpty => FontFamily is null && BaseFontSize is null && TitleColour is null && HeadingBackground is null && HeadingForeground is null && AccentColour is null
        && string.IsNullOrWhiteSpace(FooterText) && !ShowLogo;

    /// <summary>Reads a theme from its JSON, or null when there is none.</summary>
    /// <param name="json">The stored JSON.</param>
    /// <returns>The theme, or null.</returns>
    public static ReportHouseStyle? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            ReportHouseStyle? theme = JsonSerializer.Deserialize<ReportHouseStyle>(json, Json);
            return theme is null || theme.IsEmpty ? null : theme;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The theme as JSON for storing.</summary>
    /// <returns>The JSON.</returns>
    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>Whether a colour is a #RRGGBB value.</summary>
    /// <param name="colour">The text.</param>
    /// <returns>True when well formed.</returns>
    public static bool IsColour(string? colour) => colour is not null && Hex().IsMatch(colour);

    /// <summary>The settings that are not well-formed colours, for a message.</summary>
    /// <returns>The names, empty when all is well.</returns>
    public IReadOnlyList<string> BadColours()
    {
        List<string> bad = [];
        foreach ((string name, string? value) in new[] { ("Title colour", TitleColour), ("Heading background", HeadingBackground), ("Heading text colour", HeadingForeground), ("Accent colour", AccentColour) })
        {
            if (value is not null && !IsColour(value))
            {
                bad.Add(name);
            }
        }

        return bad;
    }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex Hex();
}
