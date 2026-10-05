using One4All.Core.Common;
using One4All.Core.Inventory;

namespace One4All.Application.Inventory;

/// <summary>A batch or serial number and the quantity of it moving.</summary>
/// <param name="LotNumber">The batch or serial number.</param>
/// <param name="Quantity">The quantity (unsigned).</param>
/// <param name="ExpiryDate">The batch's expiry, for receipts.</param>
public readonly record struct LotQuantity(string LotNumber, decimal Quantity, DateOnly? ExpiryDate = null);

/// <summary>A lot on hand, for picking.</summary>
/// <param name="LotNumber">The lot.</param>
/// <param name="OnHand">The quantity on hand.</param>
/// <param name="ExpiryDate">Its expiry date.</param>
/// <param name="FirstReceived">When it first came in.</param>
public readonly record struct LotOnHand(string LotNumber, decimal OnHand, DateOnly? ExpiryDate, DateOnly FirstReceived);

/// <summary>Batch / serial checks and first-expiry-first-out picking.</summary>
public static class LotRules
{
    /// <summary>Checks the lots offered for a movement of an item.</summary>
    /// <param name="tracking">How the item is traced.</param>
    /// <param name="movementQuantity">The movement's quantity (unsigned).</param>
    /// <param name="lots">The lots offered.</param>
    /// <returns>Null when they account for the movement; otherwise the error.</returns>
    public static Error? Validate(ItemTracking tracking, decimal movementQuantity, IReadOnlyList<LotQuantity> lots)
    {
        if (tracking == ItemTracking.None)
        {
            return Error.Rule("Inv.NotTracked", "The item is not batch or serial tracked.");
        }

        if (lots.Count == 0 || lots.Any(l => string.IsNullOrWhiteSpace(l.LotNumber) || l.Quantity <= 0m))
        {
            return Error.Validation("Inv.LotLines", "Each lot needs a number and a positive quantity.");
        }

        if (lots.Sum(l => l.Quantity) != movementQuantity)
        {
            return Error.Validation("Inv.LotQuantityMismatch", $"The lots total {lots.Sum(l => l.Quantity):0.###} but the movement is {movementQuantity:0.###}.");
        }

        if (tracking == ItemTracking.Serial)
        {
            if (lots.Any(l => l.Quantity != 1m))
            {
                return Error.Validation("Inv.SerialQuantity", "Each serial number is exactly one unit.");
            }

            if (lots.Select(l => l.LotNumber.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != lots.Count)
            {
                return Error.Validation("Inv.DuplicateSerial", "A serial number appears twice.");
            }
        }

        return null;
    }

    /// <summary>The expiry to record on a received batch: as given, else the receipt date plus the item's shelf life.</summary>
    /// <param name="given">The expiry given.</param>
    /// <param name="received">The receipt date.</param>
    /// <param name="shelfLifeDays">The item's shelf life.</param>
    /// <returns>The expiry, or null when neither is known.</returns>
    public static DateOnly? Expiry(DateOnly? given, DateOnly received, int? shelfLifeDays)
        => given ?? (shelfLifeDays is int days ? received.AddDays(days) : null);

    /// <summary>Picks a quantity first-expiry-first-out: earliest expiry first, lots without expiry last, then oldest receipt; skips expired lots.</summary>
    /// <param name="onHand">The lots on hand.</param>
    /// <param name="quantity">The quantity needed.</param>
    /// <param name="asAt">The picking date (lots expiring before it are not picked).</param>
    /// <returns>The picks, and the shortfall when there is not enough.</returns>
    public static (IReadOnlyList<LotQuantity> Picks, decimal Shortfall) Fefo(IEnumerable<LotOnHand> onHand, decimal quantity, DateOnly asAt)
    {
        List<LotQuantity> picks = [];
        decimal left = quantity;
        foreach (LotOnHand lot in onHand.Where(l => l.OnHand > 0m && (l.ExpiryDate is null || l.ExpiryDate >= asAt))
                     .OrderBy(l => l.ExpiryDate ?? DateOnly.MaxValue).ThenBy(l => l.FirstReceived).ThenBy(l => l.LotNumber, StringComparer.Ordinal))
        {
            if (left <= 0m)
            {
                break;
            }

            decimal take = Math.Min(left, lot.OnHand);
            picks.Add(new LotQuantity(lot.LotNumber, take, lot.ExpiryDate));
            left -= take;
        }

        return (picks, left);
    }

    /// <summary>How close a batch is to expiry.</summary>
    /// <param name="expiry">The expiry date.</param>
    /// <param name="asAt">The date.</param>
    /// <param name="warningDays">The look-ahead window.</param>
    /// <returns>Expired, Expiring (inside the window) or OK.</returns>
    public static string ExpiryStatus(DateOnly expiry, DateOnly asAt, int warningDays)
        => expiry < asAt ? "Expired" : expiry.DayNumber - asAt.DayNumber <= warningDays ? "Expiring" : "OK";
}
