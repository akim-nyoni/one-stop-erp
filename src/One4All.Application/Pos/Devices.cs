using System.Globalization;
using System.Text;

namespace One4All.Application.Pos;

/// <summary>What the value carried in an embedded barcode means.</summary>
public enum EmbeddedValueKind
{
    /// <summary>The weight (kilograms).</summary>
    Weight = 1,

    /// <summary>The price of the pack.</summary>
    Price = 2,
}

/// <summary>
/// A parsing template for in-store EAN-13 barcodes that carry a price or weight (brief §4.1 butchery / deli): the prefix the scale prints
/// (20–29), how many digits identify the item (its PLU), and how many carry the value with how many decimals. The last digit is the EAN check.
/// </summary>
/// <param name="Prefix">The leading digits (usually two, e.g. "21").</param>
/// <param name="ItemDigits">Digits of the item code after the prefix.</param>
/// <param name="ValueDigits">Digits of the value after the item code.</param>
/// <param name="Kind">Weight or price.</param>
/// <param name="Decimals">Implied decimals in the value (3 for grams as kg, 2 for cents).</param>
public sealed record EmbeddedBarcodeRule(string Prefix, int ItemDigits, int ValueDigits, EmbeddedValueKind Kind, int Decimals);

/// <summary>A barcode read through a template.</summary>
/// <param name="ItemCode">The item's code (its PLU) as printed, leading zeros removed.</param>
/// <param name="Kind">What the value is.</param>
/// <param name="Value">The weight or price.</param>
public sealed record EmbeddedBarcode(string ItemCode, EmbeddedValueKind Kind, decimal Value);

/// <summary>EAN-13 check digits and embedded price / weight barcodes.</summary>
public static class Barcodes
{
    /// <summary>The usual templates: 21 = weight in grams (5 digits), 22 = price in cents (5 digits), each after a 5-digit PLU.</summary>
    public static IReadOnlyList<EmbeddedBarcodeRule> DefaultRules { get; } =
    [
        new EmbeddedBarcodeRule("21", 5, 5, EmbeddedValueKind.Weight, 3),
        new EmbeddedBarcodeRule("22", 5, 5, EmbeddedValueKind.Price, 2),
    ];

    /// <summary>Whether a code is a 13-digit EAN with a correct check digit.</summary>
    /// <param name="code">The code.</param>
    /// <returns>True when valid.</returns>
    public static bool IsValidEan13(string? code)
    {
        if (code is null || code.Length != 13 || !code.All(char.IsAsciiDigit))
        {
            return false;
        }

        int sum = 0;
        for (int i = 0; i < 12; i++)
        {
            sum += (code[i] - '0') * (i % 2 == 0 ? 1 : 3);
        }

        return (10 - (sum % 10)) % 10 == code[12] - '0';
    }

    /// <summary>Reads a scanned code through the templates; null when it is not an embedded barcode (look it up as a plain code).</summary>
    /// <param name="code">The scanned code.</param>
    /// <param name="rules">The templates (null: <see cref="DefaultRules"/>).</param>
    /// <returns>The item and value, or null.</returns>
    public static EmbeddedBarcode? TryParseEmbedded(string? code, IEnumerable<EmbeddedBarcodeRule>? rules = null)
    {
        string text = (code ?? string.Empty).Trim();
        if (!IsValidEan13(text))
        {
            return null;
        }

        foreach (EmbeddedBarcodeRule rule in rules ?? DefaultRules)
        {
            if (!text.StartsWith(rule.Prefix, StringComparison.Ordinal) || rule.Prefix.Length + rule.ItemDigits + rule.ValueDigits > 12)
            {
                continue;
            }

            string item = text.Substring(rule.Prefix.Length, rule.ItemDigits).TrimStart('0');
            decimal raw = decimal.Parse(text.AsSpan(12 - rule.ValueDigits, rule.ValueDigits), NumberStyles.None, CultureInfo.InvariantCulture);
            decimal value = raw / (decimal)Math.Pow(10, rule.Decimals);
            return new EmbeddedBarcode(item.Length == 0 ? "0" : item, rule.Kind, value);
        }

        return null;
    }
}

/// <summary>
/// Builds ESC/POS byte streams for receipt printers and the cash drawer wired to them: initialise, text in the printer's code page,
/// bold, alignment, double size, feed, cut, and the drawer-kick pulse.
/// </summary>
public sealed class EscPosBuilder
{
    private const byte Esc = 0x1B;
    private const byte Gs = 0x1D;
    private readonly List<byte> _bytes = [Esc, (byte)'@'];
    private readonly Encoding _encoding = Encoding.Latin1;

    /// <summary>Gets the bytes so far.</summary>
    /// <returns>The stream.</returns>
    public byte[] ToArray() => [.. _bytes];

    /// <summary>Adds text and a line feed.</summary>
    /// <param name="text">The text (characters outside Latin-1 print as '?').</param>
    /// <returns>The builder.</returns>
    public EscPosBuilder Line(string text = "")
    {
        _bytes.AddRange(_encoding.GetBytes(text.Replace("\r", string.Empty, StringComparison.Ordinal)));
        _bytes.Add(0x0A);
        return this;
    }

    /// <summary>Adds every line of a block of text.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The builder.</returns>
    public EscPosBuilder Text(string text)
    {
        foreach (string line in (text ?? string.Empty).Replace("\r", string.Empty, StringComparison.Ordinal).TrimEnd('\n').Split('\n'))
        {
            Line(line);
        }

        return this;
    }

    /// <summary>Turns bold on or off.</summary>
    /// <param name="on">On.</param>
    /// <returns>The builder.</returns>
    public EscPosBuilder Bold(bool on) => Add(Esc, (byte)'E', on ? (byte)1 : (byte)0);

    /// <summary>Sets the alignment: 0 left, 1 centre, 2 right.</summary>
    /// <param name="alignment">The alignment.</param>
    /// <returns>The builder.</returns>
    public EscPosBuilder Align(int alignment) => Add(Esc, (byte)'a', (byte)Math.Clamp(alignment, 0, 2));

    /// <summary>Turns double width and height on or off.</summary>
    /// <param name="on">On.</param>
    /// <returns>The builder.</returns>
    public EscPosBuilder DoubleSize(bool on) => Add(Gs, (byte)'!', on ? (byte)0x11 : (byte)0x00);

    /// <summary>Feeds lines.</summary>
    /// <param name="lines">How many.</param>
    /// <returns>The builder.</returns>
    public EscPosBuilder Feed(int lines) => Add(Esc, (byte)'d', (byte)Math.Clamp(lines, 0, 255));

    /// <summary>Feeds and makes a partial cut.</summary>
    /// <returns>The builder.</returns>
    public EscPosBuilder Cut() => Add(Gs, (byte)'V', 66, 3);

    /// <summary>Pulses the cash drawer on pin 2.</summary>
    /// <returns>The builder.</returns>
    public EscPosBuilder KickDrawer() => Add(Esc, (byte)'p', 0, 25, 250);

    /// <summary>A complete receipt: the text, fed and cut, the drawer opened when asked.</summary>
    /// <param name="receiptText">The receipt as text.</param>
    /// <param name="openDrawer">Whether to open the drawer (cash taken).</param>
    /// <returns>The bytes.</returns>
    public static byte[] Receipt(string receiptText, bool openDrawer)
    {
        EscPosBuilder builder = new EscPosBuilder().Text(receiptText).Feed(3).Cut();
        return (openDrawer ? builder.KickDrawer() : builder).ToArray();
    }

    private EscPosBuilder Add(params byte[] bytes)
    {
        _bytes.AddRange(bytes);
        return this;
    }
}

/// <summary>A weight read from a scale.</summary>
/// <param name="Kilograms">The weight.</param>
/// <param name="Stable">Whether the scale reported it settled (only a stable weight may be sold).</param>
public sealed record ScaleReading(decimal Kilograms, bool Stable);

/// <summary>Reads the common scale reply format: an optional status, a number and a unit (kg, g, lb), e.g. "ST,GS,  1.234kg" or "US 0.500 kg".</summary>
public static class ScaleResponse
{
    /// <summary>Parses a reply; "US" (unstable) or "M" (motion) marks it unstable; grams and pounds become kilograms.</summary>
    /// <param name="reply">The reply.</param>
    /// <returns>The reading, or null when no weight was found.</returns>
    public static ScaleReading? TryParse(string? reply)
    {
        string text = (reply ?? string.Empty).Trim();
        int start = text.IndexOfAny(['-', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9']);
        if (start < 0)
        {
            return null;
        }

        int end = start + 1;
        while (end < text.Length && (char.IsAsciiDigit(text[end]) || text[end] == '.'))
        {
            end++;
        }

        if (!decimal.TryParse(text.AsSpan(start, end - start), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out decimal value))
        {
            return null;
        }

        string unit = text[end..].Trim().ToLowerInvariant();
        decimal kilograms = unit.StartsWith("lb", StringComparison.Ordinal) ? Math.Round(value * 0.45359237m, 3)
            : unit.StartsWith('g') ? value / 1000m : value;
        string status = text[..start].ToUpperInvariant();
        bool stable = !status.Contains("US", StringComparison.Ordinal) && !status.Contains('M');
        return new ScaleReading(kilograms, stable);
    }
}
