using System.Text;

namespace One4All.Application.Communications;

/// <summary>A rendered message.</summary>
/// <param name="Subject">The subject.</param>
/// <param name="Body">The body.</param>
/// <param name="MissingFields">Merge fields the template uses that were not supplied (the message should not be sent).</param>
public sealed record RenderedMessage(string Subject, string Body, IReadOnlyList<string> MissingFields);

/// <summary>
/// Message templates with merge fields (brief §8): <c>{{Field}}</c> placeholders are replaced by the supplied values
/// (field names ignore case); <c>{{{{</c> writes a literal <c>{{</c>; every placeholder without a value is reported, so a
/// half-merged letter never goes out. Pure and unit-tested.
/// </summary>
public static class TemplateRenderer
{
    /// <summary>The merge fields a template uses, in first-use order.</summary>
    /// <param name="text">The template text.</param>
    /// <returns>The field names.</returns>
    public static IReadOnlyList<string> Fields(string text)
    {
        List<string> fields = [];
        Merge(text ?? string.Empty, name =>
        {
            if (!fields.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                fields.Add(name);
            }

            return string.Empty;
        });
        return fields;
    }

    /// <summary>Renders a template's subject and body.</summary>
    /// <param name="subject">The subject template.</param>
    /// <param name="body">The body template.</param>
    /// <param name="values">The merge values.</param>
    /// <returns>The message and any fields left unmerged.</returns>
    public static RenderedMessage Render(string subject, string body, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        Dictionary<string, string> lookup = new(values, StringComparer.OrdinalIgnoreCase);
        List<string> missing = [];
        string Value(string name)
        {
            if (lookup.TryGetValue(name, out string? value))
            {
                return value;
            }

            if (!missing.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                missing.Add(name);
            }

            return "{{" + name + "}}";
        }

        return new RenderedMessage(Merge(subject ?? string.Empty, Value), Merge(body ?? string.Empty, Value), missing);
    }

    private static string Merge(string text, Func<string, string> value)
    {
        StringBuilder output = new(text.Length);
        int i = 0;
        while (i < text.Length)
        {
            if (string.CompareOrdinal(text, i, "{{{{", 0, 4) == 0)
            {
                output.Append("{{");
                i += 4;
                continue;
            }

            if (string.CompareOrdinal(text, i, "{{", 0, 2) == 0)
            {
                int close = text.IndexOf("}}", i + 2, StringComparison.Ordinal);
                string name = close < 0 ? string.Empty : text[(i + 2)..close].Trim();
                if (close > 0 && name.Length > 0 && name.All(c => char.IsLetterOrDigit(c) || c is '_' or '.'))
                {
                    output.Append(value(name));
                    i = close + 2;
                    continue;
                }
            }

            output.Append(text[i]);
            i++;
        }

        return output.ToString();
    }
}
