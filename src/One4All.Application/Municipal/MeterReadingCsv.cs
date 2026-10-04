using System.Globalization;
using One4All.Application.Banking;

namespace One4All.Application.Municipal;

/// <summary>A reading from an imported file.</summary>
/// <param name="LineNumber">The line in the file.</param>
/// <param name="Serial">The meter serial (upper case).</param>
/// <param name="Date">The reading date.</param>
/// <param name="Reading">The register value.</param>
/// <param name="TamperSuspected">Whether the reader flagged tampering.</param>
public sealed record MeterReadingCsvRow(int LineNumber, string Serial, DateOnly Date, decimal Reading, bool TamperSuspected);

/// <summary>A line of an imported file that could not be used.</summary>
/// <param name="LineNumber">The line in the file.</param>
/// <param name="Message">What is wrong with it.</param>
public sealed record MeterReadingCsvError(int LineNumber, string Message);

/// <summary>The rows and errors of an imported readings file.</summary>
/// <param name="Rows">The usable readings, in file order.</param>
/// <param name="Errors">The lines that could not be used.</param>
public sealed record MeterReadingCsvResult(IReadOnlyList<MeterReadingCsvRow> Rows, IReadOnlyList<MeterReadingCsvError> Errors);

/// <summary>
/// Parses meter readings exported by handheld or mobile reading apps (brief §4.9): a header with SERIAL, DATE and
/// READING columns in any order and an optional TAMPER column (Y / YES / TRUE / 1); dates as yyyy-MM-dd or dd/MM/yyyy.
/// Bad lines are reported and the rest kept. Pure and unit-tested.
/// </summary>
public static class MeterReadingCsvParser
{
    private static readonly string[] Required = ["SERIAL", "DATE", "READING"];

    /// <summary>Parses a file.</summary>
    /// <param name="csv">The file's text.</param>
    /// <returns>The readings and the lines that could not be used.</returns>
    public static MeterReadingCsvResult Parse(string csv)
    {
        ArgumentNullException.ThrowIfNull(csv);
        string[] rows = csv.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (rows.Length == 0 || string.IsNullOrWhiteSpace(rows[0]))
        {
            return new MeterReadingCsvResult([], [new MeterReadingCsvError(1, "The file has no header row.")]);
        }

        Dictionary<string, int> columns = BankStatementCsvParser.SplitFields(rows[0], ',')
            .Select((name, index) => (Name: name.Trim().ToUpperInvariant(), index))
            .GroupBy(c => c.Name).ToDictionary(g => g.Key, g => g.First().index);
        string[] missing = [.. Required.Where(r => !columns.ContainsKey(r))];
        if (missing.Length > 0)
        {
            return new MeterReadingCsvResult([], [new MeterReadingCsvError(1, $"The header is missing {string.Join(", ", missing)}.")]);
        }

        List<MeterReadingCsvRow> readings = [];
        List<MeterReadingCsvError> errors = [];
        for (int i = 1; i < rows.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(rows[i]))
            {
                continue;
            }

            int line = i + 1;
            List<string> fields = BankStatementCsvParser.SplitFields(rows[i], ',');
            string Field(string name) => columns.TryGetValue(name, out int index) && index < fields.Count ? fields[index].Trim() : string.Empty;

            string serial = Field("SERIAL").ToUpperInvariant();
            if (serial.Length == 0)
            {
                errors.Add(new MeterReadingCsvError(line, "A line needs a meter serial."));
                continue;
            }

            if (!DateOnly.TryParseExact(Field("DATE"), ["yyyy-MM-dd", "dd/MM/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date))
            {
                errors.Add(new MeterReadingCsvError(line, $"'{Field("DATE")}' is not a date (yyyy-MM-dd or dd/MM/yyyy)."));
                continue;
            }

            if (!decimal.TryParse(Field("READING"), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal reading) || reading < 0m)
            {
                errors.Add(new MeterReadingCsvError(line, $"'{Field("READING")}' is not a reading."));
                continue;
            }

            string tamper = Field("TAMPER").ToUpperInvariant();
            readings.Add(new MeterReadingCsvRow(line, serial, date, reading, tamper is "Y" or "YES" or "TRUE" or "1"));
        }

        return new MeterReadingCsvResult(readings, errors);
    }
}
