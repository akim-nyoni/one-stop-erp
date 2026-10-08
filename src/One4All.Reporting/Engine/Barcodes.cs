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
    public static readonly IReadOnlyList<string> Symbologies = ["QR", "Code128", "Code39", "EAN13", "PDF417"];

    // Code 39: per character, nine elements (bar, space, bar …) where 1 is wide.
    private static readonly Dictionary<char, string> Code39 = new()
    {
        ['0'] = "000110100", ['1'] = "100100001", ['2'] = "001100001", ['3'] = "101100000", ['4'] = "000110001", ['5'] = "100110000", ['6'] = "001110000",
        ['7'] = "000100101", ['8'] = "100100100", ['9'] = "001100100", ['A'] = "100001001", ['B'] = "001001001", ['C'] = "101001000", ['D'] = "000011001",
        ['E'] = "100011000", ['F'] = "001011000", ['G'] = "000001101", ['H'] = "100001100", ['I'] = "001001100", ['J'] = "000011100", ['K'] = "100000011",
        ['L'] = "001000011", ['M'] = "101000010", ['N'] = "000010011", ['O'] = "100010010", ['P'] = "001010010", ['Q'] = "000000111", ['R'] = "100000110",
        ['S'] = "001000110", ['T'] = "000010110", ['U'] = "110000001", ['V'] = "011000001", ['W'] = "111000000", ['X'] = "010010001", ['Y'] = "110010000",
        ['Z'] = "011010000", ['-'] = "010000101", ['.'] = "110000100", [' '] = "011000100", ['$'] = "010101000", ['/'] = "010100010", ['+'] = "010001010",
        ['%'] = "000101010", ['*'] = "010010100",
    };

    // EAN-13 digit codes (left odd "L", left even "G", right "R") and the left-half parity each first digit implies.
    private static readonly string[] EanL = ["0001101", "0011001", "0010011", "0111101", "0100011", "0110001", "0101111", "0111011", "0110111", "0001011"];
    private static readonly string[] EanG = ["0100111", "0110011", "0011011", "0100001", "0011101", "0111001", "0000101", "0010001", "0001001", "0010111"];
    private static readonly string[] EanR = ["1110010", "1100110", "1101100", "1000010", "1011100", "1001110", "1010000", "1000100", "1001000", "1110100"];
    private static readonly string[] EanParity = ["LLLLLL", "LLGLGG", "LLGGLG", "LLGGGL", "LGLLGG", "LGGLLG", "LGGGLL", "LGLGLG", "LGLGGL", "LGGLGL"];

    /// <summary>The Code 39 modules (true = dark) for a value: start and stop asterisks, wide elements three modules, a narrow gap between characters.</summary>
    /// <param name="value">The value (upper-cased; digits, A–Z, space and - . $ / + %).</param>
    /// <returns>The modules, or null when a character cannot be encoded.</returns>
    public static IReadOnlyList<bool>? Code39Modules(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string text = "*" + value.ToUpperInvariant() + "*";
        if (value.Contains('*', StringComparison.Ordinal) || text.Any(c => !Code39.ContainsKey(c)))
        {
            return null;
        }

        List<bool> modules = [];
        foreach (char c in text)
        {
            if (modules.Count > 0)
            {
                modules.Add(false); // inter-character gap
            }

            string pattern = Code39[c];
            for (int i = 0; i < pattern.Length; i++)
            {
                modules.AddRange(Enumerable.Repeat(i % 2 == 0, pattern[i] == '1' ? 3 : 1));
            }
        }

        return modules;
    }

    /// <summary>The PDF417 modules (true = dark) for a value, error correction level 2, quiet zone included (ZXing's encoder).</summary>
    /// <param name="value">The value (any text; bytes are used where it is not plain text).</param>
    /// <returns>The rows of modules.</returns>
    public static bool[][] Pdf417Modules(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ZXing.PDF417.PDF417Writer writer = new();
        Dictionary<ZXing.EncodeHintType, object> hints = new()
        {
            [ZXing.EncodeHintType.ERROR_CORRECTION] = ZXing.PDF417.Internal.PDF417ErrorCorrectionLevel.L2,
            [ZXing.EncodeHintType.MARGIN] = 2,
            [ZXing.EncodeHintType.CHARACTER_SET] = "UTF-8",
        };
        ZXing.Common.BitMatrix matrix = writer.encode(value, ZXing.BarcodeFormat.PDF_417, 0, 0, hints);
        return [.. Enumerable.Range(0, matrix.Height).Select(y => Enumerable.Range(0, matrix.Width).Select(x => matrix[x, y]).ToArray())];
    }

    // A PDF417 symbol, two pixels a module across and three down (the symbol's rows are taller than wide).
    private static byte[] Pdf417Png(string value)
    {
        bool[][] rows = Pdf417Modules(value);
        const int across = 2, down = 3;
        int width = rows[0].Length * across;
        return GreyscalePng(width, rows.Length * down, y => [.. rows[y / down].SelectMany(dark => Enumerable.Repeat(dark ? (byte)0 : (byte)255, across))]);
    }

    /// <summary>The EAN-13 check digit for twelve digits.</summary>
    /// <param name="twelve">The first twelve digits.</param>
    /// <returns>The check digit.</returns>
    public static int Ean13CheckDigit(string twelve)
    {
        ArgumentNullException.ThrowIfNull(twelve);
        int sum = 0;
        for (int i = 0; i < 12; i++)
        {
            sum += (twelve[i] - '0') * (i % 2 == 0 ? 1 : 3);
        }

        return (10 - (sum % 10)) % 10;
    }

    /// <summary>The 95 EAN-13 modules ("1" dark) for twelve digits (the check digit is added) or thirteen (the check digit must be right).</summary>
    /// <param name="value">The digits.</param>
    /// <returns>The modules, or null when the value is not a valid EAN-13.</returns>
    public static string? Ean13Modules(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string digits = value.Trim();
        if (digits.Length is not (12 or 13) || !digits.All(char.IsAsciiDigit))
        {
            return null;
        }

        int check = Ean13CheckDigit(digits[..12]);
        if (digits.Length == 13 && digits[12] - '0' != check)
        {
            return null;
        }

        digits = digits[..12] + (char)('0' + check);
        string parity = EanParity[digits[0] - '0'];
        System.Text.StringBuilder modules = new("101");
        for (int i = 1; i <= 6; i++)
        {
            modules.Append(parity[i - 1] == 'L' ? EanL[digits[i] - '0'] : EanG[digits[i] - '0']);
        }

        modules.Append("01010");
        for (int i = 7; i <= 12; i++)
        {
            modules.Append(EanR[digits[i] - '0']);
        }

        return modules.Append("101").ToString();
    }

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

        string kind = (symbology ?? "Code128").Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        if (kind is "PDF417" or "PDF")
        {
            return Pdf417Png(value);
        }
        List<bool> bars;
        if (kind == "CODE39")
        {
            if (Code39Modules(value) is not IReadOnlyList<bool> code39)
            {
                return null;
            }

            bars = [.. code39];
        }
        else if (kind is "EAN13" or "EAN")
        {
            if (Ean13Modules(value) is not string ean)
            {
                return null;
            }

            bars = [.. ean.Select(m => m == '1')];
        }
        else
        {
            if (Code128Pattern(value) is not string pattern)
            {
                return null;
            }

            bars = [];
            for (int i = 0; i < pattern.Length; i++)
            {
                bars.AddRange(Enumerable.Repeat(i % 2 == 0, pattern[i] - '0'));
            }
        }

        // Bars two pixels a module, with a quiet zone of ten modules each side.
        const int moduleWidth = 2, quiet = 10, height = 50;
        List<bool> modules = [.. Enumerable.Repeat(false, quiet), .. bars, .. Enumerable.Repeat(false, quiet)];
        byte[] row = [.. modules.SelectMany(dark => Enumerable.Repeat(dark ? (byte)0 : (byte)255, moduleWidth))];
        return GreyscalePng(row.Length, height, _ => row);
    }

    /// <summary>A data URI for a PNG, as the renderers and the viewer take images.</summary>
    /// <param name="png">The PNG.</param>
    /// <returns>The data URI.</returns>
    public static string DataUri(byte[] png) => "data:image/png;base64," + Convert.ToBase64String(png);

    /// <summary>A minimal 8-bit RGB PNG from a row-major RGB buffer.</summary>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="rgb">Three bytes a pixel, row by row.</param>
    /// <returns>The PNG.</returns>
    public static byte[] RgbPng(int width, int height, byte[] rgb)
    {
        ArgumentNullException.ThrowIfNull(rgb);
        return Png(width, height, 2, y => rgb.AsSpan(y * width * 3, width * 3).ToArray());
    }

    // A minimal 8-bit greyscale PNG: signature, IHDR, one zlib IDAT, IEND.
    private static byte[] GreyscalePng(int width, int height, Func<int, byte[]> rowAt) => Png(width, height, 0, rowAt);

    private static byte[] Png(int width, int height, byte colourType, Func<int, byte[]> rowAt)
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
        header[9] = colourType; // 0 greyscale, 2 RGB
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
