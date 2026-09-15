using System.Globalization;
using System.Text;

namespace One4All.Reporting.Expressions;

/// <summary>
/// Converts numbers to English words (brief §13.1) — essential for cheques and payment vouchers. Handles
/// the integer part and, for currency, the minor units (e.g. "One Hundred And Fifteen Dollars and Zero
/// Cents"). Multi-language variants can be added as further methods.
/// </summary>
public static class NumberToWords
{
    private static readonly string[] Ones =
    [
        "Zero", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten",
        "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen", "Seventeen", "Eighteen", "Nineteen",
    ];

    private static readonly string[] Tens =
    [
        "", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety",
    ];

    private static readonly (long Value, string Name)[] Scales =
    [
        (1_000_000_000_000, "Trillion"),
        (1_000_000_000, "Billion"),
        (1_000_000, "Million"),
        (1_000, "Thousand"),
        (100, "Hundred"),
    ];

    /// <summary>Converts a whole number to words.</summary>
    /// <param name="value">The integer value.</param>
    /// <returns>The number in English words.</returns>
    public static string Convert(long value)
    {
        if (value == 0)
        {
            return Ones[0];
        }

        if (value < 0)
        {
            return "Minus " + Convert(-value);
        }

        StringBuilder sb = new();
        long remaining = value;

        foreach ((long scaleValue, string scaleName) in Scales)
        {
            if (remaining >= scaleValue)
            {
                long count = remaining / scaleValue;
                remaining %= scaleValue;
                sb.Append(Convert(count)).Append(' ').Append(scaleName);
                if (remaining > 0)
                {
                    sb.Append(' ');
                }
            }
        }

        if (remaining > 0)
        {
            if (sb.Length > 0)
            {
                sb.Append("And ");
            }

            if (remaining < 20)
            {
                sb.Append(Ones[remaining]);
            }
            else
            {
                sb.Append(Tens[remaining / 10]);
                if (remaining % 10 > 0)
                {
                    sb.Append('-').Append(Ones[remaining % 10]);
                }
            }
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// Converts a currency amount to words, e.g. <c>Amount(115.05m, "Dollars", "Cents")</c> →
    /// "One Hundred And Fifteen Dollars and Five Cents".
    /// </summary>
    /// <param name="amount">The amount.</param>
    /// <param name="majorUnit">The major unit name (e.g. "Dollars").</param>
    /// <param name="minorUnit">The minor unit name (e.g. "Cents").</param>
    /// <returns>The amount in words.</returns>
    public static string Amount(decimal amount, string majorUnit = "Dollars", string minorUnit = "Cents")
    {
        bool negative = amount < 0m;
        decimal abs = Math.Abs(amount);
        long whole = (long)decimal.Truncate(abs);
        int minor = (int)decimal.Round((abs - whole) * 100m, 0, MidpointRounding.ToEven);

        string result = $"{Convert(whole)} {majorUnit} and {Convert(minor)} {minorUnit}";
        return negative ? "Minus " + result : result;
    }

    /// <summary>Formats a value using an optional .NET format string and invariant culture.</summary>
    /// <param name="value">The value.</param>
    /// <param name="format">The format string, or null.</param>
    /// <returns>The formatted string.</returns>
    public static string Format(object? value, string? format)
    {
        if (value is null)
        {
            return string.Empty;
        }

        if (string.IsNullOrEmpty(format))
        {
            return value.ToString() ?? string.Empty;
        }

        return value is IFormattable formattable
            ? formattable.ToString(format, CultureInfo.InvariantCulture)
            : value.ToString() ?? string.Empty;
    }
}
