using System.Globalization;

namespace One4All.Core.Accounting;

/// <summary>
/// An immutable monetary amount in a specific currency. All money in the system is
/// <see cref="decimal"/> (never <see cref="float"/>/<see cref="double"/>) and is persisted
/// as <c>numeric(19,4)</c> per the brief. Arithmetic across different currencies is a
/// programming error and throws; conversion must go through an explicit exchange-rate step.
/// </summary>
public readonly struct Money : IEquatable<Money>, IComparable<Money>
{
    /// <summary>The scale (decimal places) at which amounts are stored and compared: <c>numeric(19,4)</c>.</summary>
    public const int StorageScale = 4;

    /// <summary>
    /// Creates a money value, rounding the amount to <see cref="StorageScale"/> using banker's rounding
    /// so that a value constructed from a computation matches what will be stored.
    /// </summary>
    /// <param name="amount">The decimal amount.</param>
    /// <param name="currency">The currency of the amount.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="currency"/> is uninitialized.</exception>
    public Money(decimal amount, CurrencyCode currency)
    {
        if (currency.IsEmpty)
        {
            throw new ArgumentException("Money requires a currency.", nameof(currency));
        }

        Currency = currency;
        Amount = decimal.Round(amount, StorageScale, MidpointRounding.ToEven);
    }

    /// <summary>The amount, rounded to <see cref="StorageScale"/> decimal places.</summary>
    public decimal Amount { get; }

    /// <summary>The currency of the amount.</summary>
    public CurrencyCode Currency { get; }

    /// <summary>True when the amount is exactly zero.</summary>
    public bool IsZero => Amount == 0m;

    /// <summary>True when the amount is greater than zero.</summary>
    public bool IsPositive => Amount > 0m;

    /// <summary>True when the amount is less than zero.</summary>
    public bool IsNegative => Amount < 0m;

    /// <summary>Creates a zero amount in the given currency.</summary>
    /// <param name="currency">The currency.</param>
    /// <returns>Zero money in <paramref name="currency"/>.</returns>
    public static Money Zero(CurrencyCode currency) => new(0m, currency);

    /// <summary>Adds two amounts of the same currency.</summary>
    /// <param name="other">The amount to add.</param>
    /// <returns>The sum.</returns>
    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount + other.Amount, Currency);
    }

    /// <summary>Subtracts an amount of the same currency.</summary>
    /// <param name="other">The amount to subtract.</param>
    /// <returns>The difference.</returns>
    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount - other.Amount, Currency);
    }

    /// <summary>Multiplies the amount by a scalar (e.g. a quantity or a percentage factor).</summary>
    /// <param name="factor">The multiplier.</param>
    /// <returns>The product, rounded to storage scale.</returns>
    public Money Multiply(decimal factor) => new(Amount * factor, Currency);

    /// <summary>Returns the amount with its sign reversed.</summary>
    /// <returns>The negated amount.</returns>
    public Money Negate() => new(-Amount, Currency);

    /// <summary>Returns the absolute value of the amount.</summary>
    /// <returns>The absolute amount.</returns>
    public Money Abs() => new(Math.Abs(Amount), Currency);

    /// <summary>
    /// Allocates the amount into <paramref name="parts"/> as evenly as possible without losing
    /// or gaining a cent: the remainder from rounding is distributed one minor unit at a time to
    /// the earliest parts. Essential for splitting bills, apportioning landed cost and spreading budgets.
    /// </summary>
    /// <param name="parts">The number of parts to split into.</param>
    /// <returns>An array of <paramref name="parts"/> amounts that sum exactly to this amount.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="parts"/> is not positive.</exception>
    public Money[] Allocate(int parts)
    {
        if (parts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(parts), parts, "Allocation requires at least one part.");
        }

        decimal minorUnit = 1m / Pow10(StorageScale);
        decimal low = decimal.Round(Amount / parts, StorageScale, MidpointRounding.ToZero);
        Money[] result = new Money[parts];

        decimal allocated = 0m;
        for (int i = 0; i < parts; i++)
        {
            result[i] = new Money(low, Currency);
            allocated += low;
        }

        // Distribute the rounding remainder one minor unit at a time.
        decimal remainder = decimal.Round(Amount - allocated, StorageScale, MidpointRounding.ToEven);
        int units = (int)decimal.Round(remainder / minorUnit, 0, MidpointRounding.ToEven);
        for (int i = 0; i < Math.Abs(units); i++)
        {
            int index = i % parts;
            decimal adjust = units > 0 ? minorUnit : -minorUnit;
            result[index] = new Money(result[index].Amount + adjust, Currency);
        }

        return result;
    }

    /// <summary>Compares this amount to another of the same currency.</summary>
    /// <param name="other">The amount to compare to.</param>
    /// <returns>A signed comparison of the two amounts.</returns>
    public int CompareTo(Money other)
    {
        EnsureSameCurrency(other);
        return Amount.CompareTo(other.Amount);
    }

    /// <inheritdoc />
    public bool Equals(Money other) => Currency == other.Currency && Amount == other.Amount;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Money other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Amount, Currency);

    /// <summary>Addition operator for same-currency amounts.</summary>
    /// <param name="a">Left operand.</param>
    /// <param name="b">Right operand.</param>
    /// <returns>The sum.</returns>
    public static Money operator +(Money a, Money b) => a.Add(b);

    /// <summary>Subtraction operator for same-currency amounts.</summary>
    /// <param name="a">Left operand.</param>
    /// <param name="b">Right operand.</param>
    /// <returns>The difference.</returns>
    public static Money operator -(Money a, Money b) => a.Subtract(b);

    /// <summary>Unary negation operator.</summary>
    /// <param name="a">The operand.</param>
    /// <returns>The negated amount.</returns>
    public static Money operator -(Money a) => a.Negate();

    /// <summary>Scalar multiplication operator.</summary>
    /// <param name="a">The amount.</param>
    /// <param name="factor">The multiplier.</param>
    /// <returns>The product.</returns>
    public static Money operator *(Money a, decimal factor) => a.Multiply(factor);

    /// <summary>Equality operator.</summary>
    /// <param name="a">Left operand.</param>
    /// <param name="b">Right operand.</param>
    /// <returns>True when currency and amount are equal.</returns>
    public static bool operator ==(Money a, Money b) => a.Equals(b);

    /// <summary>Inequality operator.</summary>
    /// <param name="a">Left operand.</param>
    /// <param name="b">Right operand.</param>
    /// <returns>True when currency or amount differ.</returns>
    public static bool operator !=(Money a, Money b) => !a.Equals(b);

    /// <summary>Greater-than operator for same-currency amounts.</summary>
    /// <param name="a">Left operand.</param>
    /// <param name="b">Right operand.</param>
    /// <returns>True when <paramref name="a"/> exceeds <paramref name="b"/>.</returns>
    public static bool operator >(Money a, Money b) => a.CompareTo(b) > 0;

    /// <summary>Less-than operator for same-currency amounts.</summary>
    /// <param name="a">Left operand.</param>
    /// <param name="b">Right operand.</param>
    /// <returns>True when <paramref name="a"/> is below <paramref name="b"/>.</returns>
    public static bool operator <(Money a, Money b) => a.CompareTo(b) < 0;

    /// <summary>Greater-than-or-equal operator for same-currency amounts.</summary>
    /// <param name="a">Left operand.</param>
    /// <param name="b">Right operand.</param>
    /// <returns>True when <paramref name="a"/> is at least <paramref name="b"/>.</returns>
    public static bool operator >=(Money a, Money b) => a.CompareTo(b) >= 0;

    /// <summary>Less-than-or-equal operator for same-currency amounts.</summary>
    /// <param name="a">Left operand.</param>
    /// <param name="b">Right operand.</param>
    /// <returns>True when <paramref name="a"/> is at most <paramref name="b"/>.</returns>
    public static bool operator <=(Money a, Money b) => a.CompareTo(b) <= 0;

    /// <summary>Formats the amount with its currency code, using invariant formatting.</summary>
    /// <returns>A string of the form <c>1234.5600 USD</c>.</returns>
    public override string ToString() =>
        $"{Amount.ToString("F" + StorageScale, CultureInfo.InvariantCulture)} {Currency}";

    private void EnsureSameCurrency(Money other)
    {
        if (Currency != other.Currency)
        {
            throw new InvalidOperationException(
                $"Cannot operate on amounts of different currencies ({Currency} vs {other.Currency}). Convert via an exchange rate first.");
        }
    }

    private static decimal Pow10(int exponent)
    {
        decimal value = 1m;
        for (int i = 0; i < exponent; i++)
        {
            value *= 10m;
        }

        return value;
    }
}
