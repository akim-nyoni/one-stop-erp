using System.Globalization;
using One4All.Application.Banking;

namespace One4All.Application.Accounting;

/// <summary>One line of an imported journal.</summary>
/// <param name="LineNumber">The file line it came from.</param>
/// <param name="AccountCode">The account.</param>
/// <param name="CostCentre">The cost centre, if any.</param>
/// <param name="Department">The department, if any.</param>
/// <param name="Debit">The debit amount, or zero.</param>
/// <param name="Credit">The credit amount, or zero.</param>
/// <param name="Narrative">The line narrative, if any.</param>
public sealed record JournalCsvLine(int LineNumber, string AccountCode, string? CostCentre, string? Department, decimal Debit, decimal Credit, string? Narrative);

/// <summary>A journal read from a file: the lines sharing one reference.</summary>
/// <param name="Reference">The journal reference.</param>
/// <param name="Date">The posting date.</param>
/// <param name="Narrative">The first narrative given on its lines.</param>
/// <param name="Lines">Its lines, in file order.</param>
public sealed record ImportedJournal(string Reference, DateOnly Date, string? Narrative, IReadOnlyList<JournalCsvLine> Lines)
{
    /// <summary>The journal's total debits.</summary>
    public decimal TotalDebit => Lines.Sum(l => l.Debit);
}

/// <summary>A problem with a journal file.</summary>
/// <param name="LineNumber">The file line (1 is the header).</param>
/// <param name="Message">What is wrong.</param>
public sealed record JournalCsvError(int LineNumber, string Message);

/// <summary>The journals read from a file and any problems found.</summary>
/// <param name="Journals">The journals, in the order first seen.</param>
/// <param name="Errors">The problems; a file with any is imported not at all.</param>
public sealed record JournalCsvResult(IReadOnlyList<ImportedJournal> Journals, IReadOnlyList<JournalCsvError> Errors)
{
    /// <summary>Whether the file can be imported.</summary>
    public bool IsClean => Errors.Count == 0 && Journals.Count > 0;
}

/// <summary>
/// Reads manual journals from a CSV file (brief §4.4): a header row naming the columns, in any order — Reference, Date,
/// Account, Debit, Credit, and optionally CostCentre, Department and Narrative — then one row per line. Rows sharing a
/// reference form one journal, which must have one date and balance. Dates are yyyy-MM-dd or dd/MM/yyyy. Pure and
/// unit-tested.
/// </summary>
public static class JournalCsvParser
{
    private static readonly string[] Required = ["REFERENCE", "DATE", "ACCOUNT", "DEBIT", "CREDIT"];

    /// <summary>Parses a journal file.</summary>
    /// <param name="csv">The file content.</param>
    /// <returns>The journals and any problems.</returns>
    public static JournalCsvResult Parse(string csv)
    {
        ArgumentNullException.ThrowIfNull(csv);
        string[] rows = csv.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        List<JournalCsvError> errors = [];
        if (rows.Length == 0 || string.IsNullOrWhiteSpace(rows[0]))
        {
            return new JournalCsvResult([], [new JournalCsvError(1, "The file has no header row.")]);
        }

        Dictionary<string, int> columns = BankStatementCsvParser.SplitFields(rows[0], ',')
            .Select((name, index) => (Name: name.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant(), index))
            .GroupBy(c => c.Name).ToDictionary(g => g.Key, g => g.First().index);
        string[] missing = [.. Required.Where(r => !columns.ContainsKey(r))];
        if (missing.Length > 0)
        {
            return new JournalCsvResult([], [new JournalCsvError(1, $"The header is missing {string.Join(", ", missing)}.")]);
        }

        List<string> order = [];
        Dictionary<string, (DateOnly Date, List<JournalCsvLine> Lines, int FirstLine)> journals = new(StringComparer.Ordinal);
        for (int i = 1; i < rows.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(rows[i]))
            {
                continue;
            }

            int lineNumber = i + 1;
            List<string> fields = BankStatementCsvParser.SplitFields(rows[i], ',');
            string Field(string name) => columns.TryGetValue(name, out int index) && index < fields.Count ? fields[index].Trim() : string.Empty;

            string reference = Field("REFERENCE");
            string account = Field("ACCOUNT");
            if (reference.Length == 0 || account.Length == 0)
            {
                errors.Add(new JournalCsvError(lineNumber, "A line needs a reference and an account."));
                continue;
            }

            if (!DateOnly.TryParseExact(Field("DATE"), ["yyyy-MM-dd", "dd/MM/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date))
            {
                errors.Add(new JournalCsvError(lineNumber, $"'{Field("DATE")}' is not a date (yyyy-MM-dd or dd/MM/yyyy)."));
                continue;
            }

            if (!TryAmount(Field("DEBIT"), out decimal debit) || !TryAmount(Field("CREDIT"), out decimal credit))
            {
                errors.Add(new JournalCsvError(lineNumber, "The debit or credit is not an amount."));
                continue;
            }

            if ((debit > 0m) == (credit > 0m) || debit < 0m || credit < 0m)
            {
                errors.Add(new JournalCsvError(lineNumber, "A line needs a positive debit or a positive credit, not both."));
                continue;
            }

            if (!journals.TryGetValue(reference, out var journal))
            {
                journal = (date, [], lineNumber);
                journals[reference] = journal;
                order.Add(reference);
            }
            else if (journal.Date != date)
            {
                errors.Add(new JournalCsvError(lineNumber, $"Journal {reference} has lines on different dates."));
                continue;
            }

            journal.Lines.Add(new JournalCsvLine(lineNumber, account, Optional(Field("COSTCENTRE")), Optional(Field("DEPARTMENT")), debit, credit, Optional(Field("NARRATIVE"))));
        }

        List<ImportedJournal> result = [];
        foreach (string reference in order)
        {
            (DateOnly date, List<JournalCsvLine> lines, int firstLine) = journals[reference];
            decimal debits = lines.Sum(l => l.Debit);
            decimal credits = lines.Sum(l => l.Credit);
            if (debits != credits)
            {
                errors.Add(new JournalCsvError(firstLine, $"Journal {reference} does not balance: debits {debits:0.00}, credits {credits:0.00}."));
                continue;
            }

            result.Add(new ImportedJournal(reference, date, lines.Select(l => l.Narrative).FirstOrDefault(n => n is not null), lines));
        }

        if (result.Count == 0 && errors.Count == 0)
        {
            errors.Add(new JournalCsvError(2, "The file has no journal lines."));
        }

        return new JournalCsvResult(result, [.. errors.OrderBy(e => e.LineNumber)]);
    }

    private static bool TryAmount(string text, out decimal value)
    {
        if (text.Length == 0)
        {
            value = 0m;
            return true;
        }

        return decimal.TryParse(text.Replace(",", string.Empty, StringComparison.Ordinal), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }

    private static string? Optional(string value) => value.Length == 0 ? null : value;
}
