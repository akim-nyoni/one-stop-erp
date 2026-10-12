using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Common;
using One4All.Core.Inventory;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Inventory;

/// <summary>Keeps the goods held for suppliers and customers outside our stock, and buys supplier-owned goods when they are used.</summary>
public sealed class HeldStockService : IHeldStockService
{
    private readonly One4AllDbContext _db;
    private readonly IInventoryService _inventory;
    private readonly IPurchaseLedgerService _ap;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="inventory">Takes used goods into our stock.</param>
    /// <param name="ap">Raises the supplier's invoice for used goods.</param>
    public HeldStockService(One4AllDbContext db, IInventoryService inventory, IPurchaseLedgerService ap)
    {
        _db = db;
        _inventory = inventory;
        _ap = ap;
    }

    /// <inheritdoc />
    public async Task<Result<long>> TakeInAsync(long companyId, HeldStockIn request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Quantity <= 0m || request.UnitPrice is < 0m)
        {
            return Error.Validation("Inv.HeldQuantity", "Goods taken in need a positive quantity (and a price that is not negative).");
        }

        string ownerCode = (request.OwnerCode ?? string.Empty).Trim().ToUpperInvariant();
        long? ownerId = request.Owner == HeldStockOwner.Supplier
            ? await _db.Suppliers.Where(s => s.CompanyId == companyId && s.Code == ownerCode).Select(s => (long?)s.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            : await _db.Customers.Where(c => c.CompanyId == companyId && c.Code == ownerCode).Select(c => (long?)c.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (ownerId is null)
        {
            return Error.NotFound("Inv.HeldOwner", $"{request.Owner} {ownerCode} not found.");
        }

        string itemCode = (request.ItemCode ?? string.Empty).Trim().ToUpperInvariant();
        string warehouseCode = (request.WarehouseCode ?? string.Empty).Trim().ToUpperInvariant();
        long? itemId = await _db.Items.Where(i => i.CompanyId == companyId && i.Code == itemCode).Select(i => (long?)i.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        long? warehouseId = await _db.Warehouses.Where(w => w.CompanyId == companyId && w.Code == warehouseCode).Select(w => (long?)w.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (itemId is null || warehouseId is null)
        {
            return Error.NotFound("Inv.HeldItem", $"Item {itemCode} or location {warehouseCode} not found.");
        }

        HeldStock? held = await _db.Set<HeldStock>().FirstOrDefaultAsync(h => h.CompanyId == companyId && h.Owner == request.Owner && h.OwnerId == ownerId
            && h.ItemId == itemId && h.WarehouseId == warehouseId, ct).ConfigureAwait(false);
        if (held is null)
        {
            if (request.Owner == HeldStockOwner.Supplier && request.UnitPrice is not > 0m)
            {
                return Error.Validation("Inv.HeldPrice", "Supplier-owned goods need the agreed price they are billed at when used.");
            }

            held = new HeldStock(companyId, request.Owner, ownerId.Value, itemId.Value, warehouseId.Value);
            _db.Set<HeldStock>().Add(held);
        }

        if (request.Owner == HeldStockOwner.Supplier && request.UnitPrice is decimal price and > 0m)
        {
            held.UnitPrice = price;
        }

        held.Move(HeldStockMovementKind.In, request.Quantity, request.Date, Clean(request.Reference));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return held.Id;
    }

    /// <inheritdoc />
    public async Task<Result> ReturnAsync(long companyId, long heldStockId, decimal quantity, DateOnly date, string? reference, CancellationToken ct = default)
    {
        Result<HeldStock> held = await TakeFromAsync(companyId, heldStockId, quantity, ct).ConfigureAwait(false);
        if (held.IsFailure)
        {
            return Result.Failure(held.Error);
        }

        held.Value.Move(HeldStockMovementKind.Returned, quantity, date, Clean(reference));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public Task<Result<string>> UseAsync(long companyId, long heldStockId, decimal quantity, DateOnly date, string reference, string clearingAccountCode, CancellationToken ct = default)
        => _db.AtomicAsync(() => UseCoreAsync(companyId, heldStockId, quantity, date, reference, clearingAccountCode, ct), ct);

    // One unit: the goods come into our stock and the supplier is billed for them, or neither.
    private async Task<Result<string>> UseCoreAsync(long companyId, long heldStockId, decimal quantity, DateOnly date, string reference, string clearingAccountCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reference) || string.IsNullOrWhiteSpace(clearingAccountCode))
        {
            return Error.Validation("Inv.HeldUse", "Using consignment goods needs a reference and the consignment clearing account.");
        }

        Result<HeldStock> found = await TakeFromAsync(companyId, heldStockId, quantity, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        HeldStock held = found.Value;
        if (held.Owner != HeldStockOwner.Supplier)
        {
            return Error.Rule("Inv.CustomerOwned", "Customer-owned goods are never ours to use; hand them back instead.");
        }

        string clearing = clearingAccountCode.Trim().ToUpperInvariant();
        Result<StockMovementResult> received = await _inventory.ReceiveAsync(new ReceiveStockRequest
        {
            CompanyId = companyId, ItemId = held.ItemId, WarehouseId = held.WarehouseId, Date = date, Quantity = quantity, UnitCost = held.UnitPrice, CreditAccountCode = clearing,
            Reference = reference.Trim(),
        }, ct).ConfigureAwait(false);
        if (received.IsFailure)
        {
            return received.Error;
        }

        var supplier = await _db.Suppliers.Where(s => s.Id == held.OwnerId).Select(s => new { s.Id, s.Currency }).FirstAsync(ct).ConfigureAwait(false);
        string itemCode = await _db.Items.Where(i => i.Id == held.ItemId).Select(i => i.Code).FirstAsync(ct).ConfigureAwait(false);
        Result<TradingPostingResult> invoiced = await _ap.PostInvoiceAsync(new PurchaseInvoiceRequest
        {
            CompanyId = companyId, SupplierId = supplier.Id, DocumentNumber = reference.Trim(), DocumentDate = date, Currency = supplier.Currency,
            NetAmount = decimal.Round(quantity * held.UnitPrice, 2, MidpointRounding.AwayFromZero), ExpenseAccountCode = clearing,
            Narrative = $"Consignment stock used: {quantity:0.####} x {itemCode}",
        }, ct).ConfigureAwait(false);
        if (invoiced.IsFailure)
        {
            return invoiced.Error;
        }

        held.Move(HeldStockMovementKind.Used, quantity, date, reference.Trim(), invoiced.Value.JournalNumber);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return invoiced.Value.JournalNumber;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<HeldStockRow>> HoldingsAsync(long companyId, HeldStockOwner? owner, CancellationToken ct = default)
    {
        var held = await _db.Set<HeldStock>().AsNoTracking().Where(h => h.CompanyId == companyId && h.Quantity > 0m && (owner == null || h.Owner == owner))
            .Select(h => new
            {
                h.Id, h.Owner, h.Quantity, h.UnitPrice,
                Item = _db.Items.Where(i => i.Id == h.ItemId).Select(i => i.Code).First(),
                Warehouse = _db.Warehouses.Where(w => w.Id == h.WarehouseId).Select(w => w.Code).First(),
                Supplier = _db.Suppliers.Where(s => s.Id == h.OwnerId).Select(s => new { s.Code, s.Name }).FirstOrDefault(),
                Customer = _db.Customers.Where(c => c.Id == h.OwnerId).Select(c => new { c.Code, c.Name }).FirstOrDefault(),
            })
            .ToListAsync(ct).ConfigureAwait(false);
        return [.. held.Select(h =>
            {
                (string code, string name) = h.Owner == HeldStockOwner.Supplier ? (h.Supplier?.Code ?? "?", h.Supplier?.Name ?? string.Empty) : (h.Customer?.Code ?? "?", h.Customer?.Name ?? string.Empty);
                return new HeldStockRow(h.Id, h.Owner, code, name, h.Item, h.Warehouse, h.Quantity, h.UnitPrice);
            })
            .OrderBy(r => r.Owner).ThenBy(r => r.OwnerCode, StringComparer.Ordinal).ThenBy(r => r.ItemCode, StringComparer.Ordinal)];
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task<Result<HeldStock>> TakeFromAsync(long companyId, long heldStockId, decimal quantity, CancellationToken ct)
    {
        HeldStock? held = await _db.Set<HeldStock>().FirstOrDefaultAsync(h => h.CompanyId == companyId && h.Id == heldStockId, ct).ConfigureAwait(false);
        if (held is null)
        {
            return Error.NotFound("Inv.HeldNotFound", $"Held stock {heldStockId} not found.");
        }

        if (quantity <= 0m || quantity > held.Quantity)
        {
            return Error.Rule("Inv.HeldShort", $"Only {held.Quantity:0.####} is held.");
        }

        return held;
    }
}
