using System.Text.RegularExpressions;

namespace One4All.Core.Accounting;

/// <summary>
/// A validated ISO 4217 three-letter currency code (e.g. <c>USD</c>, <c>ZWL</c>).
/// A lightweight value object used wherever a currency must be named; the full currency
/// master (symbol, decimals, denominations, rounding) is a <c>sys</c>-schema entity that
/// references this code.
/// </summary>
public readonly partial struct CurrencyCode : IEquatable<CurrencyCode>
{
    [GeneratedRegex("^[A-Z]{3}$", RegexOptions.CultureInvariant)]
    private static partial Regex IsoPattern();

    private CurrencyCode(string value) => Value = value;

    /// <summary>The three-letter uppercase code.</summary>
    public string Value { get; }

    /// <summary>True when this code has not been initialized (default struct value).</summary>
    public bool IsEmpty => string.IsNullOrEmpty(Value);

    /// <summary>
    /// Parses and validates a currency code. Trims surrounding whitespace and upper-cases before validating.
    /// </summary>
    /// <param name="value">A candidate three-letter code.</param>
    /// <returns>The parsed <see cref="CurrencyCode"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is not three ASCII letters.</exception>
    public static CurrencyCode Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Currency code is required.", nameof(value));
        }

        string normalized = value.Trim().ToUpperInvariant();
        if (!IsoPattern().IsMatch(normalized))
        {
            throw new ArgumentException($"'{value}' is not a valid ISO 4217 currency code (expected three letters).", nameof(value));
        }

        return new CurrencyCode(normalized);
    }

    /// <summary>Attempts to parse a currency code without throwing.</summary>
    /// <param name="value">A candidate three-letter code.</param>
    /// <param name="code">The parsed code on success.</param>
    /// <returns>True when parsing succeeded.</returns>
    public static bool TryParse(string? value, out CurrencyCode code)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            string normalized = value.Trim().ToUpperInvariant();
            if (IsoPattern().IsMatch(normalized))
            {
                code = new CurrencyCode(normalized);
                return true;
            }
        }

        code = default;
        return false;
    }

    /// <inheritdoc />
    public bool Equals(CurrencyCode other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is CurrencyCode other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Value is null ? 0 : StringComparer.Ordinal.GetHashCode(Value);

    /// <summary>Equality operator.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when the codes are equal.</returns>
    public static bool operator ==(CurrencyCode left, CurrencyCode right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when the codes differ.</returns>
    public static bool operator !=(CurrencyCode left, CurrencyCode right) => !left.Equals(right);

    /// <summary>Returns the three-letter code.</summary>
    /// <returns>The code, or an empty string if uninitialized.</returns>
    public override string ToString() => Value ?? string.Empty;
}
