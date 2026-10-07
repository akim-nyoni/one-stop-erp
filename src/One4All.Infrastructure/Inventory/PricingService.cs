using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Inventory;
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
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        foreach (PriceListLine line in list.InForce(today).ToList())
        {
            if (only is not null && !only.Contains(line.ItemId))
            {
                continue;
            }

            decimal adjusted = One4All.Application.Inventory.PriceAdjustment.Apply(line.UnitPrice, percent, roundTo, rounding);
            if (adjusted != line.UnitPrice)
            {
                // The price in force today changes: a standing price in place, a dated one by a new price from today (its history kept).
                list.SetPrice(line.ItemId, line.MinimumQuantity, adjusted, line.EffectiveFrom is null ? null : today);
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
            copy.SetPrice(line.ItemId, line.MinimumQuantity, One4All.Application.Inventory.PriceAdjustment.Apply(line.UnitPrice, percent, roundTo, rounding), line.EffectiveFrom);
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

        // Once a dated price is in force, an immediate change is a new price from today (earlier prices kept as history).
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        bool datedInForce = list.Lines.Any(l => l.ItemId == itemId && l.MinimumQuantity == minimumQuantity && l.EffectiveFrom is DateOnly from && from <= today);
        list.SetPrice(itemId, minimumQuantity, unitPrice, datedInForce ? today : null);
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
    public Task<Result<ResolvedPrice>> PriceAsync(long companyId, long? customerId, long itemId, decimal quantity, CancellationToken ct = default)
        => PriceAsync(companyId, customerId, itemId, quantity, DateOnly.FromDateTime(DateTime.Today), ct);

    /// <inheritdoc />
    public async Task<Result<ResolvedPrice>> PriceAsync(long companyId, long? customerId, long itemId, decimal quantity, DateOnly asAt, CancellationToken ct = default)
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

        // The customer's own list first: its price, else its cost-plus rule.
        if (customerListId is { } own)
        {
            PriceList? mine = await LoadActiveListAsync(companyId, p => p.Id == own, itemId, ct).ConfigureAwait(false);
            PriceListLine? line = mine?.Resolve(itemId, quantity, asAt);
            if (mine is not null && line is not null)
            {
                return new ResolvedPrice(itemId, quantity, line.UnitPrice, mine.Currency, mine.Id, mine.Code, line.MinimumQuantity, FromCustomerList: true) { EffectiveFrom = line.EffectiveFrom };
            }

            if (mine is { CostPlusBasis: not CostPlusBasis.None } && await FromCostAsync(companyId, mine, itemId, ct).ConfigureAwait(false) is decimal costPlus)
            {
                return new ResolvedPrice(itemId, quantity, costPlus, mine.Currency, mine.Id, mine.Code, 1m, FromCustomerList: true, FromCost: true);
            }
        }

        // Then the company default.
        PriceList? fallback = await LoadActiveListAsync(companyId, p => p.IsDefault, itemId, ct).ConfigureAwait(false);
        PriceListLine? resolved = fallback?.Resolve(itemId, quantity, asAt);
        if (fallback is not null && resolved is null && fallback.CostPlusBasis != CostPlusBasis.None
            && await FromCostAsync(companyId, fallback, itemId, ct).ConfigureAwait(false) is decimal defaultCostPlus)
        {
            return new ResolvedPrice(itemId, quantity, defaultCostPlus, fallback.Currency, fallback.Id, fallback.Code, 1m, FromCustomerList: false, FromCost: true);
        }

        if (fallback is null || resolved is null)
        {
            return Error.NotFound("Price.NoPrice", $"No selling price is set for item {itemId} at quantity {quantity}.");
        }

        return new ResolvedPrice(itemId, quantity, resolved.UnitPrice, fallback.Currency, fallback.Id, fallback.Code, resolved.MinimumQuantity, FromCustomerList: false)
        {
            EffectiveFrom = resolved.EffectiveFrom,
        };
    }

    /// <inheritdoc />
    public async Task<Result> SchedulePriceAsync(long companyId, long priceListId, long itemId, decimal minimumQuantity, decimal unitPrice, DateOnly effectiveFrom, DateOnly today,
        CancellationToken ct = default)
    {
        if (minimumQuantity < 1m || unitPrice < 0m)
        {
            return Result.Failure(Error.Validation("Price.Line", "A price needs a minimum quantity of at least 1 and a unit price of nil or more."));
        }

        if (effectiveFrom <= today)
        {
            return Result.Failure(Error.Rule("Price.EffectiveInPast", $"A scheduled price takes effect after today ({today:yyyy-MM-dd}); change today's price directly."));
        }

        PriceList? list = await _db.PriceLists.Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == priceListId && p.CompanyId == companyId, ct).ConfigureAwait(false);
        if (list is null)
        {
            return Result.Failure(Error.NotFound("Price.ListNotFound", $"Price list {priceListId} not found."));
        }

        if (!await _db.Items.AnyAsync(i => i.Id == itemId && i.CompanyId == companyId, ct).ConfigureAwait(false))
        {
            return Result.Failure(Error.NotFound("Inv.ItemNotFound", $"Item {itemId} not found."));
        }

        list.SetPrice(itemId, minimumQuantity, unitPrice, effectiveFrom);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<int>> ScheduleAdjustmentAsync(long companyId, long priceListId, decimal percent, decimal roundTo, One4All.Application.Inventory.PriceRounding rounding,
        DateOnly effectiveFrom, DateOnly today, CancellationToken ct = default)
    {
        if (percent <= -100m)
        {
            return Error.Validation("Price.Percent", "A price cannot be reduced by 100% or more.");
        }

        if (effectiveFrom <= today)
        {
            return Error.Rule("Price.EffectiveInPast", $"A scheduled change takes effect after today ({today:yyyy-MM-dd}); adjust today's prices directly.");
        }

        PriceList? list = await _db.PriceLists.Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == priceListId && p.CompanyId == companyId, ct).ConfigureAwait(false);
        if (list is null)
        {
            return Error.NotFound("Price.ListNotFound", $"Price list {priceListId} not found.");
        }

        // Each price in force the day before becomes the base for the new one from that day.
        int scheduled = 0;
        foreach (PriceListLine line in list.InForce(effectiveFrom.AddDays(-1)).ToList())
        {
            list.SetPrice(line.ItemId, line.MinimumQuantity, One4All.Application.Inventory.PriceAdjustment.Apply(line.UnitPrice, percent, roundTo, rounding), effectiveFrom);
            scheduled++;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return scheduled;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<(decimal MinimumQuantity, decimal UnitPrice, DateOnly? EffectiveFrom)>> PriceHistoryAsync(long companyId, long priceListId, long itemId, CancellationToken ct = default)
        => [.. (await _db.PriceLists.AsNoTracking().Where(p => p.Id == priceListId && p.CompanyId == companyId).SelectMany(p => p.Lines).Where(l => l.ItemId == itemId)
                .ToListAsync(ct).ConfigureAwait(false))
            .OrderBy(l => l.MinimumQuantity).ThenBy(l => l.EffectiveFrom ?? DateOnly.MinValue).Select(l => (l.MinimumQuantity, l.UnitPrice, l.EffectiveFrom))];

    /// <inheritdoc />
    public async Task<Result> SetCostPlusAsync(long companyId, long priceListId, CostPlusBasis basis, decimal percent, decimal roundTo, PriceRounding rounding, CancellationToken ct = default)
    {
        if (basis != CostPlusBasis.None && (percent < 0m || (basis == CostPlusBasis.Margin && percent >= 100m) || roundTo < 0m))
        {
            return Result.Failure(Error.Validation("Price.CostPlusInvalid", "A markup cannot be negative, a margin must be under 100%, and the price point cannot be negative."));
        }

        PriceList? list = await _db.PriceLists.FirstOrDefaultAsync(p => p.Id == priceListId && p.CompanyId == companyId, ct).ConfigureAwait(false);
        if (list is null)
        {
            return Result.Failure(Error.NotFound("Price.ListNotFound", $"Price list {priceListId} not found."));
        }

        CurrencyCode baseCurrency = await _db.Companies.Where(c => c.Id == companyId).Select(c => c.BaseCurrency).SingleAsync(ct).ConfigureAwait(false);
        if (basis != CostPlusBasis.None && list.Currency != baseCurrency)
        {
            return Result.Failure(Error.Rule("Price.CostPlusCurrency", $"Cost is held in {baseCurrency}; list {list.Code} is in {list.Currency}."));
        }

        list.CostPlusBasis = basis;
        list.CostPlusPercent = basis == CostPlusBasis.None ? 0m : percent;
        list.CostPlusRoundTo = roundTo;
        list.CostPlusRounding = (int)rounding;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    // The cost-plus price of an item on a list, or null when the item has no cost to price from.
    private async Task<decimal?> FromCostAsync(long companyId, PriceList list, long itemId, CancellationToken ct)
    {
        var stock = await _db.StockBalances.AsNoTracking().Where(b => b.CompanyId == companyId && b.ItemId == itemId && b.QuantityOnHand > 0m)
            .GroupBy(b => b.ItemId).Select(g => new { Quantity = g.Sum(b => b.QuantityOnHand), Value = g.Sum(b => b.TotalValue) }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        decimal? cost = stock is { Quantity: > 0m } ? stock.Value / stock.Quantity
            : await _db.Items.AsNoTracking().Where(i => i.Id == itemId && i.CompanyId == companyId).Select(i => i.StandardCost).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return cost is > 0m ? PriceAdjustment.FromCost(cost.Value, list.CostPlusBasis, list.CostPlusPercent, list.CostPlusRoundTo, (PriceRounding)list.CostPlusRounding) : null;
    }

    // Loads one active list matching the predicate with only the item's lines attached.
    private async Task<PriceList?> LoadActiveListAsync(long companyId, System.Linq.Expressions.Expression<Func<PriceList, bool>> which, long itemId, CancellationToken ct) =>
        await _db.PriceLists
            .Include(p => p.Lines.Where(l => l.ItemId == itemId))
            .Where(p => p.CompanyId == companyId && p.IsActive)
            .Where(which)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
}
