namespace One4All.Application.Pos;

/// <summary>
/// The cash reconciliation for a till session at close (cash-up). The drawer should hold the opening float
/// plus the net cash taken (cash tendered less change given); the variance is what was counted minus that
/// expectation — positive is an overage, negative a shortage. Kept as a pure value so the arithmetic is
/// unit-tested; the POS service supplies the figures from the session's sales and posts the variance.
/// </summary>
/// <param name="OpeningFloat">The float the drawer opened with.</param>
/// <param name="CashReceived">Cash tendered on sales (non-change).</param>
/// <param name="ChangeGiven">Cash paid out as change.</param>
/// <param name="CountedCash">The cash physically counted at close.</param>
public readonly record struct CashUpCalculation(decimal OpeningFloat, decimal CashReceived, decimal ChangeGiven, decimal CountedCash, decimal CashMovements = 0m)
{
    /// <summary>The cash the drawer is expected to hold: float + cash received − change given.</summary>
    public decimal ExpectedCash => OpeningFloat + CashReceived - ChangeGiven + CashMovements;

    /// <summary>Counted minus expected: positive is an overage, negative a shortage, zero balances.</summary>
    public decimal Variance => CountedCash - ExpectedCash;

    /// <summary>True when more cash was counted than expected.</summary>
    public bool IsOver => Variance > 0m;

    /// <summary>True when less cash was counted than expected.</summary>
    public bool IsShort => Variance < 0m;

    /// <summary>True when the drawer reconciles exactly.</summary>
    public bool Balances => Variance == 0m;
}

/// <summary>Cash counted in one currency.</summary>
/// <param name="Currency">The currency (ISO code).</param>
/// <param name="Amount">The amount counted.</param>
public readonly record struct CashCount(string Currency, decimal Amount);

/// <summary>One currency's cash-up: what the drawer should hold in it, what was counted, and the variance in base at the session's rate.</summary>
/// <param name="Currency">The currency.</param>
/// <param name="Expected">Expected in the currency.</param>
/// <param name="Counted">Counted in the currency.</param>
/// <param name="Rate">The rate to base (the session's average for its tenders; 1 for base).</param>
public sealed record CurrencyCashUp(string Currency, decimal Expected, decimal Counted, decimal Rate)
{
    /// <summary>Counted − expected, in the currency.</summary>
    public decimal Variance => Counted - Expected;

    /// <summary>The variance in base currency.</summary>
    public decimal VarianceBase => decimal.Round(Variance * Rate, 2, MidpointRounding.AwayFromZero);
}

/// <summary>A count of one note or coin: its face value and how many.</summary>
/// <param name="FaceValue">The note or coin's value.</param>
/// <param name="Pieces">How many were counted.</param>
public readonly record struct DenominationLine(decimal FaceValue, int Pieces)
{
    /// <summary>The value of the pieces.</summary>
    public decimal Amount => FaceValue * Pieces;
}

/// <summary>
/// Cash counted note by note (brief §4.1): "50x10 + 20x5 + 0.5x4" is ten 50s, five 20s and four 50-cent coins; a part without "x" is an amount
/// counted loose (e.g. a bag of mixed coins). The total is what the cash-up uses; the lines are shown back so the counter can check them.
/// </summary>
public static class DenominationCount
{
    /// <summary>Reads a count.</summary>
    /// <param name="text">The typed count.</param>
    /// <param name="total">The total value.</param>
    /// <param name="lines">The note-and-coin lines (loose amounts have a face value of the amount and one piece).</param>
    /// <returns>False when a part cannot be read, a face value is not positive or a number of pieces is negative or not whole.</returns>
    public static bool TryRead(string? text, out decimal total, out IReadOnlyList<DenominationLine> lines)
    {
        List<DenominationLine> list = [];
        lines = list;
        total = 0m;
        string compact = string.Concat((text ?? string.Empty).Where(ch => !char.IsWhiteSpace(ch))).Replace('×', 'x').Replace('X', 'x').Replace('*', 'x');
        if (compact.Length == 0)
        {
            return false;
        }

        foreach (string part in compact.Split('+'))
        {
            string[] bits = part.Split('x');
            if (bits.Length > 2 || !Number(bits[0], out decimal face) || face <= 0m && bits.Length == 2 || face < 0m)
            {
                return false;
            }

            int pieces = 1;
            if (bits.Length == 2)
            {
                if (!Number(bits[1], out decimal count) || count < 0m || count != decimal.Truncate(count) || count > int.MaxValue)
                {
                    return false;
                }

                pieces = (int)count;
            }

            DenominationLine line = new(face, pieces);
            list.Add(line);
            total += line.Amount;
        }

        return true;
    }

    /// <summary>The first counted face value that is not one of the currency's notes or coins (loose amounts are not checked).</summary>
    /// <param name="text">The typed count.</param>
    /// <param name="faceValues">The currency's notes and coins; when empty, any face value is accepted.</param>
    /// <returns>The unknown face value, or null.</returns>
    public static decimal? UnknownFace(string? text, IReadOnlyCollection<decimal> faceValues)
    {
        ArgumentNullException.ThrowIfNull(faceValues);
        if (faceValues.Count == 0 || !TryRead(text, out _, out _))
        {
            return null;
        }

        string compact = string.Concat((text ?? string.Empty).Where(ch => !char.IsWhiteSpace(ch))).Replace('×', 'x').Replace('X', 'x').Replace('*', 'x');
        foreach (string part in compact.Split('+').Where(p => p.Contains('x', StringComparison.Ordinal)))
        {
            _ = Number(part.Split('x')[0], out decimal face);
            if (!faceValues.Contains(face))
            {
                return face;
            }
        }

        return null;
    }

    /// <summary>The lines as text: "10 × 50 = 500.00; 5 × 20 = 100.00".</summary>
    /// <param name="lines">The lines.</param>
    /// <returns>The text.</returns>
    public static string Describe(IEnumerable<DenominationLine> lines) => string.Join("; ", lines.Select(l => l.Pieces == 1 && !IsWhole(l.FaceValue)
        ? l.Amount.ToString("0.00", global::System.Globalization.CultureInfo.InvariantCulture)
        : $"{l.Pieces} × {l.FaceValue.ToString("0.##", global::System.Globalization.CultureInfo.InvariantCulture)} = {l.Amount.ToString("0.00", global::System.Globalization.CultureInfo.InvariantCulture)}"));

    private static bool IsWhole(decimal value) => value == decimal.Truncate(value);

    private static bool Number(string text, out decimal value)
        => decimal.TryParse(text, global::System.Globalization.NumberStyles.Number, global::System.Globalization.CultureInfo.InvariantCulture, out value);
}

/// <summary>Reads a till count typed as "120" (base), "50x10+20x5" (notes and coins) or "120; ZAR 100x5; EUR 20" (brief §4.1).</summary>
public static class CashCounts
{
    /// <summary>Parses the count.</summary>
    /// <param name="text">The typed count.</param>
    /// <param name="baseCurrency">The base currency, for an amount typed without one.</param>
    /// <param name="counts">The counts, one per currency.</param>
    /// <returns>False when a part cannot be read or a currency is repeated.</returns>
    public static bool TryParse(string? text, string baseCurrency, out IReadOnlyList<CashCount> counts)
    {
        List<CashCount> list = [];
        counts = list;
        foreach (string raw in (text ?? string.Empty).Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // "50 x 10 + 20 x 5" is read as one amount: close up the spaces around the note-count signs.
            string part = global::System.Text.RegularExpressions.Regex.Replace(raw, @"(?<=\d)\s*([+xX×*])\s*(?=\d)", "$1");
            string[] bits = part.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string currency = bits.Length == 2 ? (char.IsLetter(bits[0][0]) ? bits[0] : bits[1]) : baseCurrency;
            string amount = bits.Length == 2 ? (char.IsLetter(bits[0][0]) ? bits[1] : bits[0]) : bits[0];
            if (bits.Length > 2 || currency.Length != 3 || !currency.All(char.IsLetter) || !DenominationCount.TryRead(amount, out decimal value, out _) || value < 0m)
            {
                return false;
            }

            currency = currency.ToUpperInvariant();
            if (list.Any(c => c.Currency == currency))
            {
                return false;
            }

            list.Add(new CashCount(currency, value));
        }

        return list.Count > 0;
    }
}

/// <summary>Cash rounding when the smallest coins are scarce (brief §4.1).</summary>
public static class CashRounding
{
    /// <summary>An amount to the nearest increment (midpoint away from zero).</summary>
    /// <param name="amount">The exact amount.</param>
    /// <param name="increment">The smallest cash unit (e.g. 0.05).</param>
    /// <returns>The amount to collect in cash.</returns>
    public static decimal Round(decimal amount, decimal increment)
        => increment <= 0m ? amount : decimal.Round(amount / increment, 0, MidpointRounding.AwayFromZero) * increment;
}
