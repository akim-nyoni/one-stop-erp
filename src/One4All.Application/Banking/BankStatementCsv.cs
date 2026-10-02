using System.Globalization;
using One4All.Application.Abstractions.Banking;

namespace One4All.Application.Banking;

/// <summary>
/// Describes how a bank's CSV export is laid out, so the same parser serves every bank. Columns are
/// zero-based indexes. Either a single signed <see cref="AmountColumn"/> or a <see cref="DebitColumn"/> /
/// <see cref="CreditColumn"/> pair must be given.
/// </summary>
public sealed record BankCsvLayout
{
    /// <summary>The field delimiter (default comma).</summary>
    public char Delimiter { get; init; } = ',';

    /// <summary>Whether the first row is a header to skip (default true).</summary>
    public bool HasHeader { get; init; } = true;

    /// <summary>The date column.</summary>
    public int DateColumn { get; init; }

    /// <summary>The .NET date format of the date column (default <c>yyyy-MM-dd</c>).</summary>
    public string DateFormat { get; init; } = "yyyy-MM-dd";

    /// <summary>The description / narrative column.</summary>
    public int DescriptionColumn { get; init; } = 1;

    /// <summary>The optional reference column.</summary>
    public int? ReferenceColumn { get; init; }

    /// <summary>A single signed amount column (money in positive, money out negative); null when debit/credit columns are used.</summary>
    public int? AmountColumn { get; init; }

    /// <summary>The money-out column (always positive in the file; imported negative); used with <see cref="CreditColumn"/>.</summary>
    public int? DebitColumn { get; init; }

    /// <summary>The money-in column; used with <see cref="DebitColumn"/>.</summary>
    public int? CreditColumn { get; init; }

    /// <summary>A common layout: Date, Description, Amount, Reference with ISO dates.</summary>
    public static BankCsvLayout Simple { get; } = new() { DateColumn = 0, DescriptionColumn = 1, AmountColumn = 2, ReferenceColumn = 3 };

    /// <summary>Checks the layout names an amount source.</summary>
    /// <returns>True when a signed amount column or both debit and credit columns are set.</returns>
    public bool IsValid => AmountColumn is not null || (DebitColumn is not null && CreditColumn is not null);
}

/// <summary>A line the parser could not read.</summary>
/// <param name="LineNumber">The 1-based line number in the file.</param>
/// <param name="Reason">What was wrong.</param>
public sealed record BankCsvError(int LineNumber, string Reason);

/// <summary>The result of parsing a statement CSV.</summary>
/// <param name="Lines">The lines parsed.</param>
/// <param name="Errors">The lines rejected.</param>
public sealed record BankCsvParseResult(IReadOnlyList<StatementLineImport> Lines, IReadOnlyList<BankCsvError> Errors)
{
    /// <summary>True when every data line parsed.</summary>
    public bool IsClean => Errors.Count == 0;
}

/// <summary>
/// Parses a bank statement CSV export into statement lines according to a <see cref="BankCsvLayout"/>.
/// Handles quoted fields (with doubled quotes), thousands separators, bracketed negatives and blank lines;
/// bad lines are reported, not thrown. Pure and unit-tested.
/// </summary>
public static class BankStatementCsvParser
{
    /// <summary>Parses the CSV text.</summary>
    /// <param name="csv">The file content.</param>
    /// <param name="layout">The bank's layout.</param>
    /// <returns>The parsed lines and any errors.</returns>
    public static BankCsvParseResult Parse(string csv, BankCsvLayout layout)
    {
        List<StatementLineImport> lines = [];
        List<BankCsvError> errors = [];
        if (!layout.IsValid)
        {
            errors.Add(new BankCsvError(0, "The layout must name an amount column or both debit and credit columns."));
            return new BankCsvParseResult(lines, errors);
        }

        string[] rows = csv.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (int i = 0; i < rows.Length; i++)
        {
            int lineNumber = i + 1;
            string row = rows[i];
            if (string.IsNullOrWhiteSpace(row) || (i == 0 && layout.HasHeader))
            {
                continue;
            }

            List<string> fields = SplitFields(row, layout.Delimiter);
            string? problem = TryParse(fields, layout, out StatementLineImport line);
            if (problem is null)
            {
                lines.Add(line);
            }
            else
            {
                errors.Add(new BankCsvError(lineNumber, problem));
            }
        }

        return new BankCsvParseResult(lines, errors);
    }

    private static string? TryParse(List<string> fields, BankCsvLayout layout, out StatementLineImport line)
    {
        line = default;

        if (!TryField(fields, layout.DateColumn, out string dateText))
        {
            return $"Missing date column {layout.DateColumn}.";
        }

        if (!DateOnly.TryParseExact(dateText.Trim(), layout.DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date))
        {
            return $"Date '{dateText}' is not in the format {layout.DateFormat}.";
        }

        string description = TryField(fields, layout.DescriptionColumn, out string d) ? d.Trim() : string.Empty;
        string? reference = layout.ReferenceColumn is { } rc && TryField(fields, rc, out string r) && r.Trim().Length > 0 ? r.Trim() : null;

        decimal amount;
        if (layout.AmountColumn is { } ac)
        {
            if (!TryField(fields, ac, out string amountText) || !TryMoney(amountText, out amount))
            {
                return $"Amount column {ac} is missing or not a number.";
            }
        }
        else
        {
            TryField(fields, layout.DebitColumn!.Value, out string debitText);
            TryField(fields, layout.CreditColumn!.Value, out string creditText);
            bool hasDebit = TryMoney(debitText, out decimal debit) && debit != 0m;
            bool hasCredit = TryMoney(creditText, out decimal credit) && credit != 0m;
            if (!hasDebit && !hasCredit)
            {
                if (!string.IsNullOrWhiteSpace(debitText) && !TryMoney(debitText, out _) || !string.IsNullOrWhiteSpace(creditText) && !TryMoney(creditText, out _))
                {
                    return "Debit / credit column is not a number.";
                }

                return "Neither debit nor credit has a value.";
            }

            amount = (hasCredit ? Math.Abs(credit) : 0m) - (hasDebit ? Math.Abs(debit) : 0m);
        }

        if (amount == 0m)
        {
            return "The amount is zero.";
        }

        line = new StatementLineImport(date, description, amount, reference);
        return null;
    }

    private static bool TryField(List<string> fields, int index, out string value)
    {
        if (index >= 0 && index < fields.Count)
        {
            value = fields[index];
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool TryMoney(string text, out decimal value)
    {
        value = 0m;
        string t = text.Trim();
        if (t.Length == 0)
        {
            return false;
        }

        bool negative = false;
        if (t.StartsWith('(') && t.EndsWith(')'))
        {
            negative = true;
            t = t[1..^1];
        }

        t = t.Replace(",", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal);
        if (!decimal.TryParse(t, NumberStyles.Number | NumberStyles.AllowLeadingSign | NumberStyles.AllowTrailingSign, CultureInfo.InvariantCulture, out value))
        {
            return false;
        }

        if (negative)
        {
            value = -Math.Abs(value);
        }

        return true;
    }

    // RFC-4180-style split: quoted fields may contain the delimiter and doubled quotes.
    private static List<string> SplitFields(string row, char delimiter)
    {
        List<string> fields = [];
        System.Text.StringBuilder current = new();
        bool inQuotes = false;
        for (int i = 0; i < row.Length; i++)
        {
            char c = row[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < row.Length && row[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == delimiter)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return fields;
    }
}
