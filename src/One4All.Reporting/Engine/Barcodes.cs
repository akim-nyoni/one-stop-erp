using System.IO.Compression;
using QRCoder;

namespace One4All.Reporting.Engine;

/// <summary>
/// Barcodes for report elements (brief §13.1), drawn as PNG images: QR codes (fiscal verification links, payment references) and Code 128
/// (item codes, document numbers). Pure — no fonts or system drawing libraries.
/// </summary>
public static class Barcodes
{
    // Code 128 bar / space widths for values 0..105, and the stop pattern.
    private static readonly string[] Patterns =
    [
        "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312", "132212", "221213", "221312", "231212", "112232", "122132", "122231", "113222",
        "123122", "123221", "223211", "221132", "221231", "213212", "223112", "312131", "311222", "321122", "321221", "312212", "322112", "322211", "212123", "212321",
        "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313", "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121",
        "313121", "211331", "231131", "213113", "213311", "213131", "311123", "311321", "331121", "312113", "312311", "332111", "314111", "221411", "431111", "111224",
        "111422", "121124", "121421", "141122", "141221", "112214", "112412", "122114", "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111",
        "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112", "421211", "212141", "214121", "412121", "111143", "111341", "131141", "114113",
        "114311", "411113", "411311", "113141", "114131", "311141", "411131", "211412", "211214", "211232",
    ];

    private const string Stop = "2331112";
    private const int StartB = 104;

    /// <summary>The symbologies supported.</summary>
    public static readonly IReadOnlyList<string> Symbologies = ["QR", "Code128"];

    /// <summary>The Code 128 (set B) width pattern of a value: start, data, checksum and stop, as bar / space widths.</summary>
    /// <param name="value">Printable ASCII text.</param>
    /// <returns>The widths, or null when the text has characters set B cannot carry.</returns>
    public static string? Code128Pattern(string value)
    {
        if (value.Any(c => c < 32 || c > 126))
        {
            return null;
        }

        int checksum = StartB;
        System.Text.StringBuilder widths = new(Patterns[StartB]);
        for (int i = 0; i < value.Length; i++)
        {
            int code = value[i] - 32;
            checksum += code * (i + 1);
            widths.Append(Patterns[code]);
        }

        widths.Append(Patterns[checksum % 103]).Append(Stop);
        return widths.ToString();
    }

    /// <summary>Draws a barcode as a PNG.</summary>
    /// <param name="symbology">QR or Code128 (anything else is Code128).</param>
    /// <param name="value">The data.</param>
    /// <returns>The PNG, or null when there is nothing (or nothing encodable) to draw.</returns>
    public static byte[]? Png(string? symbology, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (string.Equals(symbology?.Replace(" ", string.Empty, StringComparison.Ordinal), "QR", StringComparison.OrdinalIgnoreCase)
            || string.Equals(symbology, "QRCode", StringComparison.OrdinalIgnoreCase))
        {
            using QRCodeGenerator generator = new();
            using QRCodeData data = generator.CreateQrCode(value, QRCodeGenerator.ECCLevel.M);
            return new PngByteQRCode(data).GetGraphic(4);
        }

        if (Code128Pattern(value) is not string pattern)
        {
            return null;
        }

        // Bars two pixels a module, with a quiet zone of ten modules each side.
        const int moduleWidth = 2, quiet = 10, height = 50;
        List<bool> modules = [.. Enumerable.Repeat(false, quiet)];
        for (int i = 0; i < pattern.Length; i++)
        {
            modules.AddRange(Enumerable.Repeat(i % 2 == 0, pattern[i] - '0'));
        }

        modules.AddRange(Enumerable.Repeat(false, quiet));
        byte[] row = [.. modules.SelectMany(dark => Enumerable.Repeat(dark ? (byte)0 : (byte)255, moduleWidth))];
        return GreyscalePng(row.Length, height, _ => row);
    }

    /// <summary>A data URI for a PNG, as the renderers and the viewer take images.</summary>
    /// <param name="png">The PNG.</param>
    /// <returns>The data URI.</returns>
    public static string DataUri(byte[] png) => "data:image/png;base64," + Convert.ToBase64String(png);

    // A minimal 8-bit greyscale PNG: signature, IHDR, one zlib IDAT, IEND.
    private static byte[] GreyscalePng(int width, int height, Func<int, byte[]> rowAt)
    {
        using MemoryStream raw = new();
        for (int y = 0; y < height; y++)
        {
            raw.WriteByte(0); // no filter
            raw.Write(rowAt(y));
        }

        using MemoryStream compressed = new();
        using (ZLibStream zlib = new(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            raw.Position = 0;
            raw.CopyTo(zlib);
        }

        using MemoryStream png = new();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        byte[] header = new byte[13];
        WriteBigEndian(header, 0, width);
        WriteBigEndian(header, 4, height);
        header[8] = 8; // bit depth
        header[9] = 0; // greyscale
        Chunk(png, "IHDR", header);
        Chunk(png, "IDAT", compressed.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void Chunk(Stream output, string type, byte[] data)
    {
        byte[] length = new byte[4];
        WriteBigEndian(length, 0, data.Length);
        output.Write(length);
        byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);
        byte[] crc = new byte[4];
        WriteBigEndian(crc, 0, unchecked((int)Crc32([.. typeBytes, .. data])));
        output.Write(crc);
    }

    private static void WriteBigEndian(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }
}
