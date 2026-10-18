using System.Globalization;
using System.Text;

namespace One4All.Application.Payroll;

/// <summary>One variable read from an import file, before it is checked against the employee and the run.</summary>
/// <param name="Line">The line number in the file.</param>
/// <param name="EmployeeNumber">The employee number.</param>
/// <param name="ElementCode">The element code.</param>
/// <param name="Units">Units, when given.</param>
/// <param name="Amount">Amount, when given.</param>
/// <param name="Error">Why the cell or line cannot be read, or null.</param>
public sealed record ParsedVariable(int Line, string EmployeeNumber, string ElementCode, decimal? Units, decimal? Amount, string? Error);

/// <summary>
/// Reads a variable-input file (brief §4.15.6) in any of three shapes: the template without a header ("employee number, element code, units, amount");
/// the same with named columns in any order (Employee, Element, Units or Hours, Amount; a Name column is ignored); or a sheet with one row per employee
/// and a column per pay element (each filled cell a variable: units for a per-unit element, otherwise an amount). Commas, semicolons or tabs separate
/// the cells; a quoted cell may hold the separator ("1,250.00"). Pure and unit-tested.
/// </summary>
public static class PayrollVariableCsv
{
    private static readonly string[] EmployeeNames = ["EMPLOYEE", "EMPLOYEENUMBER", "EMPLOYEENO", "EMPNO", "NUMBER", "STAFFNUMBER"];
    private static readonly string[] ElementNames = ["ELEMENT", "ELEMENTCODE", "CODE", "PAYELEMENT"];
    private static readonly string[] UnitNames = ["UNITS", "HOURS", "QUANTITY", "QTY", "DAYS"];
    private static readonly string[] AmountNames = ["AMOUNT", "VALUE"];
    private static readonly string[] IgnoredNames = ["NAME", "EMPLOYEENAME", "FULLNAME", "DEPARTMENT", "NOTE", "NOTES"];

    /// <summary>Reads the file.</summary>
    /// <param name="csv">The file's text.</param>
    /// <param name="isElement">Whether a code is a pay element (for a sheet's columns).</param>
    /// <param name="isPerUnit">Whether an element is paid per unit (a sheet's cell is then units).</param>
    /// <param name="isEmployee">Whether a number is an employee's (so a headerless file's first line is not taken for a header).</param>
    /// <returns>Every variable read, in file order, with its error when it cannot be read.</returns>
    public static IReadOnlyList<ParsedVariable> Parse(string csv, Func<string, bool> isElement, Func<string, bool> isPerUnit, Func<string, bool> isEmployee)
    {
        ArgumentNullException.ThrowIfNull(isElement);
        ArgumentNullException.ThrowIfNull(isPerUnit);
        ArgumentNullException.ThrowIfNull(isEmployee);
        List<(int Line, string[] Cells)> rows = [];
        string[] lines = (csv ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        char separator = Separator(lines.FirstOrDefault(l => l.Trim().Length > 0) ?? string.Empty);
        for (int n = 0; n < lines.Length; n++)
        {
            if (lines[n].Trim().Length > 0)
            {
                rows.Add((n + 1, Cells(lines[n], separator)));
            }
        }

        if (rows.Count == 0)
        {
            return [];
        }

        string[] header = [.. rows[0].Cells.Select(Key)];
        int employee = Find(header, EmployeeNames);
        if (employee < 0)
        {
            return Positional(rows, isEmployee);
        }

        int element = Find(header, ElementNames), units = Find(header, UnitNames), amount = Find(header, AmountNames);
        return element >= 0 ? Named(rows.Skip(1), employee, element, units, amount) : Sheet(rows, employee, isElement, isPerUnit);
    }

    // No header (or a header the template's way): employee, element, units, amount by position; a first line that is not data is skipped.
    private static List<ParsedVariable> Positional(List<(int Line, string[] Cells)> rows, Func<string, bool> isEmployee)
    {
        List<ParsedVariable> read = [];
        for (int i = 0; i < rows.Count; i++)
        {
            (int line, string[] cells) = rows[i];
            string number = cells.ElementAtOrDefault(0) ?? string.Empty;
            bool unitsOk = TryNumber(cells.ElementAtOrDefault(2), out decimal? units), amountOk = TryNumber(cells.ElementAtOrDefault(3), out decimal? amount);
            if (i == 0 && (!unitsOk || !amountOk || (units is null && amount is null)) && !isEmployee(number))
            {
                continue;
            }

            read.Add(new ParsedVariable(line, number, (cells.ElementAtOrDefault(1) ?? string.Empty).ToUpperInvariant(), units, amount, unitsOk && amountOk ? null : "Units and amount must be numbers."));
        }

        return read;
    }

    // Named columns in any order.
    private static List<ParsedVariable> Named(IEnumerable<(int Line, string[] Cells)> rows, int employee, int element, int unitsAt, int amountAt)
    {
        List<ParsedVariable> read = [];
        foreach ((int line, string[] cells) in rows)
        {
            bool unitsOk = TryNumber(unitsAt < 0 ? null : cells.ElementAtOrDefault(unitsAt), out decimal? units);
            bool amountOk = TryNumber(amountAt < 0 ? null : cells.ElementAtOrDefault(amountAt), out decimal? amount);
            read.Add(new ParsedVariable(line, cells.ElementAtOrDefault(employee) ?? string.Empty, (cells.ElementAtOrDefault(element) ?? string.Empty).ToUpperInvariant(), units, amount,
                unitsOk && amountOk ? null : "Units and amount must be numbers."));
        }

        return read;
    }

    // A sheet: a row per employee, a column per element; blank cells are nothing to pay.
    private static List<ParsedVariable> Sheet(List<(int Line, string[] Cells)> rows, int employee, Func<string, bool> isElement, Func<string, bool> isPerUnit)
    {
        List<ParsedVariable> read = [];
        string[] codes = [.. rows[0].Cells.Select(c => c.Trim().ToUpperInvariant())];
        List<int> columns = [];
        for (int c = 0; c < codes.Length; c++)
        {
            if (c == employee || codes[c].Length == 0 || IgnoredNames.Contains(Key(codes[c])))
            {
                continue;
            }

            if (!isElement(codes[c]))
            {
                read.Add(new ParsedVariable(rows[0].Line, string.Empty, codes[c], null, null, $"Column '{rows[0].Cells[c].Trim()}' is not a pay element."));
                continue;
            }

            columns.Add(c);
        }

        foreach ((int line, string[] cells) in rows.Skip(1))
        {
            string number = cells.ElementAtOrDefault(employee) ?? string.Empty;
            foreach (int c in columns)
            {
                string? text = cells.ElementAtOrDefault(c);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                bool ok = TryNumber(text, out decimal? value);
                bool perUnit = isPerUnit(codes[c]);
                read.Add(new ParsedVariable(line, number, codes[c], perUnit ? value : null, perUnit ? null : value, ok ? null : $"'{text.Trim()}' under {codes[c]} is not a number."));
            }
        }

        return read;
    }

    // Tab when the first line has one, else a semicolon when it has those and no commas, else a comma.
    private static char Separator(string first) => first.Contains('\t', StringComparison.Ordinal) ? '\t' : first.Contains(';', StringComparison.Ordinal) && !first.Contains(',', StringComparison.Ordinal) ? ';' : ',';

    // A line's cells: quoted cells may hold the separator and doubled quotes.
    private static string[] Cells(string line, char separator)
    {
        List<string> cells = [];
        StringBuilder cell = new();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (c == separator && !quoted)
            {
                cells.Add(cell.ToString().Trim());
                cell.Clear();
            }
            else
            {
                cell.Append(c);
            }
        }

        cells.Add(cell.ToString().Trim());
        return [.. cells];
    }

    private static string Key(string header) => new([.. header.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant)]);

    private static int Find(string[] header, string[] names) => Array.FindIndex(header, h => names.Contains(h));

    private static bool TryNumber(string? text, out decimal? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        // Commas only as thousands separators (1,250.50); "7,5" is a decimal comma, not 75.
        string trimmed = text.Trim();
        if (trimmed.Contains(',', StringComparison.Ordinal) && !global::System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^-?\d{1,3}(,\d{3})+(\.\d+)?$"))
        {
            return false;
        }

        if (decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }
}
