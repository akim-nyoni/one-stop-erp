using One4All.Core.Accounting;
using One4All.Core.Common;

namespace One4All.Core.System;

/// <summary>
/// A currency in the currency master (<c>sys.currency</c>): global reference data a company draws its
/// base and reporting currencies from. Carries the display symbol, minor-unit decimals, cash-rounding
/// rule and the denomination list used for POS cash tendering (brief §3.3).
/// </summary>
public sealed class Currency : SoftDeletableEntity
{
    private readonly List<CurrencyDenomination> _denominations = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private Currency()
    {
        Name = string.Empty;
        Symbol = string.Empty;
    }

    /// <summary>Creates a currency.</summary>
    /// <param name="code">The ISO 4217 code.</param>
    /// <param name="name">Display name.</param>
    /// <param name="symbol">Display symbol.</param>
    /// <param name="decimalPlaces">Minor-unit decimal places (e.g. 2 for USD, 0 for JPY).</param>
    /// <param name="roundingIncrement">Smallest cash tender increment (e.g. 0.01 or 0.05).</param>
    /// <param name="roundingMode">How amounts are rounded.</param>
    public Currency(
        CurrencyCode code,
        string name,
        string symbol,
        int decimalPlaces = 2,
        decimal roundingIncrement = 0.01m,
        RoundingMode roundingMode = RoundingMode.HalfToEven)
    {
        Code = code;
        Name = name;
        Symbol = symbol;
        DecimalPlaces = decimalPlaces;
        RoundingIncrement = roundingIncrement;
        RoundingMode = roundingMode;
    }

    /// <summary>The ISO 4217 code (also the natural key).</summary>
    public CurrencyCode Code { get; private set; }

    /// <summary>Display name.</summary>
    public string Name { get; set; }

    /// <summary>Display symbol.</summary>
    public string Symbol { get; set; }

    /// <summary>Minor-unit decimal places.</summary>
    public int DecimalPlaces { get; set; }

    /// <summary>Smallest cash tender increment for rounding change (e.g. 0.05).</summary>
    public decimal RoundingIncrement { get; set; }

    /// <summary>How amounts in this currency are rounded.</summary>
    public RoundingMode RoundingMode { get; set; }

    /// <summary>The cash denominations available (notes and coins), for tender and change breakdown.</summary>
    public IReadOnlyCollection<CurrencyDenomination> Denominations => _denominations.AsReadOnly();

    /// <summary>The smallest amount that can be handed over in cash: the smallest denomination, else the rounding increment, else one minor unit.</summary>
    public decimal SmallestCashUnit => _denominations.Count > 0 ? _denominations.Min(d => d.FaceValue)
        : RoundingIncrement > 0m ? RoundingIncrement : 1m / (decimal)Math.Pow(10, DecimalPlaces);

    /// <summary>Adds a denomination.</summary>
    /// <param name="faceValue">The face value (e.g. 20 for a $20 note).</param>
    /// <param name="isNote">True for a note, false for a coin.</param>
    public void AddDenomination(decimal faceValue, bool isNote)
    {
        if (_denominations.All(d => d.FaceValue != faceValue))
        {
            _denominations.Add(new CurrencyDenomination(faceValue, isNote));
        }
    }

    /// <summary>
    /// Rounds an amount to this currency's <see cref="RoundingIncrement"/> using its <see cref="RoundingMode"/>.
    /// </summary>
    /// <param name="amount">The raw amount.</param>
    /// <returns>The rounded amount.</returns>
    public decimal Round(decimal amount)
    {
        if (RoundingIncrement <= 0m)
        {
            return amount;
        }

        decimal quotient = amount / RoundingIncrement;
        decimal rounded = RoundingMode switch
        {
            RoundingMode.HalfToEven => Math.Round(quotient, 0, MidpointRounding.ToEven),
            RoundingMode.HalfAwayFromZero => Math.Round(quotient, 0, MidpointRounding.AwayFromZero),
            RoundingMode.Up => Math.Ceiling(quotient),
            RoundingMode.Down => Math.Floor(quotient),
            _ => Math.Round(quotient, 0, MidpointRounding.ToEven),
        };

        return rounded * RoundingIncrement;
    }
}

/// <summary>A cash denomination (note or coin) of a currency (<c>sys.currency_denomination</c>).</summary>
public sealed class CurrencyDenomination : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private CurrencyDenomination()
    {
    }

    /// <summary>Creates a denomination.</summary>
    /// <param name="faceValue">The face value.</param>
    /// <param name="isNote">True for a note, false for a coin.</param>
    public CurrencyDenomination(decimal faceValue, bool isNote)
    {
        FaceValue = faceValue;
        IsNote = isNote;
    }

    /// <summary>The owning currency id.</summary>
    public long CurrencyId { get; private set; }

    /// <summary>The face value.</summary>
    public decimal FaceValue { get; private set; }

    /// <summary>True for a note, false for a coin.</summary>
    public bool IsNote { get; private set; }
}
