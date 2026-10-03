using One4All.Application.Banking;

namespace One4All.Application.Trading;

/// <summary>The result of reading pasted supplier-statement text.</summary>
/// <param name="Lines">The statement lines read.</param>
/// <param name="Errors">Lines that could not be read, with their line numbers.</param>
public sealed record SupplierStatementParseResult(IReadOnlyList<SupplierStatementLine> Lines, IReadOnlyList<BankCsvError> Errors)
{
    /// <summary>True when every non-blank line was read.</summary>
    public bool IsClean => Errors.Count == 0;
}

/// <summary>
/// Reads a supplier statement pasted or exported as delimited text — one line per document:
/// <c>DocumentNumber, Date, Amount</c> (invoices positive, credits / payments negative). Uses the tested bank
/// CSV parser, so quoting, thousands separators and bracketed negatives behave the same way. Pure.
/// </summary>
public static class SupplierStatementText
{
    /// <summary>Parses the text.</summary>
    /// <param name="text">The pasted text.</param>
    /// <param name="dateFormat">The date format (default <c>yyyy-MM-dd</c>).</param>
    /// <param name="hasHeader">Whether the first line is a header.</param>
    /// <param name="delimiter">The delimiter (default comma; tab when pasting from a spreadsheet).</param>
    /// <returns>The lines and any errors.</returns>
    public static SupplierStatementParseResult Parse(string text, string dateFormat = "yyyy-MM-dd", bool hasHeader = false, char delimiter = ',')
    {
        BankCsvLayout layout = new()
        {
            Delimiter = delimiter,
            HasHeader = hasHeader,
            DescriptionColumn = 0, // the document number
            DateColumn = 1,
            DateFormat = dateFormat,
            AmountColumn = 2,
        };

        BankCsvParseResult parsed = BankStatementCsvParser.Parse(text, layout);
        List<BankCsvError> errors = [.. parsed.Errors];
        List<SupplierStatementLine> lines = [];
        int index = 0;
        foreach (var line in parsed.Lines)
        {
            index++;
            if (string.IsNullOrWhiteSpace(line.Description))
            {
                errors.Add(new BankCsvError(0, $"Statement line {index} has no document number."));
                continue;
            }

            lines.Add(new SupplierStatementLine(line.Description, line.StatementDate, line.Amount));
        }

        return new SupplierStatementParseResult(lines, errors);
    }
}
