using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using One4All.Application.Abstractions.Banking;

namespace One4All.Application.Banking;

/// <summary>The bank statement file formats that can be imported (brief §4.7).</summary>
public enum StatementFileFormat
{
    /// <summary>Delimited text, read with a <see cref="BankCsvLayout"/>.</summary>
    Csv = 1,

    /// <summary>SWIFT MT940 customer statement.</summary>
    Mt940 = 2,

    /// <summary>ISO 20022 camt.053 bank-to-customer statement (XML).</summary>
    Camt053 = 3,

    /// <summary>Open Financial Exchange statement, SGML (OFX 1.x) or XML (OFX 2.x).</summary>
    Ofx = 4,
}

/// <summary>
/// Reads SWIFT MT940 statements: each <c>:61:</c> statement line (value date, debit / credit mark, amount with a decimal
/// comma, transaction type, customer reference) with the <c>:86:</c> narrative that follows it. Credits are money in
/// (positive), debits money out (negative); reversals (<c>RC</c> / <c>RD</c>) flip the sign. Pure and unit-tested.
/// </summary>
public static partial class Mt940Parser
{
    /// <summary>Parses an MT940 statement.</summary>
    /// <param name="text">The file content.</param>
    /// <returns>The lines read and any errors, as for CSV.</returns>
    public static BankCsvParseResult Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        List<StatementLineImport> lines = [];
        List<BankCsvError> errors = [];

        // Fields start with :tag: at the start of a line; continuation lines belong to the field before.
        List<(string Tag, string Value, int LineNumber)> fields = [];
        string[] rows = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (int i = 0; i < rows.Length; i++)
        {
            string row = rows[i];
            Match tag = FieldStart().Match(row);
            if (tag.Success)
            {
                fields.Add((tag.Groups["tag"].Value, row[tag.Length..], i + 1));
            }
            else if (fields.Count > 0 && row.Length > 0 && row != "-" && !row.StartsWith('{'))
            {
                (string t, string v, int n) = fields[^1];
                fields[^1] = (t, v + " " + row.Trim(), n);
            }
        }

        for (int i = 0; i < fields.Count; i++)
        {
            if (fields[i].Tag != "61")
            {
                continue;
            }

            Match line = StatementLine().Match(fields[i].Value.Trim());
            if (!line.Success)
            {
                errors.Add(new BankCsvError(fields[i].LineNumber, "The :61: statement line is not in the MT940 layout."));
                continue;
            }

            if (!DateOnly.TryParseExact(line.Groups["date"].Value, "yyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date)
                || !decimal.TryParse(line.Groups["amount"].Value.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal amount))
            {
                errors.Add(new BankCsvError(fields[i].LineNumber, "The value date or amount cannot be read."));
                continue;
            }

            bool moneyIn = line.Groups["mark"].Value is "C" or "RD";
            string reference = line.Groups["ref"].Value.Trim();
            string description = i + 1 < fields.Count && fields[i + 1].Tag == "86" ? fields[i + 1].Value.Trim() : reference;
            lines.Add(new StatementLineImport(date, description, moneyIn ? amount : -amount,
                reference.Length == 0 || reference.Equals("NONREF", StringComparison.OrdinalIgnoreCase) ? null : reference));
        }

        return new BankCsvParseResult(lines, errors);
    }

    [GeneratedRegex(@"^:(?<tag>\d{2}[A-Z]?):")]
    private static partial Regex FieldStart();

    [GeneratedRegex(@"^(?<date>\d{6})(?<entry>\d{4})?(?<mark>RC|RD|C|D)(?<funds>[A-Z])?(?<amount>\d+,\d{0,2})(?<type>[NFS][A-Z0-9]{3})(?<ref>[^/]*)(//(?<bank>.*))?$")]
    private static partial Regex StatementLine();
}

/// <summary>
/// Reads OFX statements, both the SGML of OFX 1.x (leaf elements without closing tags) and the XML of OFX 2.x: each
/// <c>STMTTRN</c> with its posting date, signed amount, cheque or reference number (else the bank's transaction id) and
/// name / memo. Pure and unit-tested.
/// </summary>
public static partial class OfxParser
{
    /// <summary>Parses an OFX statement.</summary>
    /// <param name="text">The file content.</param>
    /// <returns>The lines read and any errors, as for CSV.</returns>
    public static BankCsvParseResult Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        List<StatementLineImport> lines = [];
        List<BankCsvError> errors = [];
        foreach (Match transaction in Transaction().Matches(text))
        {
            int lineNumber = text[..transaction.Index].Count(c => c == '\n') + 1;
            Dictionary<string, string> fields = new(StringComparer.OrdinalIgnoreCase);
            foreach (Match field in Field().Matches(transaction.Groups["body"].Value))
            {
                fields.TryAdd(field.Groups["tag"].Value, System.Net.WebUtility.HtmlDecode(field.Groups["value"].Value.Trim()));
            }

            string? posted = fields.GetValueOrDefault("DTPOSTED");
            if (posted is null || posted.Length < 8
                || !DateOnly.TryParseExact(posted[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date)
                || !decimal.TryParse(fields.GetValueOrDefault("TRNAMT"), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal amount))
            {
                errors.Add(new BankCsvError(lineNumber, "The transaction's posting date or amount cannot be read."));
                continue;
            }

            string? reference = new[] { "CHECKNUM", "REFNUM", "FITID" }.Select(fields.GetValueOrDefault).FirstOrDefault(v => !string.IsNullOrEmpty(v));
            string description = string.Join(" ", new[] { "NAME", "MEMO" }.Select(fields.GetValueOrDefault).Where(v => !string.IsNullOrEmpty(v)));
            lines.Add(new StatementLineImport(date, description.Length == 0 ? reference ?? string.Empty : description, amount, reference));
        }

        if (lines.Count == 0 && errors.Count == 0 && !text.Contains("<OFX", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(new BankCsvError(1, "The file is not an OFX statement."));
        }

        return new BankCsvParseResult(lines, errors);
    }

    [GeneratedRegex(@"<STMTTRN>(?<body>.*?)</STMTTRN>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex Transaction();

    // A leaf element: its value runs to the next tag or line end, whether or not it is closed (SGML or XML).
    [GeneratedRegex(@"<(?<tag>[A-Z0-9.]+)>(?<value>[^<\r\n]*)", RegexOptions.IgnoreCase)]
    private static partial Regex Field();
}

/// <summary>
/// Reads ISO 20022 camt.053 statements: each <c>Ntry</c> (entry) with its amount, credit / debit indicator, booking
/// date (else value date), reference (end-to-end id, else the bank's reference) and remittance / additional information.
/// Any camt.053 version is accepted - elements are matched by local name. Pure and unit-tested.
/// </summary>
public static class Camt053Parser
{
    /// <summary>Parses a camt.053 statement.</summary>
    /// <param name="xml">The file content.</param>
    /// <returns>The lines read and any errors, as for CSV.</returns>
    public static BankCsvParseResult Parse(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);
        List<StatementLineImport> lines = [];
        List<BankCsvError> errors = [];

        XDocument document;
        try
        {
            document = XDocument.Parse(xml, LoadOptions.SetLineInfo);
        }
        catch (System.Xml.XmlException ex)
        {
            return new BankCsvParseResult([], [new BankCsvError(ex.LineNumber, "The file is not valid XML.")]);
        }

        foreach (XElement entry in document.Descendants().Where(e => e.Name.LocalName == "Ntry"))
        {
            int lineNumber = ((System.Xml.IXmlLineInfo)entry).LineNumber;
            string? amountText = Child(entry, "Amt")?.Value;
            string? indicator = Child(entry, "CdtDbtInd")?.Value;
            string? dateText = Child(Child(entry, "BookgDt"), "Dt")?.Value ?? Child(Child(entry, "ValDt"), "Dt")?.Value
                ?? Child(Child(entry, "BookgDt"), "DtTm")?.Value?[..10];
            if (amountText is null || indicator is null || dateText is null
                || !decimal.TryParse(amountText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal amount)
                || !DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date))
            {
                errors.Add(new BankCsvError(lineNumber, "The entry's amount, credit/debit indicator or date cannot be read."));
                continue;
            }

            bool moneyIn = indicator == "CRDT";
            if (Child(entry, "RvslInd")?.Value == "true")
            {
                moneyIn = !moneyIn;
            }

            XElement? details = entry.Descendants().FirstOrDefault(e => e.Name.LocalName == "TxDtls");
            string? endToEnd = details?.Descendants().FirstOrDefault(e => e.Name.LocalName == "EndToEndId")?.Value;
            string? reference = endToEnd is { Length: > 0 } && endToEnd != "NOTPROVIDED" ? endToEnd : Child(entry, "AcctSvcrRef")?.Value;
            string description = string.Join(" ", (details ?? entry).Descendants().Where(e => e.Name.LocalName == "Ustrd").Select(e => e.Value.Trim()));
            if (description.Length == 0)
            {
                description = Child(entry, "AddtlNtryInf")?.Value.Trim() ?? reference ?? string.Empty;
            }

            lines.Add(new StatementLineImport(date, description, moneyIn ? amount : -amount, reference));
        }

        return new BankCsvParseResult(lines, errors);
    }

    private static XElement? Child(XElement? parent, string localName) => parent?.Elements().FirstOrDefault(e => e.Name.LocalName == localName);
}
