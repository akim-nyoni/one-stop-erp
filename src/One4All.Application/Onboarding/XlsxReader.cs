using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace One4All.Application.Onboarding;

/// <summary>
/// Reads the first (or a named) worksheet of an Excel workbook (.xlsx) as CSV text, so the migration templates can be filled in Excel and loaded as they
/// are (brief §11: "import from Excel or CSV"). Shared and inline strings, numbers, booleans and dates (cells styled as dates, as yyyy-MM-dd) are read;
/// empty cells keep their columns; formulas give their cached values.
/// </summary>
public static class XlsxReader
{
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRel = "http://schemas.openxmlformats.org/package/2006/relationships";

    // The built-in number formats that are dates (ECMA-376 18.8.30).
    private static readonly HashSet<int> DateFormats = [14, 15, 16, 17, 18, 19, 20, 21, 22, 45, 46, 47];

    /// <summary>Whether the bytes look like an .xlsx workbook (a zip file).</summary>
    /// <param name="data">The file's bytes.</param>
    /// <returns>True for a zip (PK) signature.</returns>
    public static bool IsWorkbook(byte[] data) => data is { Length: > 4 } && data[0] == (byte)'P' && data[1] == (byte)'K';

    /// <summary>The worksheet as CSV.</summary>
    /// <param name="data">The workbook's bytes.</param>
    /// <param name="sheetName">The sheet (null: the first).</param>
    /// <returns>The CSV text.</returns>
    public static string ToCsv(byte[] data, string? sheetName = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        using ZipArchive zip = new(new MemoryStream(data), ZipArchiveMode.Read);
        XDocument workbook = Load(zip, "xl/workbook.xml") ?? throw new InvalidDataException("Not an Excel workbook (no xl/workbook.xml).");
        List<XElement> sheets = [.. workbook.Descendants(Main + "sheet")];
        XElement sheet = (sheetName is null ? sheets.FirstOrDefault() : sheets.FirstOrDefault(s => string.Equals((string?)s.Attribute("name"), sheetName, StringComparison.OrdinalIgnoreCase)))
            ?? throw new InvalidDataException(sheetName is null ? "The workbook has no sheets." : $"The workbook has no sheet {sheetName}.");
        string relationship = (string?)sheet.Attribute(Rel + "id") ?? string.Empty;
        string target = Load(zip, "xl/_rels/workbook.xml.rels")?.Descendants(PackageRel + "Relationship")
            .Where(r => (string?)r.Attribute("Id") == relationship).Select(r => (string?)r.Attribute("Target")).FirstOrDefault() ?? "worksheets/sheet1.xml";
        string path = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;

        List<string> shared = [.. (Load(zip, "xl/sharedStrings.xml")?.Root?.Elements(Main + "si") ?? []).Select(si => string.Concat(si.Descendants(Main + "t").Select(t => t.Value)))];
        HashSet<int> dateStyles = DateStyles(Load(zip, "xl/styles.xml"));
        XDocument worksheet = Load(zip, path) ?? throw new InvalidDataException($"The workbook has no {path}.");

        StringBuilder csv = new();
        foreach (XElement row in worksheet.Descendants(Main + "row"))
        {
            List<string> cells = [];
            foreach (XElement cell in row.Elements(Main + "c"))
            {
                int column = Column((string?)cell.Attribute("r"), cells.Count);
                while (cells.Count < column)
                {
                    cells.Add(string.Empty);
                }

                cells.Add(Value(cell, shared, dateStyles));
            }

            while (cells.Count > 0 && cells[^1].Length == 0)
            {
                cells.RemoveAt(cells.Count - 1);
            }

            if (cells.Count > 0)
            {
                csv.AppendLine(string.Join(',', cells.Select(Quote)));
            }
        }

        return csv.ToString();
    }

    private static string Value(XElement cell, List<string> shared, HashSet<int> dateStyles)
    {
        string type = (string?)cell.Attribute("t") ?? "n";
        string raw = cell.Element(Main + "v")?.Value ?? string.Empty;
        return type switch
        {
            "s" => int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) && index < shared.Count ? shared[index] : string.Empty,
            "inlineStr" => string.Concat(cell.Descendants(Main + "t").Select(t => t.Value)),
            "b" => raw == "1" ? "true" : "false",
            "str" or "e" => raw,
            _ when int.TryParse((string?)cell.Attribute("s"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int style) && dateStyles.Contains(style)
                && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double serial)
                => DateOnly.FromDateTime(DateTime.FromOADate(serial)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            _ => decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal number) ? number.ToString(CultureInfo.InvariantCulture) : raw,
        };
    }

    // The cell styles whose number format is a date (built-in, or a custom format with day / month / year codes).
    private static HashSet<int> DateStyles(XDocument? styles)
    {
        if (styles?.Root is null)
        {
            return [];
        }

        HashSet<int> dateFormats = [.. DateFormats];
        foreach (XElement format in styles.Root.Element(Main + "numFmts")?.Elements(Main + "numFmt") ?? [])
        {
            string code = ((string?)format.Attribute("formatCode") ?? string.Empty).ToLowerInvariant();
            if ((code.Contains('d') || code.Contains('y')) && !code.Contains('h') && int.TryParse((string?)format.Attribute("numFmtId"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
            {
                dateFormats.Add(id);
            }
        }

        HashSet<int> result = [];
        int index = 0;
        foreach (XElement xf in styles.Root.Element(Main + "cellXfs")?.Elements(Main + "xf") ?? [])
        {
            if (int.TryParse((string?)xf.Attribute("numFmtId"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) && dateFormats.Contains(id))
            {
                result.Add(index);
            }

            index++;
        }

        return result;
    }

    // The zero-based column of a reference such as "C7" (the next column when there is none).
    private static int Column(string? reference, int next)
    {
        if (string.IsNullOrEmpty(reference))
        {
            return next;
        }

        int column = 0;
        foreach (char ch in reference.TakeWhile(char.IsLetter))
        {
            column = (column * 26) + (char.ToUpperInvariant(ch) - 'A' + 1);
        }

        return column == 0 ? next : column - 1;
    }

    private static string Quote(string value)
        => value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : value;

    private static XDocument? Load(ZipArchive zip, string path)
    {
        ZipArchiveEntry? entry = zip.GetEntry(path);
        if (entry is null)
        {
            return null;
        }

        using Stream stream = entry.Open();
        return XDocument.Load(stream);
    }
}
