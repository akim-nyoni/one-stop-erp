using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Inventory;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Inventory;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Inventory;

/// <summary>Price-list maintenance and selling-price resolution (customer list first, default list second).</summary>
public sealed class PricingService : IPricingService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public PricingService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result<long>> CreatePriceListAsync(long companyId, string code, string name, CurrencyCode currency, bool isDefault, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation("Price.CodeAndName", "A price list needs a code and a name.");
        }

        string normalised = code.Trim().ToUpperInvariant();
        if (await _db.PriceLists.AnyAsync(p => p.CompanyId == companyId && p.Code == normalised, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Price.DuplicateCode", $"Price list '{normalised}' already exists.");
        }

        if (isDefault)
        {
            // Only one default per company.
            foreach (PriceList other in await _db.PriceLists.Where(p => p.CompanyId == companyId && p.IsDefault).ToListAsync(ct).ConfigureAwait(false))
            {
                other.IsDefault = false;
            }
        }

        PriceList list = new(companyId, normalised, name.Trim(), currency) { IsDefault = isDefault };
        _db.PriceLists.Add(list);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return list.Id;
    }

    /// <inheritdoc />
    public async Task<Result<int>> AdjustPricesAsync(long companyId, long priceListId, decimal percent, decimal roundTo, One4All.Application.Inventory.PriceRounding rounding, IReadOnlyCollection<long>? itemIds = null, CancellationToken ct = default)
    {
        if (percent <= -100m)
        {
            return Error.Validation("Price.Percent", "A price cannot be reduced by 100% or more.");
        }

        PriceList? list = await _db.PriceLists.Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.Id == priceListId && p.CompanyId == companyId, ct).ConfigureAwait(false);
        if (list is null)
        {
            return Error.NotFound("Price.ListNotFound", $"Price list {priceListId} not found.");
        }

        HashSet<long>? only = itemIds is null ? null : [.. itemIds];
        int changed = 0;
        foreach (PriceListLine line in list.Lines.ToList())
        {
            if (only is not null && !only.Contains(line.ItemId))
            {
                continue;
            }

            decimal adjusted = One4All.Application.Inventory.PriceAdjustment.Apply(line.UnitPrice, percent, roundTo, rounding);
            if (adjusted != line.UnitPrice)
            {
                list.SetPrice(line.ItemId, line.MinimumQuantity, adjusted);
                changed++;
            }
        }

        if (changed > 0)
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return changed;
    }

    /// <inheritdoc />
    public async Task<Result<long>> CopyPriceListAsync(long companyId, long sourcePriceListId, string code, string name, decimal percent, decimal roundTo, One4All.Application.Inventory.PriceRounding rounding, CancellationToken ct = default)
    {
        if (percent <= -100m)
        {
            return Error.Validation("Price.Percent", "A price cannot be reduced by 100% or more.");
        }

        PriceList? source = await _db.PriceLists.Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.Id == sourcePriceListId && p.CompanyId == companyId, ct).ConfigureAwait(false);
        if (source is null)
        {
            return Error.NotFound("Price.ListNotFound", $"Price list {sourcePriceListId} not found.");
        }

        Result<long> created = await CreatePriceListAsync(companyId, code, name, source.Currency, isDefault: false, ct).ConfigureAwait(false);
        if (created.IsFailure)
        {
            return created.Error;
        }

        PriceList copy = await _db.PriceLists.Include(p => p.Lines).FirstAsync(p => p.Id == created.Value, ct).ConfigureAwait(false);
        foreach (PriceListLine line in source.Lines)
        {
            copy.SetPrice(line.ItemId, line.MinimumQuantity, One4All.Application.Inventory.PriceAdjustment.Apply(line.UnitPrice, percent, roundTo, rounding));
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return copy.Id;
    }

    /// <inheritdoc />
    public async Task<Result> SetPriceAsync(long companyId, long priceListId, long itemId, decimal minimumQuantity, decimal unitPrice, CancellationToken ct = default)
    {
        if (minimumQuantity < 1m)
        {
            return Result.Failure(Error.Validation("Price.MinimumQuantity", "The minimum quantity must be at least 1."));
        }

        if (unitPrice < 0m)
        {
            return Result.Failure(Error.Validation("Price.Negative", "A unit price cannot be negative."));
        }

        PriceList? list = await _db.PriceLists.Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.Id == priceListId && p.CompanyId == companyId, ct).ConfigureAwait(false);
        if (list is null)
        {
            return Result.Failure(Error.NotFound("Price.ListNotFound", $"Price list {priceListId} not found."));
        }

        if (!await _db.Items.AnyAsync(i => i.Id == itemId && i.CompanyId == companyId, ct).ConfigureAwait(false))
        {
            return Result.Failure(Error.NotFound("Inv.ItemNotFound", $"Item {itemId} not found."));
        }

        list.SetPrice(itemId, minimumQuantity, unitPrice);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> AssignCustomerAsync(long companyId, long customerId, long? priceListId, CancellationToken ct = default)
    {
        Customer? customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == customerId && c.CompanyId == companyId, ct).ConfigureAwait(false);
        if (customer is null)
        {
            return Result.Failure(Error.NotFound("Ar.CustomerNotFound", $"Customer {customerId} not found."));
        }

        if (priceListId is { } id)
        {
            PriceList? list = await _db.PriceLists.FirstOrDefaultAsync(p => p.Id == id && p.CompanyId == companyId, ct).ConfigureAwait(false);
            if (list is null)
            {
                return Result.Failure(Error.NotFound("Price.ListNotFound", $"Price list {id} not found."));
            }

            if (list.Currency != customer.Currency)
            {
                return Result.Failure(Error.Rule("Price.CurrencyMismatch", $"Price list {list.Code} is in {list.Currency} but customer {customer.Code} trades in {customer.Currency}."));
            }
        }

        customer.PriceListId = priceListId;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<ResolvedPrice>> PriceAsync(long companyId, long? customerId, long itemId, decimal quantity, CancellationToken ct = default)
    {
        if (quantity <= 0m)
        {
            return Error.Validation("Price.Quantity", "The quantity to price must be positive.");
        }

        long? customerListId = null;
        if (customerId is { } cid)
        {
            Customer? customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == cid && c.CompanyId == companyId, ct).ConfigureAwait(false);
            if (customer is null)
            {
                return Error.NotFound("Ar.CustomerNotFound", $"Customer {cid} not found.");
            }

            customerListId = customer.PriceListId;
        }

        // The customer's own list first.
        if (customerListId is { } own)
        {
            PriceList? mine = await LoadActiveListAsync(companyId, p => p.Id == own, itemId, ct).ConfigureAwait(false);
            PriceListLine? line = mine?.Resolve(itemId, quantity);
            if (mine is not null && line is not null)
            {
                return new ResolvedPrice(itemId, quantity, line.UnitPrice, mine.Currency, mine.Id, mine.Code, line.MinimumQuantity, FromCustomerList: true);
            }
        }

        // Then the company default.
        PriceList? fallback = await LoadActiveListAsync(companyId, p => p.IsDefault, itemId, ct).ConfigureAwait(false);
        PriceListLine? resolved = fallback?.Resolve(itemId, quantity);
        if (fallback is null || resolved is null)
        {
            return Error.NotFound("Price.NoPrice", $"No selling price is set for item {itemId} at quantity {quantity}.");
        }

        return new ResolvedPrice(itemId, quantity, resolved.UnitPrice, fallback.Currency, fallback.Id, fallback.Code, resolved.MinimumQuantity, FromCustomerList: false);
    }

    // Loads one active list matching the predicate with only the item's lines attached.
    private async Task<PriceList?> LoadActiveListAsync(long companyId, System.Linq.Expressions.Expression<Func<PriceList, bool>> which, long itemId, CancellationToken ct) =>
        await _db.PriceLists
            .Include(p => p.Lines.Where(l => l.ItemId == itemId))
            .Where(p => p.CompanyId == companyId && p.IsActive)
            .Where(which)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
}
