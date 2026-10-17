using One4All.Core.Common;
using One4All.Core.Inventory;

namespace One4All.Application.Abstractions.Inventory;

/// <summary>
/// Goods held for suppliers (consignment stock, not ours until used) and for customers (in our keeping, never ours): taken in, handed
/// back and - supplier-owned only - used, which buys them from the supplier at the agreed price and takes them into our own stock
/// (brief §4.6 consignment, supplier-owned and customer-owned stock).
/// </summary>
public interface IHeldStockService
{
    /// <summary>Takes goods in for their owner (no posting: they are not ours).</summary>
    /// <returns>The holding's id.</returns>
    Task<Result<long>> TakeInAsync(long companyId, HeldStockIn request, CancellationToken ct = default);

    /// <summary>Hands goods back to their owner.</summary>
    Task<Result> ReturnAsync(long companyId, long heldStockId, decimal quantity, DateOnly date, string? reference, CancellationToken ct = default);

    /// <summary>
    /// Uses supplier-owned goods: they come into our stock at the agreed price (Dr stock, Cr the consignment clearing account) and the supplier's
    /// invoice for them is raised (Dr clearing, Cr the supplier), in one unit.
    /// </summary>
    /// <returns>The supplier invoice's journal number.</returns>
    Task<Result<string>> UseAsync(long companyId, long heldStockId, decimal quantity, DateOnly date, string reference, string clearingAccountCode, CancellationToken ct = default);

    /// <summary>What is held now, optionally for one kind of owner.</summary>
    Task<IReadOnlyList<HeldStockRow>> HoldingsAsync(long companyId, HeldStockOwner? owner, CancellationToken ct = default);
}

/// <summary>Goods to take in for their owner.</summary>
/// <param name="Owner">Supplier or customer.</param>
/// <param name="OwnerCode">The supplier's or customer's code.</param>
/// <param name="ItemCode">The item.</param>
/// <param name="WarehouseCode">Where they are held.</param>
/// <param name="Quantity">How many.</param>
/// <param name="Date">When.</param>
/// <param name="Reference">The delivery or job reference.</param>
public sealed record HeldStockIn(HeldStockOwner Owner, string OwnerCode, string ItemCode, string WarehouseCode, decimal Quantity, DateOnly Date, string? Reference)
{
    /// <summary>The agreed price per unit for supplier-owned goods (kept as the latest when more comes in).</summary>
    public decimal? UnitPrice { get; init; }
}

/// <summary>A holding as listed.</summary>
/// <param name="Id">The holding's id.</param>
/// <param name="Owner">Supplier or customer.</param>
/// <param name="OwnerCode">The owner's code.</param>
/// <param name="OwnerName">The owner's name.</param>
/// <param name="ItemCode">The item.</param>
/// <param name="WarehouseCode">Where it is held.</param>
/// <param name="Quantity">The quantity held.</param>
/// <param name="UnitPrice">The agreed price (supplier-owned).</param>
public sealed record HeldStockRow(long Id, HeldStockOwner Owner, string OwnerCode, string OwnerName, string ItemCode, string WarehouseCode, decimal Quantity, decimal UnitPrice);
