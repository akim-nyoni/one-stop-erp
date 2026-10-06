using System.Globalization;

namespace One4All.Application.Workflow;

/// <summary>
/// Stage condition expressions (brief §7 rules): clauses <c>field op value</c> joined by <c>and</c> (binding tighter) and <c>or</c>.
/// Operators: <c>= != &gt; &gt;= &lt; &lt;=</c> and <c>in</c> with a comma list (<c>Department in IT, HR</c>). <c>Value</c> is the document
/// value; other fields come from the document's attributes (missing ones are blank). Numbers compare as numbers, text case-insensitively.
/// Pure.
/// </summary>
public static class WorkflowCondition
{
    private static readonly string[] Operators = [">=", "<=", "!=", "=", ">", "<"];

    /// <summary>Whether a condition holds (a blank condition always does).</summary>
    /// <param name="condition">The expression.</param>
    /// <param name="value">The document value.</param>
    /// <param name="attributes">The document attributes.</param>
    /// <returns>True when it holds.</returns>
    public static bool Matches(string? condition, decimal value, IReadOnlyDictionary<string, string> attributes)
        => string.IsNullOrWhiteSpace(condition)
            || Split(condition, " or ").Any(any => Split(any, " and ").All(clause => Clause(clause, value, attributes) ?? false));

    /// <summary>Why a condition cannot be read, or null when it can.</summary>
    /// <param name="condition">The expression.</param>
    /// <returns>The problem, or null.</returns>
    public static string? Problem(string? condition)
    {
        if (string.IsNullOrWhiteSpace(condition))
        {
            return null;
        }

        string? bad = Split(condition, " or ").SelectMany(any => Split(any, " and ")).FirstOrDefault(c => Clause(c, 0m, new Dictionary<string, string>()) is null);
        return bad is null ? null : $"'{bad.Trim()}' is not field op value (op: = != > >= < <= in).";
    }

    private static IEnumerable<string> Split(string text, string separator)
        => text.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).SelectMany(p => p.Split(separator.ToUpperInvariant(), StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    // The clause's truth, or null when it cannot be read.
    private static bool? Clause(string clause, decimal value, IReadOnlyDictionary<string, string> attributes)
    {
        int inAt = clause.IndexOf(" in ", StringComparison.OrdinalIgnoreCase);
        if (inAt > 0)
        {
            string actual = Field(clause[..inAt].Trim(), value, attributes);
            string[] options = clause[(inAt + 4)..].Trim().Trim('(', ')').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return options.Length == 0 ? null : options.Any(o => Compare(actual, o) == 0);
        }

        foreach (string op in Operators)
        {
            int at = clause.IndexOf(op, StringComparison.Ordinal);
            if (at <= 0)
            {
                continue;
            }

            string field = clause[..at].Trim(), expected = clause[(at + op.Length)..].Trim().Trim('\'', '"');
            if (field.Length == 0 || field.Any(char.IsWhiteSpace) || expected.Length == 0)
            {
                return null;
            }

            int comparison = Compare(Field(field, value, attributes), expected);
            return op switch
            {
                "=" => comparison == 0,
                "!=" => comparison != 0,
                ">" => comparison > 0,
                ">=" => comparison >= 0,
                "<" => comparison < 0,
                _ => comparison <= 0,
            };
        }

        return null;
    }

    private static string Field(string name, decimal value, IReadOnlyDictionary<string, string> attributes)
        => string.Equals(name, "Value", StringComparison.OrdinalIgnoreCase) ? value.ToString(CultureInfo.InvariantCulture)
            : attributes.FirstOrDefault(a => string.Equals(a.Key, name, StringComparison.OrdinalIgnoreCase)).Value ?? string.Empty;

    private static int Compare(string actual, string expected)
        => decimal.TryParse(actual, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal a) && decimal.TryParse(expected, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal e)
            ? a.CompareTo(e)
            : string.Compare(actual.Trim(), expected.Trim(), StringComparison.OrdinalIgnoreCase);
}
