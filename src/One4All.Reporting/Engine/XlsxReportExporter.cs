using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace One4All.Reporting.Engine;

/// <summary>
/// Exports report rows to a minimal, valid <c>.xlsx</c> (OpenXML SpreadsheetML) workbook - a single sheet
/// with a bold header row of the field names and one row per record. Numeric values are written as numbers so
/// Excel totals and formats them; everything else is written as an inline string. Built with the BCL
/// <see cref="ZipArchive"/> only, so the reporting layer takes no extra dependency.
/// </summary>
public sealed class XlsxReportExporter
{
    /// <summary>Exports rows to XLSX bytes.</summary>
    /// <param name="rows">The data rows.</param>
    /// <param name="sheetName">The worksheet name.</param>
    /// <returns>The .xlsx bytes.</returns>
    public byte[] Export(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, string sheetName = "Report")
    {
        List<string> columns = rows.Count == 0 ? [] : rows[0].Keys.ToList();
        string sheetXml = BuildSheetXml(columns, rows);

        using MemoryStream stream = new();
        using (ZipArchive zip = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, "[Content_Types].xml", ContentTypesXml);
            Write(zip, "_rels/.rels", RootRelsXml);
            Write(zip, "xl/workbook.xml", WorkbookXml(sheetName));
            Write(zip, "xl/_rels/workbook.xml.rels", WorkbookRelsXml);
            Write(zip, "xl/styles.xml", StylesXml);
            Write(zip, "xl/worksheets/sheet1.xml", sheetXml);
        }

        return stream.ToArray();
    }

    private static string BuildSheetXml(List<string> columns, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        StringBuilder sb = new();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");

        int rowIndex = 1;
        if (columns.Count > 0)
        {
            sb.Append(CultureInfo.InvariantCulture, $"<row r=\"{rowIndex}\">");
            for (int c = 0; c < columns.Count; c++)
            {
                AppendStringCell(sb, CellReference(c, rowIndex), columns[c], styleIndex: 1);
            }

            sb.Append("</row>");
            rowIndex++;
        }

        foreach (IReadOnlyDictionary<string, object?> row in rows)
        {
            sb.Append(CultureInfo.InvariantCulture, $"<row r=\"{rowIndex}\">");
            for (int c = 0; c < columns.Count; c++)
            {
                object? value = row.GetValueOrDefault(columns[c]);
                string reference = CellReference(c, rowIndex);
                if (TryNumber(value, out double number))
                {
                    sb.Append(CultureInfo.InvariantCulture, $"<c r=\"{reference}\"><v>{number.ToString("R", CultureInfo.InvariantCulture)}</v></c>");
                }
                else
                {
                    AppendStringCell(sb, reference, Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty, styleIndex: 0);
                }
            }

            sb.Append("</row>");
            rowIndex++;
        }

        sb.Append("</sheetData></worksheet>");
        return sb.ToString();
    }

    private static void AppendStringCell(StringBuilder sb, string reference, string value, int styleIndex)
    {
        string style = styleIndex > 0 ? $" s=\"{styleIndex}\"" : string.Empty;
        sb.Append(CultureInfo.InvariantCulture, $"<c r=\"{reference}\"{style} t=\"inlineStr\"><is><t xml:space=\"preserve\">");
        sb.Append(Escape(value));
        sb.Append("</t></is></c>");
    }

    private static bool TryNumber(object? value, out double number)
    {
        switch (value)
        {
            case null:
                number = 0;
                return false;
            case decimal d:
                number = (double)d;
                return true;
            case double db:
                number = db;
                return true;
            case float f:
                number = f;
                return true;
            case int i:
                number = i;
                return true;
            case long l:
                number = l;
                return true;
            default:
                number = 0;
                return false;
        }
    }

    private static string CellReference(int columnZeroBased, int rowOneBased)
    {
        // Convert a 0-based column index to A, B, …, Z, AA, …
        StringBuilder col = new();
        int n = columnZeroBased;
        do
        {
            col.Insert(0, (char)('A' + (n % 26)));
            n = (n / 26) - 1;
        }
        while (n >= 0);

        return $"{col}{rowOneBased}";
    }

    private static string Escape(string value)
    {
        StringBuilder sb = new(value.Length);
        foreach (char ch in value)
        {
            // Strip control characters that are invalid in XML 1.0, and escape the markup-significant ones.
            if (ch is '&') { sb.Append("&amp;"); }
            else if (ch is '<') { sb.Append("&lt;"); }
            else if (ch is '>') { sb.Append("&gt;"); }
            else if (ch is '\t' or '\n' or '\r' || ch >= ' ') { sb.Append(ch); }
        }

        return sb.ToString();
    }

    private static void Write(ZipArchive zip, string path, string content)
    {
        ZipArchiveEntry entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using StreamWriter writer = new(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private const string ContentTypesXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
        "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
        "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
        "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
        "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
        "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
        "</Types>";

    private const string RootRelsXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
        "</Relationships>";

    private static string WorkbookXml(string sheetName) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" " +
        "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
        $"<sheets><sheet name=\"{Escape(SafeSheetName(sheetName))}\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>";

    private const string WorkbookRelsXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
        "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
        "</Relationships>";

    // Two cell styles: index 0 = default; index 1 = bold (for the header row).
    private const string StylesXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
        "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>" +
        "<fills count=\"1\"><fill><patternFill patternType=\"none\"/></fill></fills>" +
        "<borders count=\"1\"><border/></borders>" +
        "<cellStyleXfs count=\"1\"><xf/></cellStyleXfs>" +
        "<cellXfs count=\"2\"><xf/><xf fontId=\"1\" applyFont=\"1\"/></cellXfs>" +
        "</styleSheet>";

    private static string SafeSheetName(string name)
    {
        string trimmed = string.IsNullOrWhiteSpace(name) ? "Report" : name.Trim();
        foreach (char invalid in new[] { '\\', '/', '?', '*', '[', ']', ':' })
        {
            trimmed = trimmed.Replace(invalid, ' ');
        }

        return trimmed.Length > 31 ? trimmed[..31] : trimmed;
    }
}
