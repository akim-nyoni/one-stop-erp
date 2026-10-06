using One4All.Core.Common;

namespace One4All.Application.Pos;

/// <summary>What the till must know about an item before selling it.</summary>
/// <param name="ItemId">The item.</param>
/// <param name="Code">Its code.</param>
/// <param name="MinimumAge">The buyer's minimum age, if any.</param>
/// <param name="MaxQuantityPerSale">The most one sale may hold, if limited.</param>
/// <param name="RequiresPrescription">Whether it is prescription-only.</param>
public sealed record ItemSaleLimits(long ItemId, string Code, int? MinimumAge, decimal? MaxQuantityPerSale, bool RequiresPrescription);

/// <summary>Age checks, per-sale quantity limits and prescription-only items at the till (brief §4.1).</summary>
public static class ItemSaleRules
{
    /// <summary>
    /// Checks a sale: an age-restricted item needs the age check confirmed (<c>Pos.AgeCheck</c>), an item's quantity across the sale's
    /// lines may not pass its limit (<c>Pos.MaxQuantity</c>), and a prescription-only item needs a prescription reference
    /// (<c>Pos.PrescriptionRequired</c>).
    /// </summary>
    /// <param name="lines">Each line's item and quantity.</param>
    /// <param name="limits">The items' limits.</param>
    /// <param name="ageVerified">Whether the age check was confirmed.</param>
    /// <param name="prescriptionReference">The prescription, if any.</param>
    /// <returns>Success, or the first rule broken.</returns>
    public static Result Check(IEnumerable<(long ItemId, decimal Quantity)> lines, IReadOnlyDictionary<long, ItemSaleLimits> limits, bool ageVerified, string? prescriptionReference)
    {
        foreach (var item in lines.GroupBy(l => l.ItemId).Select(g => (Id: g.Key, Quantity: g.Sum(l => l.Quantity))))
        {
            if (!limits.TryGetValue(item.Id, out ItemSaleLimits? limit))
            {
                continue;
            }

            if (limit.MinimumAge is int age && !ageVerified)
            {
                return Result.Failure(Error.Rule("Pos.AgeCheck", $"{limit.Code} may only be sold to buyers aged {age} or over; confirm the age check."));
            }

            if (limit.MaxQuantityPerSale is decimal max && item.Quantity > max)
            {
                return Result.Failure(Error.Rule("Pos.MaxQuantity", $"At most {max:0.##} of {limit.Code} may be sold in one sale."));
            }

            if (limit.RequiresPrescription && string.IsNullOrWhiteSpace(prescriptionReference))
            {
                return Result.Failure(Error.Rule("Pos.PrescriptionRequired", $"{limit.Code} is prescription-only; record the prescription."));
            }
        }

        return Result.Success();
    }
}
