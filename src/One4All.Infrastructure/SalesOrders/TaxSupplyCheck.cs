using Microsoft.EntityFrameworkCore;
using One4All.Core.Common;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.SalesOrders;

/// <summary>Refuses a tax code on a line for the wrong kind of supply (brief §3.5: a code applies to goods, services or both).</summary>
internal static class TaxSupplyCheck
{
    /// <summary>The first line whose tax code (its own, else the item's) does not apply to its item, as an error; null when every line is fine.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="companyId">The company.</param>
    /// <param name="lines">Each line's item and tax code.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The error, or null.</returns>
    public static async Task<Error?> CheckAsync(One4AllDbContext db, long companyId, IEnumerable<(long ItemId, string? TaxCode)> lines, CancellationToken ct)
    {
        List<(long ItemId, string? TaxCode)> given = [.. lines];
        List<long> itemIds = [.. given.Select(l => l.ItemId).Distinct()];
        var items = await db.Items.AsNoTracking().Where(i => i.CompanyId == companyId && itemIds.Contains(i.Id))
            .Select(i => new { i.Id, i.Code, i.ItemType, i.TaxCode }).ToDictionaryAsync(i => i.Id, ct).ConfigureAwait(false);
        List<string> codes = [.. given.Select(l => (l.TaxCode ?? (items.TryGetValue(l.ItemId, out var item) ? item.TaxCode : null))?.Trim().ToUpperInvariant()).Where(c => !string.IsNullOrEmpty(c)).Select(c => c!).Distinct()];
        if (codes.Count == 0)
        {
            return null;
        }

        Dictionary<string, Core.Accounting.TaxCode> taxes = await db.TaxCodes.AsNoTracking().Where(t => t.CompanyId == companyId && codes.Contains(t.Code)).ToDictionaryAsync(t => t.Code, ct).ConfigureAwait(false);
        foreach ((long itemId, string? lineCode) in given)
        {
            if (!items.TryGetValue(itemId, out var item))
            {
                continue;
            }

            string? code = (lineCode ?? item.TaxCode)?.Trim().ToUpperInvariant();
            if (code is not null && taxes.TryGetValue(code, out Core.Accounting.TaxCode? tax) && !tax.Suits(item.ItemType))
            {
                return Error.Rule("Tax.WrongSupply", $"Tax code {tax.Code} applies to {tax.AppliesTo.ToString().ToLowerInvariant()} only; item {item.Code} is {(item.ItemType == Core.Inventory.ItemType.Service ? "a service" : "goods")}.");
            }
        }

        return null;
    }
}
