namespace One4All.Application.Municipal;

/// <summary>How a prepaid purchase is split.</summary>
/// <param name="ToArrears">The part taken to pay down the account's arrears.</param>
/// <param name="ToUnits">The part that buys units.</param>
/// <param name="Units">The units bought (rounded down to hundredths, so the council never over-issues).</param>
public sealed record VendSplit(decimal ToArrears, decimal ToUnits, decimal Units);

/// <summary>
/// Prepaid vending arithmetic (brief §4.9 prepaid hooks): the credit-off-arrears rule takes a percentage of each
/// purchase, never more than is owed, towards the account's arrears; the rest buys units at the tariff's unit price.
/// Pure and unit-tested.
/// </summary>
public static class PrepaidVending
{
    /// <summary>Splits a purchase.</summary>
    /// <param name="amount">The amount tendered.</param>
    /// <param name="arrears">What the account owes (nothing when zero or in credit).</param>
    /// <param name="arrearsPercent">The percentage taken towards arrears.</param>
    /// <param name="unitPrice">The price of a unit.</param>
    /// <returns>The split.</returns>
    public static VendSplit Split(decimal amount, decimal arrears, decimal arrearsPercent, decimal unitPrice)
    {
        decimal toArrears = arrears <= 0m ? 0m : Math.Min(arrears, decimal.Round(amount * arrearsPercent / 100m, 2, MidpointRounding.AwayFromZero));
        decimal toUnits = amount - toArrears;
        decimal units = unitPrice <= 0m ? 0m : decimal.Round(toUnits / unitPrice, 2, MidpointRounding.ToZero);
        return new VendSplit(toArrears, toUnits, units);
    }
}

/// <summary>
/// The hook to a prepaid token vendor (for example an STS key management system): turns a sale into the token the
/// customer keys into the meter. The built-in generator is a placeholder for testing; a live installation registers its
/// own.
/// </summary>
public interface IPrepaidTokenGenerator
{
    /// <summary>Generates the token for a sale.</summary>
    /// <param name="meterSerial">The meter.</param>
    /// <param name="units">The units bought.</param>
    /// <param name="saleNumber">The sale number.</param>
    /// <returns>The token.</returns>
    string Generate(string meterSerial, decimal units, string saleNumber);
}
