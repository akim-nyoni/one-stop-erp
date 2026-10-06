using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Inventory;
using One4All.Core.Common;
using One4All.Core.Inventory;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Inventory;

/// <summary>Barcodes, synonyms, pack units, supplier links, search, scan look-up and ABC classes (brief §4.2).</summary>
public sealed class ItemCatalogueService : IItemCatalogueService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public ItemCatalogueService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result<long>> AddAliasAsync(long companyId, long itemId, ItemAliasKind kind, string alias, CancellationToken ct = default)
    {
        string value = (alias ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            return Error.Validation("Inv.AliasRequired", "The barcode or name is required.");
        }

        if (!await _db.Items.AnyAsync(i => i.Id == itemId && i.CompanyId == companyId, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Inv.ItemNotFound", $"Item {itemId} not found.");
        }

        if (kind == ItemAliasKind.Barcode && await BarcodeOwnerAsync(companyId, value, ct).ConfigureAwait(false) is string owner)
        {
            return Error.Conflict("Inv.BarcodeInUse", $"Barcode {value} is already used by {owner}.");
        }

        ItemAlias row = new(companyId, itemId, kind, value);
        _db.ItemAliases.Add(row);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return row.Id;
    }

    /// <inheritdoc />
    public async Task<Result<long>> AddUnitAsync(long companyId, long itemId, string unitCode, decimal factor, string? barcode, CancellationToken ct = default)
    {
        string unit = (unitCode ?? string.Empty).Trim().ToUpperInvariant();
        if (unit.Length == 0 || factor <= 0m)
        {
            return Error.Validation("Inv.UnitInvalid", "A pack unit needs a code and a factor above zero.");
        }

        if (!await _db.Items.AnyAsync(i => i.Id == itemId && i.CompanyId == companyId, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Inv.ItemNotFound", $"Item {itemId} not found.");
        }

        if (await _db.ItemUnits.AnyAsync(u => u.ItemId == itemId && u.UnitCode == unit, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Inv.UnitExists", $"The item already has a {unit} unit.");
        }

        string? code = string.IsNullOrWhiteSpace(barcode) ? null : barcode.Trim();
        if (code is not null && await BarcodeOwnerAsync(companyId, code, ct).ConfigureAwait(false) is string owner)
        {
            return Error.Conflict("Inv.BarcodeInUse", $"Barcode {code} is already used by {owner}.");
        }

        ItemUnit row = new(companyId, itemId, unit, factor, code);
        _db.ItemUnits.Add(row);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return row.Id;
    }

    /// <inheritdoc />
    public async Task<Result<long>> LinkSupplierAsync(ItemSupplierRequest request, CancellationToken ct = default)
    {
        if (request.LeadTimeDays < 0 || request.MinimumOrderQuantity < 0m || request.LastCost < 0m)
        {
            return Error.Validation("Inv.SupplierLinkInvalid", "Lead time, minimum order and cost cannot be negative.");
        }

        if (!await _db.Items.AnyAsync(i => i.Id == request.ItemId && i.CompanyId == request.CompanyId, ct).ConfigureAwait(false)
            || !await _db.Suppliers.AnyAsync(s => s.Id == request.SupplierId && s.CompanyId == request.CompanyId, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Inv.ItemOrSupplierNotFound", "The item and the supplier must both exist.");
        }

        List<ItemSupplier> links = await _db.ItemSuppliers.Where(l => l.ItemId == request.ItemId).ToListAsync(ct).ConfigureAwait(false);
        ItemSupplier? link = links.FirstOrDefault(l => l.SupplierId == request.SupplierId);
        if (link is null)
        {
            link = new ItemSupplier(request.CompanyId, request.ItemId, request.SupplierId);
            _db.ItemSuppliers.Add(link);
        }

        if (request.IsPreferred)
        {
            links.ForEach(l => l.IsPreferred = false);
        }

        link.SupplierItemCode = string.IsNullOrWhiteSpace(request.SupplierItemCode) ? null : request.SupplierItemCode.Trim();
        link.LeadTimeDays = request.LeadTimeDays;
        link.MinimumOrderQuantity = request.MinimumOrderQuantity;
        link.LastCost = request.LastCost;
        link.IsPreferred = request.IsPreferred;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return link.Id;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ItemSearchHit>> SearchAsync(long companyId, string term, int take = 20, CancellationToken ct = default)
    {
        string text = (term ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return [];
        }

        string upper = text.ToUpperInvariant();
        string like = $"%{text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal)}%";
        List<(long Id, string Matched)> matches = [];
        matches.AddRange((await _db.Items.Where(i => i.CompanyId == companyId && (i.Code.ToUpper() == upper || i.Plu == text || i.Barcode == text)).Select(i => i.Id).ToListAsync(ct).ConfigureAwait(false))
            .Select(id => (id, "exact")));
        matches.AddRange((await _db.ItemAliases.Where(a => a.CompanyId == companyId && a.Alias.ToUpper() == upper).Select(a => new { a.ItemId, a.Kind }).ToListAsync(ct).ConfigureAwait(false))
            .Select(a => (a.ItemId, a.Kind == ItemAliasKind.Barcode ? "barcode" : "synonym")));
        matches.AddRange((await _db.ItemUnits.Where(u => u.CompanyId == companyId && u.Barcode == text).Select(u => u.ItemId).ToListAsync(ct).ConfigureAwait(false)).Select(id => (id, "pack barcode")));
        matches.AddRange((await _db.ItemAliases.Where(a => a.CompanyId == companyId && a.Kind == ItemAliasKind.Synonym && EF.Functions.ILike(a.Alias, like)).Select(a => a.ItemId)
            .ToListAsync(ct).ConfigureAwait(false)).Select(id => (id, "synonym")));
        matches.AddRange((await _db.Items.Where(i => i.CompanyId == companyId && (EF.Functions.ILike(i.Description, like) || EF.Functions.ILike(i.Code, like) || (i.Brand != null && EF.Functions.ILike(i.Brand, like))))
            .Select(i => i.Id).Take(take * 2).ToListAsync(ct).ConfigureAwait(false)).Select(id => (id, "description")));

        List<(long Id, string Matched)> ranked = [.. matches.GroupBy(m => m.Id).Select(g => g.First()).Take(take)];
        List<long> ids = [.. ranked.Select(r => r.Id)];
        Dictionary<long, Item> items = await _db.Items.AsNoTracking().Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct).ConfigureAwait(false);
        return [.. ranked.Where(r => items.ContainsKey(r.Id)).Select(r => new ItemSearchHit(r.Id, items[r.Id].Code, items[r.Id].Description, items[r.Id].Brand, r.Matched))];
    }

    /// <inheritdoc />
    public async Task<Result<ItemLookup>> LookupAsync(long companyId, string code, CancellationToken ct = default)
    {
        string text = (code ?? string.Empty).Trim();
        Item? item = await _db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.CompanyId == companyId && (i.Barcode == text || i.Plu == text), ct).ConfigureAwait(false);
        if (item is not null)
        {
            return new ItemLookup(item.Id, item.Code, item.UnitOfMeasure, 1m);
        }

        ItemUnit? unit = await _db.ItemUnits.AsNoTracking().FirstOrDefaultAsync(u => u.CompanyId == companyId && u.Barcode == text, ct).ConfigureAwait(false);
        if (unit is not null)
        {
            string itemCode = await _db.Items.Where(i => i.Id == unit.ItemId).Select(i => i.Code).FirstAsync(ct).ConfigureAwait(false);
            return new ItemLookup(unit.ItemId, itemCode, unit.UnitCode, unit.Factor);
        }

        long? aliased = await _db.ItemAliases.Where(a => a.CompanyId == companyId && a.Kind == ItemAliasKind.Barcode && a.Alias == text).Select(a => (long?)a.ItemId).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (aliased is long id)
        {
            Item found = await _db.Items.AsNoTracking().FirstAsync(i => i.Id == id, ct).ConfigureAwait(false);
            return new ItemLookup(found.Id, found.Code, found.UnitOfMeasure, 1m);
        }

        return Error.NotFound("Inv.NotFound", $"Nothing is coded '{text}'.");
    }

    /// <inheritdoc />
    public async Task<(int A, int B, int C)> ClassifyAbcAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct = default)
    {
        List<Item> items = await _db.Items.Where(i => i.CompanyId == companyId).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, decimal> moved = (await _db.StockMovements.Where(m => m.CompanyId == companyId && m.Type == StockMovementType.Issue && m.Date >= fromDate && m.Date <= toDate)
                .Select(m => new { m.ItemId, m.Value }).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(m => m.ItemId).ToDictionary(g => g.Key, g => Math.Abs(g.Sum(m => m.Value)));
        decimal total = moved.Values.Sum();
        decimal running = 0m;
        int a = 0, b = 0, c = 0;
        foreach (Item item in items.Where(i => i.IsStockTracked).OrderByDescending(i => moved.GetValueOrDefault(i.Id)).ThenBy(i => i.Code, StringComparer.Ordinal))
        {
            decimal value = moved.GetValueOrDefault(item.Id);
            decimal before = total == 0m ? 1m : running / total;
            running += value;
            item.AbcClass = value == 0m ? "C" : before < 0.80m ? "A" : before < 0.95m ? "B" : "C";
            _ = item.AbcClass switch { "A" => a++, "B" => b++, _ => c++ };
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return (a, b, c);
    }

    // Which item already uses a barcode (on the item itself, a pack unit or an alias), or null.
    /// <inheritdoc />
    public async Task<Result> SetSaleLimitsAsync(long companyId, long itemId, int? minimumAge, decimal? maxQuantityPerSale, bool requiresPrescription, CancellationToken ct = default)
    {
        if (minimumAge is <= 0 or > 120 || maxQuantityPerSale <= 0m)
        {
            return Result.Failure(Error.Validation("Inv.SaleLimitsInvalid", "A minimum age is between 1 and 120, and a per-sale limit is positive."));
        }

        Item? item = await _db.Items.FirstOrDefaultAsync(x => x.Id == itemId && x.CompanyId == companyId, ct).ConfigureAwait(false);
        if (item is null)
        {
            return Result.Failure(Error.NotFound("Inv.ItemNotFound", $"Item {itemId} not found."));
        }

        item.MinimumAge = minimumAge;
        item.MaxQuantityPerSale = maxQuantityPerSale;
        item.RequiresPrescription = requiresPrescription;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    private async Task<string?> BarcodeOwnerAsync(long companyId, string barcode, CancellationToken ct)
        => await _db.Items.Where(i => i.CompanyId == companyId && i.Barcode == barcode).Select(i => i.Code).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            ?? await (from u in _db.ItemUnits join i in _db.Items on u.ItemId equals i.Id where u.CompanyId == companyId && u.Barcode == barcode select i.Code).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            ?? await (from a in _db.ItemAliases join i in _db.Items on a.ItemId equals i.Id where a.CompanyId == companyId && a.Kind == ItemAliasKind.Barcode && a.Alias == barcode select i.Code)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
}
