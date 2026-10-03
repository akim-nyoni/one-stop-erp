namespace One4All.Application.Municipal;

/// <summary>A property value and the date it takes effect, in the order it was recorded.</summary>
/// <param name="EffectiveFrom">The first day it applies.</param>
/// <param name="Value">The value.</param>
public readonly record struct DatedValue(DateOnly EffectiveFrom, decimal Value);

/// <summary>
/// Valuation roll arithmetic (brief §4.9): which value applies on a date, and how much rates bills already raised must
/// change when a supplementary valuation or an objection outcome is backdated over them. Pure and unit-tested.
/// </summary>
public static class ValuationRoll
{
    /// <summary>
    /// The value in force on a date: the record with the latest effective date on or before it; of records with the same
    /// effective date, the one recorded last (an objection outcome replaces the value it was lodged against).
    /// </summary>
    /// <param name="history">The records in the order they were made.</param>
    /// <param name="date">The date.</param>
    /// <param name="fallback">The value when no record applies yet.</param>
    /// <returns>The value.</returns>
    public static decimal ValueAt(IEnumerable<DatedValue> history, DateOnly date, decimal fallback)
    {
        ArgumentNullException.ThrowIfNull(history);
        DatedValue? best = null;
        foreach (DatedValue record in history)
        {
            if (record.EffectiveFrom <= date && (best is null || record.EffectiveFrom >= best.Value.EffectiveFrom))
            {
                best = record;
            }
        }

        return best?.Value ?? fallback;
    }

    /// <summary>The total change to bills already raised when the history changes, rounded to cents.</summary>
    /// <param name="billDates">The dates of the bills affected.</param>
    /// <param name="before">The history the bills were raised on.</param>
    /// <param name="after">The history with the new record.</param>
    /// <param name="fallback">The value when no record applies.</param>
    /// <param name="charge">The charge a bill makes on a value.</param>
    /// <returns>The amount to invoice (positive) or credit (negative).</returns>
    public static decimal BackBilling(IEnumerable<DateOnly> billDates, IReadOnlyList<DatedValue> before, IReadOnlyList<DatedValue> after, decimal fallback, Func<decimal, decimal> charge)
    {
        ArgumentNullException.ThrowIfNull(charge);
        return BackBilling(billDates, before, after, fallback, (_, value) => charge(value));
    }

    /// <summary>The total change to bills already raised, with a charge that depends on the bill date (tariff versions).</summary>
    /// <param name="billDates">The dates of the bills affected.</param>
    /// <param name="before">The history the bills were raised on.</param>
    /// <param name="after">The history with the new record.</param>
    /// <param name="fallback">The value when no record applies.</param>
    /// <param name="charge">The charge a bill on a date makes on a value.</param>
    /// <returns>The amount to invoice (positive) or credit (negative).</returns>
    public static decimal BackBilling(IEnumerable<DateOnly> billDates, IReadOnlyList<DatedValue> before, IReadOnlyList<DatedValue> after, decimal fallback, Func<DateOnly, decimal, decimal> charge)
    {
        ArgumentNullException.ThrowIfNull(billDates);
        ArgumentNullException.ThrowIfNull(charge);
        decimal total = billDates.Sum(d => charge(d, ValueAt(after, d, fallback)) - charge(d, ValueAt(before, d, fallback)));
        return decimal.Round(total, 2, MidpointRounding.AwayFromZero);
    }
}
