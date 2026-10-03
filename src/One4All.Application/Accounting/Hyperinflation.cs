using One4All.Core.Accounting;

namespace One4All.Application.Accounting;

/// <summary>An account's net movement in one month, debit positive.</summary>
/// <param name="Month">The first day of the month.</param>
/// <param name="Amount">The net base amount.</param>
public readonly record struct MonthlyMovement(DateOnly Month, decimal Amount);

/// <summary>
/// IAS 29 restatement arithmetic: monetary items stay as they are; non-monetary items are restated by the closing index
/// over the index for the month each amount was recorded. Pure and unit-tested.
/// </summary>
public static class Hyperinflation
{
    /// <summary>Whether an account is monetary for restatement.</summary>
    /// <param name="type">The account type.</param>
    /// <param name="control">The control account type.</param>
    /// <param name="classification">The account's own classification.</param>
    /// <returns>True when monetary.</returns>
    public static bool IsMonetary(AccountType type, ControlAccountType control, Ias29Classification classification) => classification switch
    {
        Ias29Classification.Monetary => true,
        Ias29Classification.NonMonetary => false,
        _ => type is AccountType.Asset or AccountType.Liability && control != ControlAccountType.Stock,
    };

    /// <summary>The restatement factor from a month's index to the closing index.</summary>
    /// <param name="closingIndex">The index at the reporting date.</param>
    /// <param name="indexThen">The index when the amount was recorded.</param>
    /// <returns>The factor.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the earlier index is not positive.</exception>
    public static decimal Factor(decimal closingIndex, decimal indexThen)
        => indexThen <= 0m ? throw new ArgumentOutOfRangeException(nameof(indexThen), "A price index must be positive.") : closingIndex / indexThen;

    /// <summary>Restates a non-monetary account's movements to the closing index, to the cent.</summary>
    /// <param name="movements">The account's movements by month.</param>
    /// <param name="indexFor">The index for a month.</param>
    /// <param name="closingIndex">The index at the reporting date.</param>
    /// <returns>The restated balance, debit positive.</returns>
    public static decimal Restate(IEnumerable<MonthlyMovement> movements, Func<DateOnly, decimal> indexFor, decimal closingIndex)
    {
        ArgumentNullException.ThrowIfNull(movements);
        ArgumentNullException.ThrowIfNull(indexFor);
        return decimal.Round(movements.Sum(m => m.Amount * Factor(closingIndex, indexFor(m.Month))), 2, MidpointRounding.AwayFromZero);
    }
}
