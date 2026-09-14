namespace One4All.Core.System;

/// <summary>
/// Exchange-rate types maintained with full effective-dated history (brief §3.3). Different modules and
/// documents select different types (e.g. invoices at <see cref="Daily"/>, revaluation at <see cref="ClosingRate"/>,
/// consolidation P&amp;L at <see cref="PeriodAverage"/>).
/// </summary>
public enum ExchangeRateType
{
    /// <summary>Intraday spot rate.</summary>
    Spot = 1,

    /// <summary>The prevailing daily rate.</summary>
    Daily = 2,

    /// <summary>Average rate over a period (P&amp;L translation).</summary>
    PeriodAverage = 3,

    /// <summary>Closing rate at period end (balance-sheet translation and revaluation).</summary>
    ClosingRate = 4,

    /// <summary>Rate fixed for budgeting.</summary>
    Budget = 5,

    /// <summary>A fixed/contract rate agreed on a transaction.</summary>
    Fixed = 6,
}

/// <summary>How an exchange rate is quoted.</summary>
public enum QuotationType
{
    /// <summary>Direct quotation: 1 unit of foreign currency = rate units of base currency.</summary>
    Direct = 1,

    /// <summary>Indirect quotation: 1 unit of base currency = rate units of foreign currency.</summary>
    Indirect = 2,
}

/// <summary>Rounding mode for currency amounts and cash tenders.</summary>
public enum RoundingMode
{
    /// <summary>Banker's rounding (round half to even).</summary>
    HalfToEven = 0,

    /// <summary>Round half away from zero (commercial rounding).</summary>
    HalfAwayFromZero = 1,

    /// <summary>Always round up (ceiling).</summary>
    Up = 2,

    /// <summary>Always round down (floor / truncate).</summary>
    Down = 3,
}
