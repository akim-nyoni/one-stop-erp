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
