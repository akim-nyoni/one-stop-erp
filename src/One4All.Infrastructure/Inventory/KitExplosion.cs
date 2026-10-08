using Microsoft.EntityFrameworkCore;
using One4All.Core.Inventory;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Inventory;

/// <summary>
/// What leaves (or comes back to) stock when an item is sold or returned (brief §4.2: "kits/BOM (phantom…)"): a stock-tracked item moves itself;
/// a kit moves the components of its active bill of materials, scaled from the batch to the quantity sold, with kits inside kits exploded in turn.
/// </summary>
internal static class KitExplosion
{
    private const int MaxDepth = 8;

    /// <summary>The stock lines behind a quantity of an item.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="companyId">The company.</param>
    /// <param name="itemId">The item sold.</param>
    /// <param name="quantity">The quantity sold.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Each stock-tracked item (with a cost-of-sales account) and its quantity; empty for services and non-stock items.</returns>
    public static async Task<List<(Item Item, decimal Quantity)>> StockLinesAsync(One4AllDbContext db, long companyId, long itemId, decimal quantity, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        List<(Item, decimal)> lines = [];
        await ExplodeAsync(db, companyId, itemId, quantity, 0, lines, ct).ConfigureAwait(false);
        return [.. lines.GroupBy(l => l.Item1.Id).Select(g => (g.First().Item1, g.Sum(l => l.Item2)))];
    }

    private static async Task ExplodeAsync(One4AllDbContext db, long companyId, long itemId, decimal quantity, int depth, List<(Item, decimal)> lines, CancellationToken ct)
    {
        Item? item = await db.Set<Item>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId && i.CompanyId == companyId, ct).ConfigureAwait(false);
        if (item is null)
        {
            return;
        }

        if (item.IsStockTracked)
        {
            if (!string.IsNullOrEmpty(item.CostOfSalesAccountCode))
            {
                lines.Add((item, quantity));
            }

            return;
        }

        if (item.ItemType != ItemType.Kit || depth >= MaxDepth)
        {
            return;
        }

        BillOfMaterials? bom = await db.BillsOfMaterials.AsNoTracking().Include(b => b.Lines)
            .Where(b => b.CompanyId == companyId && b.ParentItemId == itemId && b.IsActive && b.OutputQuantity > 0m).OrderBy(b => b.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (bom is null)
        {
            return;
        }

        foreach (BomLine line in bom.Lines)
        {
            await ExplodeAsync(db, companyId, line.ComponentItemId, line.Quantity * quantity / bom.OutputQuantity, depth + 1, lines, ct).ConfigureAwait(false);
        }
    }
}
