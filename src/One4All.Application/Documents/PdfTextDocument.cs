using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace One4All.Application.Documents;

/// <summary>
/// Writes fixed-width text (payslips, statements) as a PDF in Courier, optionally password-protected with the PDF Standard Security
/// Handler, revision 4: AES-128 (AESV2) for every stream, the key derived from the user password as ISO 32000-1 §7.6.3 requires
/// (algorithms 1, 2, 3 and 5 — which mandate MD5 and RC4 for the key and password values). Readers ask for the user password to open
/// the file; the owner password is random, so the permissions cannot be lifted.
/// </summary>
public static class PdfTextDocument
{
    private const int PageWidth = 595;
    private const int PageHeight = 842;
    private const int FontSize = 9;
    private const int Leading = 11;
    private const int Margin = 40;
    private const int LinesPerPage = (PageHeight - (2 * Margin)) / Leading;

    // ISO 32000-1 §7.6.3.3: the 32-byte padding string.
    private static readonly byte[] Padding =
    [
        0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41, 0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08,
        0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80, 0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A,
    ];

    /// <summary>The permissions granted to the user password: print and copy, nothing that changes the document.</summary>
    public const int Permissions = -3392;

    /// <summary>Builds the PDF.</summary>
    /// <param name="text">The text (lines separated by newlines).</param>
    /// <param name="userPassword">The password needed to open it, or null for an open file.</param>
    /// <returns>The PDF bytes.</returns>
    public static byte[] Build(string text, string? userPassword = null)
    {
        List<string> lines = [.. (text ?? string.Empty).Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n')];
        List<List<string>> pages = [.. lines.Chunk(LinesPerPage).Select(c => c.ToList())];
        if (pages.Count == 0)
        {
            pages.Add([]);
        }

        byte[] fileId = RandomNumberGenerator.GetBytes(16);
        byte[]? key = null;
        byte[]? owner = null;
        byte[]? user = null;
        if (userPassword is not null)
        {
            owner = OwnerValue(Convert.ToHexString(RandomNumberGenerator.GetBytes(16)), userPassword);
            key = FileKey(userPassword, owner, Permissions, fileId);
            user = UserValue(key, fileId);
        }

        using MemoryStream pdf = new();
        List<long> offsets = [];
        void Write(string s) => pdf.Write(Encoding.Latin1.GetBytes(s));
        void Object(int number, string body)
        {
            offsets.Add(pdf.Position);
            Write(string.Create(CultureInfo.InvariantCulture, $"{number} 0 obj\n{body}\nendobj\n"));
        }

        Write("%PDF-1.6\n%âãÏÓ\n");
        int encryptObject = 4 + (pages.Count * 2);
        Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        Object(2, $"<< /Type /Pages /Kids [{string.Join(' ', Enumerable.Range(0, pages.Count).Select(i => $"{4 + (i * 2)} 0 R"))}] /Count {pages.Count} >>");
        Object(3, "<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >>");
        for (int i = 0; i < pages.Count; i++)
        {
            int pageObject = 4 + (i * 2), contentObject = pageObject + 1;
            Object(pageObject, string.Create(CultureInfo.InvariantCulture,
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageWidth} {PageHeight}] /Resources << /Font << /F1 3 0 R >> >> /Contents {contentObject} 0 R >>"));

            byte[] content = Content(pages[i]);
            if (key is not null)
            {
                content = Encrypt(key, contentObject, content);
            }

            offsets.Add(pdf.Position);
            Write(string.Create(CultureInfo.InvariantCulture, $"{contentObject} 0 obj\n<< /Length {content.Length} >>\nstream\n"));
            pdf.Write(content);
            Write("\nendstream\nendobj\n");
        }

        if (key is not null)
        {
            Object(encryptObject, $"<< /Filter /Standard /V 4 /R 4 /Length 128 /CF << /StdCF << /CFM /AESV2 /AuthEvent /DocOpen /Length 16 >> >> /StmF /StdCF /StrF /StdCF " +
                $"/O <{Convert.ToHexString(owner!)}> /U <{Convert.ToHexString(user!)}> /P {Permissions.ToString(CultureInfo.InvariantCulture)} >>");
        }

        long xref = pdf.Position;
        Write(string.Create(CultureInfo.InvariantCulture, $"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n"));
        foreach (long offset in offsets)
        {
            Write(string.Create(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n"));
        }

        string id = Convert.ToHexString(fileId);
        Write(string.Create(CultureInfo.InvariantCulture,
            $"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R /ID [<{id}> <{id}>]{(key is null ? string.Empty : $" /Encrypt {encryptObject} 0 R")} >>\nstartxref\n{xref}\n%%EOF\n"));
        return pdf.ToArray();
    }

    /// <summary>
    /// Builds a PDF whose pages are pictures (a designed payslip rendered page by page), protected like <see cref="Build"/>. Each page is
    /// as wide as given and as tall as its picture's proportions make it.
    /// </summary>
    /// <param name="jpegPages">One JPEG per page.</param>
    /// <param name="pageWidth">The page width in points (595 for A4).</param>
    /// <param name="userPassword">The password needed to open it, or null for an open file.</param>
    /// <returns>The PDF bytes.</returns>
    public static byte[] BuildFromImages(IReadOnlyList<byte[]> jpegPages, double pageWidth, string? userPassword = null)
    {
        ArgumentNullException.ThrowIfNull(jpegPages);
        if (jpegPages.Count == 0)
        {
            throw new ArgumentException("At least one page is needed.", nameof(jpegPages));
        }

        byte[] fileId = RandomNumberGenerator.GetBytes(16);
        byte[]? key = null;
        byte[]? owner = null;
        byte[]? user = null;
        if (userPassword is not null)
        {
            owner = OwnerValue(Convert.ToHexString(RandomNumberGenerator.GetBytes(16)), userPassword);
            key = FileKey(userPassword, owner, Permissions, fileId);
            user = UserValue(key, fileId);
        }

        using MemoryStream pdf = new();
        List<long> offsets = [];
        void Write(string s) => pdf.Write(Encoding.Latin1.GetBytes(s));
        void Object(int number, string body)
        {
            offsets.Add(pdf.Position);
            Write(string.Create(CultureInfo.InvariantCulture, $"{number} 0 obj\n{body}\nendobj\n"));
        }

        void Stream(int number, string dictionary, byte[] data)
        {
            if (key is not null)
            {
                data = Encrypt(key, number, data);
            }

            offsets.Add(pdf.Position);
            Write(string.Create(CultureInfo.InvariantCulture, $"{number} 0 obj\n<< {dictionary} /Length {data.Length} >>\nstream\n"));
            pdf.Write(data);
            Write("\nendstream\nendobj\n");
        }

        Write("%PDF-1.6\n%âãÏÓ\n");
        int encryptObject = 3 + (jpegPages.Count * 3);
        Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        Object(2, $"<< /Type /Pages /Kids [{string.Join(' ', Enumerable.Range(0, jpegPages.Count).Select(i => $"{3 + (i * 3)} 0 R"))}] /Count {jpegPages.Count} >>");
        for (int i = 0; i < jpegPages.Count; i++)
        {
            int pageObject = 3 + (i * 3), contentObject = pageObject + 1, imageObject = pageObject + 2;
            (int width, int height, int components) = JpegSize(jpegPages[i]);
            double pageHeight = Math.Round(pageWidth * height / Math.Max(1, width), 2);
            Object(pageObject, string.Create(CultureInfo.InvariantCulture,
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {pageWidth:0.##} {pageHeight:0.##}] /Resources << /XObject << /Im1 {imageObject} 0 R >> >> /Contents {contentObject} 0 R >>"));
            Stream(contentObject, string.Empty, Encoding.Latin1.GetBytes(string.Create(CultureInfo.InvariantCulture, $"q {pageWidth:0.##} 0 0 {pageHeight:0.##} 0 0 cm /Im1 Do Q")));
            Stream(imageObject, string.Create(CultureInfo.InvariantCulture,
                $"/Type /XObject /Subtype /Image /Width {width} /Height {height} /ColorSpace {(components == 1 ? "/DeviceGray" : components == 4 ? "/DeviceCMYK" : "/DeviceRGB")} /BitsPerComponent 8 /Filter /DCTDecode"), jpegPages[i]);
        }

        if (key is not null)
        {
            Object(encryptObject, $"<< /Filter /Standard /V 4 /R 4 /Length 128 /CF << /StdCF << /CFM /AESV2 /AuthEvent /DocOpen /Length 16 >> >> /StmF /StdCF /StrF /StdCF " +
                $"/O <{Convert.ToHexString(owner!)}> /U <{Convert.ToHexString(user!)}> /P {Permissions.ToString(CultureInfo.InvariantCulture)} >>");
        }

        long xref = pdf.Position;
        Write(string.Create(CultureInfo.InvariantCulture, $"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n"));
        foreach (long offset in offsets)
        {
            Write(string.Create(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n"));
        }

        string id = Convert.ToHexString(fileId);
        Write(string.Create(CultureInfo.InvariantCulture,
            $"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R /ID [<{id}> <{id}>]{(key is null ? string.Empty : $" /Encrypt {encryptObject} 0 R")} >>\nstartxref\n{xref}\n%%EOF\n"));
        return pdf.ToArray();
    }

    /// <summary>The pixel size and colour components of a JPEG, from its start-of-frame marker.</summary>
    /// <param name="jpeg">The JPEG bytes.</param>
    /// <returns>Width, height and the number of components (1 grey, 3 colour, 4 CMYK).</returns>
    public static (int Width, int Height, int Components) JpegSize(byte[] jpeg)
    {
        ArgumentNullException.ThrowIfNull(jpeg);
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
        {
            throw new ArgumentException("Not a JPEG.", nameof(jpeg));
        }

        int pos = 2;
        while (pos + 9 < jpeg.Length && jpeg[pos] == 0xFF)
        {
            byte marker = jpeg[pos + 1];
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                return ((jpeg[pos + 7] << 8) | jpeg[pos + 8], (jpeg[pos + 5] << 8) | jpeg[pos + 6], jpeg[pos + 9]);
            }

            if (marker is 0xD8 or (>= 0xD0 and <= 0xD7) or 0x01)
            {
                pos += 2;
                continue;
            }

            pos += 2 + ((jpeg[pos + 2] << 8) | jpeg[pos + 3]);
        }

        throw new ArgumentException("The JPEG has no start-of-frame marker.", nameof(jpeg));
    }

    // The page's text operators: Courier, one line per row, top to bottom.
    private static byte[] Content(IReadOnlyList<string> lines)
    {
        StringBuilder ops = new();
        ops.Append(CultureInfo.InvariantCulture, $"BT /F1 {FontSize} Tf {Leading} TL {Margin} {PageHeight - Margin} Td\n");
        foreach (string line in lines)
        {
            ops.Append('(');
            foreach (char ch in line)
            {
                ops.Append(ch switch
                {
                    '(' or ')' or '\\' => "\\" + ch,
                    _ when ch < ' ' || ch > 'ÿ' => "?",
                    _ => ch.ToString(),
                });
            }

            ops.Append(") Tj T*\n");
        }

        ops.Append("ET");
        return Encoding.Latin1.GetBytes(ops.ToString());
    }

    private static byte[] Pad(string password)
    {
        byte[] bytes = Encoding.Latin1.GetBytes(password);
        byte[] padded = new byte[32];
        int n = Math.Min(bytes.Length, 32);
        Array.Copy(bytes, padded, n);
        Array.Copy(Padding, 0, padded, n, 32 - n);
        return padded;
    }

#pragma warning disable CA5351 // ISO 32000-1 §7.6.3 prescribes MD5 and RC4 for the Standard Security Handler's key and password values.
    // Algorithm 3: the owner password value.
    private static byte[] OwnerValue(string ownerPassword, string userPassword)
    {
        byte[] hash = MD5.HashData(Pad(ownerPassword));
        for (int i = 0; i < 50; i++)
        {
            hash = MD5.HashData(hash);
        }

        byte[] key = hash[..16];
        byte[] value = Rc4(key, Pad(userPassword));
        for (byte i = 1; i <= 19; i++)
        {
            value = Rc4([.. key.Select(b => (byte)(b ^ i))], value);
        }

        return value;
    }

    // Algorithm 2: the file encryption key from the user password.
    private static byte[] FileKey(string userPassword, byte[] owner, int permissions, byte[] fileId)
    {
        byte[] input = [.. Pad(userPassword), .. owner, .. BitConverter.GetBytes(permissions), .. fileId];
        if (!BitConverter.IsLittleEndian)
        {
            Array.Reverse(input, 64, 4);
        }

        byte[] hash = MD5.HashData(input);
        for (int i = 0; i < 50; i++)
        {
            hash = MD5.HashData(hash[..16]);
        }

        return hash[..16];
    }

    // Algorithm 5: the user password value (revision 3 and later).
    private static byte[] UserValue(byte[] key, byte[] fileId)
    {
        byte[] value = Rc4(key, MD5.HashData([.. Padding, .. fileId]));
        for (byte i = 1; i <= 19; i++)
        {
            value = Rc4([.. key.Select(b => (byte)(b ^ i))], value);
        }

        return [.. value, .. new byte[16]];
    }

    // Algorithm 1 with AESV2: the object's key, then AES-128-CBC with a random IV in front of the data.
    private static byte[] Encrypt(byte[] fileKey, int objectNumber, byte[] data)
    {
        byte[] objectKey = MD5.HashData([.. fileKey, (byte)objectNumber, (byte)(objectNumber >> 8), (byte)(objectNumber >> 16), 0, 0, (byte)'s', (byte)'A', (byte)'l', (byte)'T'])[..16];
        using Aes aes = Aes.Create();
        aes.Key = objectKey;
        byte[] iv = RandomNumberGenerator.GetBytes(16);
        return [.. iv, .. aes.EncryptCbc(data, iv, PaddingMode.PKCS7)];
    }
#pragma warning restore CA5351

    private static byte[] Rc4(byte[] key, byte[] data)
    {
        byte[] s = [.. Enumerable.Range(0, 256).Select(i => (byte)i)];
        for (int i = 0, j = 0; i < 256; i++)
        {
            j = (j + s[i] + key[i % key.Length]) & 0xFF;
            (s[i], s[j]) = (s[j], s[i]);
        }

        byte[] output = new byte[data.Length];
        for (int n = 0, i = 0, j = 0; n < data.Length; n++)
        {
            i = (i + 1) & 0xFF;
            j = (j + s[i]) & 0xFF;
            (s[i], s[j]) = (s[j], s[i]);
            output[n] = (byte)(data[n] ^ s[(s[i] + s[j]) & 0xFF]);
        }

        return output;
    }
}
