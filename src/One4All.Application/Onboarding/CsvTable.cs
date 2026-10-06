using System.Globalization;
using One4All.Application.Banking;

namespace One4All.Application.Onboarding;

/// <summary>One data row of an import file, by column name.</summary>
/// <param name="LineNumber">The file line (1 is the header).</param>
/// <param name="Values">The values by column name (case-insensitive).</param>
public sealed record CsvRow(int LineNumber, IReadOnlyDictionary<string, string> Values)
{
    /// <summary>A column's trimmed value, or null when blank or absent.</summary>
    /// <param name="column">The column.</param>
    /// <returns>The value.</returns>
    public string? Text(string column) => Values.TryGetValue(column, out string? value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

    /// <summary>A column's number, or null when blank; throws <see cref="FormatException"/> when not a number.</summary>
    /// <param name="column">The column.</param>
    /// <returns>The number.</returns>
    public decimal? Number(string column)
        => Text(column) is not string text ? null
            : decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value) ? value
            : throw new FormatException($"'{column}' is not a number: {text}.");

    /// <summary>A column's ISO date (yyyy-MM-dd), or null when blank; throws <see cref="FormatException"/> otherwise.</summary>
    /// <param name="column">The column.</param>
    /// <returns>The date.</returns>
    public DateOnly? Date(string column)
        => Text(column) is not string text ? null
            : DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly value) ? value
            : throw new FormatException($"'{column}' is not a yyyy-MM-dd date: {text}.");
}

/// <summary>Reads a comma-separated import file with a header row (quoted fields allowed). Pure.</summary>
public static class CsvTable
{
    /// <summary>Reads the rows, skipping blank lines; the header names the columns.</summary>
    /// <param name="csv">The file content.</param>
    /// <param name="required">Columns that must be in the header.</param>
    /// <returns>The rows, or the missing columns.</returns>
    public static (IReadOnlyList<CsvRow> Rows, IReadOnlyList<string> MissingColumns) Read(string csv, params string[] required)
    {
        string[] lines = (csv ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (lines.Length == 0 || string.IsNullOrWhiteSpace(lines[0]))
        {
            return ([], required);
        }

        List<string> header = [.. BankStatementCsvParser.SplitFields(lines[0], ',').Select(h => h.Trim().ToLowerInvariant())];
        List<string> missing = [.. required.Where(r => !header.Contains(r.ToLowerInvariant()))];
        List<CsvRow> rows = [];
        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                continue;
            }

            List<string> fields = BankStatementCsvParser.SplitFields(lines[i], ',');
            Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
            for (int c = 0; c < header.Count; c++)
            {
                values[header[c]] = c < fields.Count ? fields[c] : string.Empty;
            }

            rows.Add(new CsvRow(i + 1, values));
        }

        return (rows, missing);
    }
}
