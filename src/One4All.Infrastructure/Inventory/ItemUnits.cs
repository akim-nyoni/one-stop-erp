using Microsoft.EntityFrameworkCore;
using One4All.Core.Common;
using One4All.Core.Inventory;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Inventory;

/// <summary>Turns an ordering unit (a box, a case, a pallet) into the item's own unit, through the item's unit list.</summary>
internal static class ItemUnits
{
    /// <summary>How many of the item's own unit one of the given unit holds.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="companyId">The company.</param>
    /// <param name="itemId">The item.</param>
    /// <param name="unitCode">The unit (blank or the item's own: 1).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The unit as recorded (null for the item's own) and its factor, or why the unit is unknown.</returns>
    public static async Task<Result<(string? Unit, decimal Factor)>> FactorAsync(One4AllDbContext db, long companyId, long itemId, string? unitCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(unitCode))
        {
            return (null, 1m);
        }

        string code = unitCode.Trim().ToUpperInvariant();
        string? own = await db.Items.AsNoTracking().Where(i => i.Id == itemId && i.CompanyId == companyId).Select(i => i.UnitOfMeasure).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (own is not null && string.Equals(own, code, StringComparison.OrdinalIgnoreCase))
        {
            return (null, 1m);
        }

        decimal? factor = await db.Set<ItemUnit>().AsNoTracking().Where(u => u.CompanyId == companyId && u.ItemId == itemId && u.UnitCode.ToUpper() == code)
            .Select(u => (decimal?)u.Factor).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return factor is decimal f && f > 0m
            ? (code, f)
            : Error.Validation("Inv.UnitUnknown", $"The item is not set up to be ordered in {code}; add the unit and how many {own ?? "units"} it holds.");
    }
}
