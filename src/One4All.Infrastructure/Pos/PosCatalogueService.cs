using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Pos;
using One4All.Core.Common;
using One4All.Core.Inventory;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Pos;

/// <summary>The till's tiles from the item master and the default price list.</summary>
public sealed class PosCatalogueService : IPosCatalogueService
{
    private const int MaxCostPlusLookups = 1_000;

    private readonly One4AllDbContext _db;
    private readonly IPricingService _pricing;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="pricing">Prices items a cost-plus list does not list explicitly.</param>
    public PosCatalogueService(One4AllDbContext db, IPricingService pricing)
    {
        _db = db;
        _pricing = pricing;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PosTile>> TilesAsync(long companyId, CancellationToken ct = default)
    {
        List<Item> items = await _db.Items.AsNoTracking().Where(i => i.CompanyId == companyId && i.IsActive && i.RevenueAccountCode != null && i.RevenueAccountCode != string.Empty)
            .OrderBy(i => i.Category).ThenBy(i => i.Description).ToListAsync(ct).ConfigureAwait(false);
        PriceList? list = await _db.PriceLists.AsNoTracking().Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.CompanyId == companyId && p.IsDefault && p.IsActive, ct).ConfigureAwait(false);
        if (list is null)
        {
            return [];
        }

        // The price of one: the explicit line with the largest break not above one.
        Dictionary<long, decimal> explicitPrices = list.Lines.Where(l => l.MinimumQuantity <= 1m).GroupBy(l => l.ItemId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(l => l.MinimumQuantity).First().UnitPrice);
        List<PosTile> tiles = [];
        int lookups = 0;
        foreach (Item item in items)
        {
            decimal? price = explicitPrices.TryGetValue(item.Id, out decimal listed) ? listed : null;
            if (price is null && list.CostPlusBasis != CostPlusBasis.None && lookups++ < MaxCostPlusLookups)
            {
                Result<ResolvedPrice> resolved = await _pricing.PriceAsync(companyId, null, item.Id, 1m, ct).ConfigureAwait(false);
                price = resolved.IsSuccess ? resolved.Value.UnitPrice : null;
            }

            if (price is decimal unitPrice)
            {
                tiles.Add(new PosTile(item.Id, item.Code, item.ShortDescription ?? item.Description, string.IsNullOrWhiteSpace(item.Category) ? "General" : item.Category!, unitPrice,
                    item.RevenueAccountCode!, item.TaxCode, item.Barcode, item.Plu));
            }
        }

        return tiles;
    }
}
