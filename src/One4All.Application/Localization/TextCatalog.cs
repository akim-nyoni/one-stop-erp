using System.Globalization;
using System.Reflection;
using System.Text.Json;
using One4All.Core.Common;

namespace One4All.Application.Localization;

/// <summary>
/// The user-interface text in other languages (brief §15: every UI string from resource files), keyed by the English text so an
/// untranslated string simply shows in English. Translations ship as embedded JSON files (<c>Localization/Strings/strings.{culture}.json</c>);
/// a culture falls back to its neutral language (pt-MZ → pt), then English. Company terminology overrides are applied to whatever is shown.
/// </summary>
public sealed class TextCatalog
{
    private static readonly Lazy<Dictionary<string, Dictionary<string, string>>> Translations = new(Load);
    private readonly Dictionary<string, string>? _specific;
    private readonly Dictionary<string, string>? _neutral;
    private readonly IReadOnlyDictionary<string, string> _terms;

    private TextCatalog(CultureInfo culture, IReadOnlyDictionary<string, string> terms)
    {
        Culture = culture;
        _terms = terms;
        Translations.Value.TryGetValue(culture.Name, out _specific);
        Translations.Value.TryGetValue(culture.TwoLetterISOLanguageName, out _neutral);
    }

    /// <summary>Gets the English catalog with no terminology.</summary>
    public static TextCatalog English { get; } = new(CultureInfo.GetCultureInfo("en"), new Dictionary<string, string>());

    /// <summary>Gets the cultures with translations shipped.</summary>
    public static IReadOnlyList<string> Cultures => [.. Translations.Value.Keys.Order(StringComparer.Ordinal)];

    /// <summary>Gets the culture.</summary>
    public CultureInfo Culture { get; }

    /// <summary>Gets a value indicating whether text runs right to left in this culture.</summary>
    public bool RightToLeft => Culture.TextInfo.IsRightToLeft;

    /// <summary>The text in the catalog's language with the company's terms.</summary>
    /// <param name="english">The English text (the key).</param>
    /// <returns>The text to show.</returns>
    public string this[string english] => Terminology.Apply(Lookup(english), _terms);

    /// <summary>A catalog for a culture (null or unknown: English) and a company's terminology.</summary>
    /// <param name="culture">The culture name (e.g. "pt-MZ").</param>
    /// <param name="terminology">Term → replacement.</param>
    /// <returns>The catalog.</returns>
    public static TextCatalog For(string? culture, IReadOnlyDictionary<string, string>? terminology = null)
    {
        CultureInfo info;
        try
        {
            info = string.IsNullOrWhiteSpace(culture) ? CultureInfo.GetCultureInfo("en") : CultureInfo.GetCultureInfo(culture.Trim());
        }
        catch (CultureNotFoundException)
        {
            info = CultureInfo.GetCultureInfo("en");
        }

        return new TextCatalog(info, terminology ?? new Dictionary<string, string>());
    }

    /// <summary>The English texts that have no translation in a culture (for translators).</summary>
    /// <param name="culture">The culture.</param>
    /// <param name="english">The English texts in use.</param>
    /// <returns>The missing ones.</returns>
    public static IReadOnlyList<string> Missing(string culture, IEnumerable<string> english)
    {
        ArgumentNullException.ThrowIfNull(english);
        Translations.Value.TryGetValue(culture, out Dictionary<string, string>? table);
        return [.. english.Distinct(StringComparer.Ordinal).Where(e => table is null || !table.ContainsKey(e))];
    }

    private string Lookup(string english)
        => _specific?.GetValueOrDefault(english) ?? _neutral?.GetValueOrDefault(english) ?? english;

    private static Dictionary<string, Dictionary<string, string>> Load()
    {
        Assembly assembly = typeof(TextCatalog).Assembly;
        Dictionary<string, Dictionary<string, string>> all = new(StringComparer.OrdinalIgnoreCase);
        foreach (string name in assembly.GetManifestResourceNames().Where(n => n.Contains(".Localization.Strings.strings.", StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal)))
        {
            string culture = name[(name.IndexOf(".strings.", StringComparison.Ordinal) + ".strings.".Length)..^".json".Length];
            using Stream stream = assembly.GetManifestResourceStream(name)!;
            all[culture] = JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
        }

        return all;
    }
}
