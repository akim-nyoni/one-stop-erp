using System.Globalization;
using System.Text;

namespace One4All.Licensing.Generator.Vendor;

/// <summary>A line of the licence certificate PDF.</summary>
/// <param name="Text">The text.</param>
/// <param name="Style">The style.</param>
public sealed record CertificateLine(string Text, CertificateStyle Style = CertificateStyle.Body);

/// <summary>How a certificate line is set.</summary>
public enum CertificateStyle
{
    /// <summary>Body text (Helvetica 10).</summary>
    Body,

    /// <summary>A heading (Helvetica bold 16).</summary>
    Title,

    /// <summary>A section heading (Helvetica bold 11).</summary>
    Heading,

    /// <summary>Fixed-width (Courier 8), for the activation string.</summary>
    Code,

    /// <summary>A warning (Helvetica bold 11, red).</summary>
    Warning,
}

/// <summary>
/// Writes the licence certificate as an A4 PDF (brief §11.4 "printable licence certificate PDF"): the lines down the left of the page, and the
/// verification QR code drawn as vector squares in the top-right corner, so it prints sharp at any size. Plain PDF 1.4 with the standard fonts.
/// </summary>
public static class CertificateLayout
{
    private const int PageWidth = 595;
    private const int PageHeight = 842;
    private const int Margin = 40;
    private const int QrSize = 130;

    /// <summary>Builds the PDF.</summary>
    /// <param name="lines">The lines, in order.</param>
    /// <param name="qrModules">The QR code's modules (true: dark), row by row, or null for none.</param>
    /// <returns>The PDF bytes.</returns>
    public static byte[] Build(IReadOnlyList<CertificateLine> lines, IReadOnlyList<IReadOnlyList<bool>>? qrModules)
    {
        ArgumentNullException.ThrowIfNull(lines);
        List<StringBuilder> pages = [new()];
        double y = PageHeight - Margin;
        foreach (CertificateLine line in lines)
        {
            (string font, int size, int leading) = line.Style switch
            {
                CertificateStyle.Title => ("F2", 16, 24),
                CertificateStyle.Heading or CertificateStyle.Warning => ("F2", 11, 18),
                CertificateStyle.Code => ("F3", 8, 10),
                _ => ("F1", 10, 14),
            };
            if (y - leading < Margin)
            {
                pages.Add(new StringBuilder());
                y = PageHeight - Margin;
            }

            y -= leading;
            string colour = line.Style == CertificateStyle.Warning ? "0.72 0.11 0.11 rg" : "0 0 0 rg";
            pages[^1].Append(CultureInfo.InvariantCulture, $"BT {colour} /{font} {size} Tf {Margin} {y:0.##} Td ({Escape(line.Text)}) Tj ET\n");
        }

        if (qrModules is { Count: > 0 })
        {
            int count = qrModules.Count;
            double cell = (double)QrSize / count;
            double left = PageWidth - Margin - QrSize, top = PageHeight - Margin;
            StringBuilder qr = new("0 0 0 rg\n");
            for (int row = 0; row < count; row++)
            {
                for (int column = 0; column < qrModules[row].Count; column++)
                {
                    if (qrModules[row][column])
                    {
                        qr.Append(CultureInfo.InvariantCulture, $"{left + (column * cell):0.###} {top - ((row + 1) * cell):0.###} {cell:0.###} {cell:0.###} re f\n");
                    }
                }
            }

            pages[0].Insert(0, qr.ToString());
        }

        using MemoryStream pdf = new();
        List<long> offsets = [];
        void Write(string s) => pdf.Write(Encoding.Latin1.GetBytes(s));
        void Object(int number, string body)
        {
            offsets.Add(pdf.Position);
            Write($"{number} 0 obj\n{body}\nendobj\n");
        }

        Write("%PDF-1.4\n%âãÏÓ\n");
        int firstPage = 6;
        string kids = string.Join(' ', Enumerable.Range(0, pages.Count).Select(i => $"{firstPage + (i * 2)} 0 R"));
        Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        Object(2, $"<< /Type /Pages /Kids [{kids}] /Count {pages.Count} >>");
        Object(3, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        Object(4, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");
        Object(5, "<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >>");
        for (int i = 0; i < pages.Count; i++)
        {
            int page = firstPage + (i * 2);
            Object(page, $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageWidth} {PageHeight}] /Resources << /Font << /F1 3 0 R /F2 4 0 R /F3 5 0 R >> >> /Contents {page + 1} 0 R >>");
            byte[] content = Encoding.Latin1.GetBytes(pages[i].ToString());
            offsets.Add(pdf.Position);
            Write($"{page + 1} 0 obj\n<< /Length {content.Length} >>\nstream\n");
            pdf.Write(content);
            Write("\nendstream\nendobj\n");
        }

        long xref = pdf.Position;
        StringBuilder table = new($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (long offset in offsets)
        {
            table.Append(CultureInfo.InvariantCulture, $"{offset:0000000000} 00000 n \n");
        }

        Write(table.ToString());
        Write($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return pdf.ToArray();
    }

    /// <summary>Splits a long string into fixed-width pieces (the activation string).</summary>
    /// <param name="text">The text.</param>
    /// <param name="width">The characters per piece.</param>
    /// <returns>The pieces.</returns>
    public static IEnumerable<string> Wrap(string text, int width)
    {
        for (int i = 0; i < (text ?? string.Empty).Length; i += width)
        {
            yield return text![i..Math.Min(text.Length, i + width)];
        }
    }

    // PDF literal strings: escape the delimiters and backslash; characters outside Latin-1 print as '?'.
    private static string Escape(string text)
    {
        StringBuilder escaped = new(text.Length);
        foreach (char c in text)
        {
            escaped.Append(c switch
            {
                '(' => "\\(",
                ')' => "\\)",
                '\\' => "\\\\",
                '—' => "-",
                '·' => "·",
                _ when c > 'ÿ' || c < ' ' => "?",
                _ => c.ToString(),
            });
        }

        return escaped.ToString();
    }
}
