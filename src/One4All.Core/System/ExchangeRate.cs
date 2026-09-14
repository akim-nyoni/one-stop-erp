using One4All.Core.Accounting;
using One4All.Core.Common;

namespace One4All.Core.System;

/// <summary>
/// An effective-dated exchange rate between two currencies for a given <see cref="ExchangeRateType"/>
/// (<c>sys.exchange_rate</c>). Full history is retained so a prior-period transaction always uses the
/// rate that was in force then (brief §3.3). Rates are <c>numeric(19,6)</c>.
/// </summary>
public sealed class ExchangeRate : AuditableEntity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private ExchangeRate()
    {
    }

    /// <summary>Creates an exchange rate.</summary>
    /// <param name="fromCurrency">The source currency.</param>
    /// <param name="toCurrency">The target currency (usually the company base).</param>
    /// <param name="rateType">The rate type.</param>
    /// <param name="effectiveFrom">The date from which the rate applies.</param>
    /// <param name="rate">The rate value.</param>
    /// <param name="quotation">Whether the rate is quoted directly or indirectly.</param>
    public ExchangeRate(
        CurrencyCode fromCurrency,
        CurrencyCode toCurrency,
        ExchangeRateType rateType,
        DateOnly effectiveFrom,
        decimal rate,
        QuotationType quotation = QuotationType.Direct)
    {
        FromCurrency = fromCurrency;
        ToCurrency = toCurrency;
        RateType = rateType;
        EffectiveFrom = effectiveFrom;
        Rate = rate;
        Quotation = quotation;
    }

    /// <summary>The source currency.</summary>
    public CurrencyCode FromCurrency { get; private set; }

    /// <summary>The target currency.</summary>
    public CurrencyCode ToCurrency { get; private set; }

    /// <summary>The rate type.</summary>
    public ExchangeRateType RateType { get; private set; }

    /// <summary>The accounting date from which this rate is effective.</summary>
    public DateOnly EffectiveFrom { get; private set; }

    /// <summary>The rate value, stored <c>numeric(19,6)</c>.</summary>
    public decimal Rate { get; private set; }

    /// <summary>Whether the rate is quoted directly or indirectly.</summary>
    public QuotationType Quotation { get; private set; }

    /// <summary>
    /// Converts an amount of <see cref="FromCurrency"/> to <see cref="ToCurrency"/> applying the
    /// quotation direction. The result is a raw decimal (caller wraps it in <see cref="Money"/> of the
    /// target currency at the appropriate scale).
    /// </summary>
    /// <param name="amount">The source amount.</param>
    /// <returns>The converted amount.</returns>
    public decimal Convert(decimal amount) =>
        Quotation == QuotationType.Direct ? amount * Rate : amount / Rate;
}
