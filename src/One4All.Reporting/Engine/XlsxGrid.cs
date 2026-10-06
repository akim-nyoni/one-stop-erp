using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace One4All.Reporting.Engine;

/// <summary>A formula cell for <see cref="XlsxGrid.Write"/> (without the leading equals sign).</summary>
/// <param name="Expression">The formula, e.g. <c>SUM(D3:O3)</c>.</param>
public sealed record XlsxFormula(string Expression);

/// <summary>
/// Reads and writes a plain grid of cells as a one-sheet <c>.xlsx</c> workbook, for files people edit in Excel and send back (budget
/// templates and the like). Writing: numbers as numbers, <see cref="XlsxFormula"/> as formulas, everything else as text, the first
/// <c>boldRows</c> rows bold. Reading: the first worksheet, as text per cell (numbers in invariant culture), understanding the shared
/// strings, inline strings, formula results and booleans Excel writes. BCL only.
/// </summary>
public static class XlsxGrid
{
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRel = "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>Writes a grid as a workbook.</summary>
    /// <param name="sheetName">The worksheet name.</param>
    /// <param name="rows">The rows of cells.</param>
    /// <param name="boldRows">How many leading rows are bold.</param>
    /// <returns>The .xlsx bytes.</returns>
    public static byte[] Write(string sheetName, IReadOnlyList<IReadOnlyList<object?>> rows, int boldRows = 0)
    {
        ArgumentNullException.ThrowIfNull(rows);
        XElement sheetData = new(Main + "sheetData");
        for (int r = 0; r < rows.Count; r++)
        {
            XElement row = new(Main + "row", new XAttribute("r", r + 1));
            for (int c = 0; c < rows[r].Count; c++)
            {
                object? value = rows[r][c];
                if (value is null)
                {
                    continue;
                }

                XElement cell = new(Main + "c", new XAttribute("r", Reference(c, r + 1)));
                if (r < boldRows)
                {
                    cell.Add(new XAttribute("s", 1));
                }

                switch (value)
                {
                    case XlsxFormula formula:
                        cell.Add(new XElement(Main + "f", formula.Expression));
                        break;
                    case decimal d:
                        cell.Add(new XElement(Main + "v", d.ToString("G29", CultureInfo.InvariantCulture)));
                        break;
                    case double or float or int or long:
                        cell.Add(new XElement(Main + "v", Convert.ToString(value, CultureInfo.InvariantCulture)));
                        break;
                    default:
                        cell.Add(new XAttribute("t", "inlineStr"), new XElement(Main + "is", new XElement(Main + "t", new XAttribute(XNamespace.Xml + "space", "preserve"),
                            Clean(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty))));
                        break;
                }

                row.Add(cell);
            }

            sheetData.Add(row);
        }

        XElement sheet = new(Main + "worksheet", sheetData);

        // Formulas carry no cached value, so Excel recalculates on opening.
        XElement workbook = new(Main + "workbook", new XAttribute(XNamespace.Xmlns + "r", Rel.NamespaceName),
            new XElement(Main + "sheets", new XElement(Main + "sheet", new XAttribute("name", SheetName(sheetName)), new XAttribute("sheetId", 1), new XAttribute(Rel + "id", "rId1"))),
            new XElement(Main + "calcPr", new XAttribute("fullCalcOnLoad", 1)));

        using MemoryStream stream = new();
        using (ZipArchive zip = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Put(zip, "[Content_Types].xml", ContentTypes);
            Put(zip, "_rels/.rels", RootRels);
            Put(zip, "xl/workbook.xml", Declaration + workbook.ToString(SaveOptions.DisableFormatting));
            Put(zip, "xl/_rels/workbook.xml.rels", WorkbookRels);
            Put(zip, "xl/styles.xml", Styles);
            Put(zip, "xl/worksheets/sheet1.xml", Declaration + sheet.ToString(SaveOptions.DisableFormatting));
        }

        return stream.ToArray();
    }

    /// <summary>Reads the first worksheet of a workbook as text cells (rows and columns as laid out, gaps as null).</summary>
    /// <param name="xlsx">The workbook bytes.</param>
    /// <returns>The rows.</returns>
    /// <exception cref="InvalidDataException">Thrown when the bytes are not a workbook.</exception>
    public static IReadOnlyList<IReadOnlyList<string?>> Read(byte[] xlsx)
    {
        ArgumentNullException.ThrowIfNull(xlsx);
        using ZipArchive zip = new(new MemoryStream(xlsx), ZipArchiveMode.Read);
        XDocument workbook = Load(zip, "xl/workbook.xml") ?? throw new InvalidDataException("Not an Excel workbook.");
        string relId = workbook.Descendants(Main + "sheet").FirstOrDefault()?.Attribute(Rel + "id")?.Value ?? throw new InvalidDataException("The workbook has no sheets.");
        string target = Load(zip, "xl/_rels/workbook.xml.rels")?.Descendants(PackageRel + "Relationship").FirstOrDefault(r => r.Attribute("Id")?.Value == relId)?.Attribute("Target")?.Value
            ?? "worksheets/sheet1.xml";
        string sheetPath = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
        XDocument sheet = Load(zip, sheetPath) ?? throw new InvalidDataException("The first worksheet is missing.");
        List<string> shared = Load(zip, "xl/sharedStrings.xml")?.Root?.Elements(Main + "si").Select(si => string.Concat(si.Descendants(Main + "t").Select(t => t.Value))).ToList() ?? [];

        List<List<string?>> rows = [];
        foreach (XElement row in sheet.Descendants(Main + "row"))
        {
            int r = int.TryParse(row.Attribute("r")?.Value, out int rn) ? rn - 1 : rows.Count;
            while (rows.Count <= r)
            {
                rows.Add([]);
            }

            int next = 0;
            foreach (XElement cell in row.Elements(Main + "c"))
            {
                int c = cell.Attribute("r")?.Value is string reference ? Column(reference) : next;
                next = c + 1;
                List<string?> cells = rows[r];
                while (cells.Count <= c)
                {
                    cells.Add(null);
                }

                cells[c] = Value(cell, shared);
            }
        }

        return rows;
    }

    private static string? Value(XElement cell, List<string> shared)
    {
        string? type = cell.Attribute("t")?.Value;
        string? v = cell.Element(Main + "v")?.Value;
        return type switch
        {
            "s" => int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) && i >= 0 && i < shared.Count ? shared[i] : null,
            "inlineStr" => string.Concat(cell.Descendants(Main + "t").Select(t => t.Value)),
            "b" => v == "1" ? "TRUE" : "FALSE",
            _ => v,
        };
    }

    // "AB12" → 27 (zero-based column).
    private static int Column(string reference)
    {
        int column = 0;
        foreach (char ch in reference.TakeWhile(char.IsLetter))
        {
            column = (column * 26) + (char.ToUpperInvariant(ch) - 'A' + 1);
        }

        return column - 1;
    }

    private static string Reference(int column, int row)
    {
        StringBuilder letters = new();
        for (int n = column; n >= 0; n = (n / 26) - 1)
        {
            letters.Insert(0, (char)('A' + (n % 26)));
        }

        return $"{letters}{row}";
    }

    private static string Clean(string value) => new([.. value.Where(ch => ch is '\t' or '\n' or '\r' || ch >= ' ')]);

    private static string SheetName(string name)
    {
        string trimmed = string.IsNullOrWhiteSpace(name) ? "Sheet1" : name.Trim();
        foreach (char invalid in "\\/?*[]:")
        {
            trimmed = trimmed.Replace(invalid, ' ');
        }

        return trimmed.Length > 31 ? trimmed[..31] : trimmed;
    }

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

    private static void Put(ZipArchive zip, string path, string content)
    {
        using StreamWriter writer = new(zip.CreateEntry(path, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private const string Declaration = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>";

    private const string ContentTypes =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
        "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
        "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
        "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
        "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
        "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
        "</Types>";

    private const string RootRels =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
        "</Relationships>";

    private const string WorkbookRels =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
        "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
        "</Relationships>";

    private const string Styles =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
        "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>" +
        "<fills count=\"1\"><fill><patternFill patternType=\"none\"/></fill></fills>" +
        "<borders count=\"1\"><border/></borders>" +
        "<cellStyleXfs count=\"1\"><xf/></cellStyleXfs>" +
        "<cellXfs count=\"2\"><xf/><xf fontId=\"1\" applyFont=\"1\"/></cellXfs>" +
        "</styleSheet>";
}
