using System.Text.RegularExpressions;

namespace One4All.Core.Common;

/// <summary>
/// Company terminology (brief §13.1: "Stand" vs "Erf" vs "Property"): whole words replaced wherever text is shown, singular and plural,
/// keeping a leading capital.
/// </summary>
public static class Terminology
{
    /// <summary>Applies the terms to text.</summary>
    /// <param name="text">The text.</param>
    /// <param name="terms">Term → replacement (e.g. Stand → Erf).</param>
    /// <returns>The text with the company's words.</returns>
    public static string Apply(string text, IReadOnlyDictionary<string, string>? terms)
    {
        if (string.IsNullOrEmpty(text) || terms is null || terms.Count == 0)
        {
            return text;
        }

        foreach ((string term, string replacement) in terms.Where(t => t.Key.Length > 0).OrderByDescending(t => t.Key.Length))
        {
            text = Regex.Replace(text, $@"\b{Regex.Escape(term)}(s?)\b", m =>
            {
                string word = replacement + m.Groups[1].Value;
                return char.IsUpper(m.Value[0]) ? char.ToUpperInvariant(word[0]) + word[1..] : char.ToLowerInvariant(word[0]) + word[1..];
            }, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        }

        return text;
    }
}
