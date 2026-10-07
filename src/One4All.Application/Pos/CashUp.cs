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

/// <summary>Reads a till count typed as "120" (base) or "120; ZAR 500; EUR 20" (brief §4.1).</summary>
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
        foreach (string part in (text ?? string.Empty).Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] bits = part.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string currency = bits.Length == 2 ? (char.IsLetter(bits[0][0]) ? bits[0] : bits[1]) : baseCurrency;
            string amount = bits.Length == 2 ? (char.IsLetter(bits[0][0]) ? bits[1] : bits[0]) : bits[0];
            if (bits.Length > 2 || currency.Length != 3 || !currency.All(char.IsLetter)
                || !decimal.TryParse(amount, global::System.Globalization.NumberStyles.Number, global::System.Globalization.CultureInfo.InvariantCulture, out decimal value) || value < 0m)
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
