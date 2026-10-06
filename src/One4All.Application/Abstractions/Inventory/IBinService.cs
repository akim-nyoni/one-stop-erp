using One4All.Core.Common;

namespace One4All.Application.Abstractions.Inventory;

/// <summary>What a bin holds.</summary>
/// <param name="BinCode">The bin.</param>
/// <param name="PickSequence">Its place on the pick walk.</param>
/// <param name="ItemCode">The item.</param>
/// <param name="Quantity">The quantity.</param>
public sealed record BinContent(string BinCode, int PickSequence, string ItemCode, decimal Quantity);

/// <summary>One stop on a pick walk.</summary>
/// <param name="BinCode">The bin (UNBINNED for stock not yet put away).</param>
/// <param name="PickSequence">The order to visit it in.</param>
/// <param name="ItemId">The item.</param>
/// <param name="Quantity">How many to take there.</param>
public sealed record PickStop(string BinCode, int PickSequence, long ItemId, decimal Quantity);

/// <summary>
/// Bin locations (brief §4.2): bins with a pick sequence and capacity; stock put away into bins (never more than the warehouse holds
/// unbinned, never past capacity), moved between them and taken out when picked; a pick route visiting bins in walk order; and reorder
/// levels set per warehouse.
/// </summary>
public interface IBinService
{
    /// <summary>Adds a bin (<c>Inv.BinExists</c> for a code in use in the warehouse).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="warehouseId">The warehouse.</param>
    /// <param name="code">The bin code.</param>
    /// <param name="pickSequence">Its place on the pick walk.</param>
    /// <param name="capacityUnits">The most units it holds.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The bin id.</returns>
    Task<Result<long>> AddBinAsync(long companyId, long warehouseId, string code, int pickSequence, decimal? capacityUnits, CancellationToken ct = default);

    /// <summary>Puts stock away into a bin (<c>Inv.NotEnoughUnbinned</c>, <c>Inv.BinFull</c>).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="warehouseId">The warehouse.</param>
    /// <param name="binCode">The bin.</param>
    /// <param name="itemId">The item.</param>
    /// <param name="quantity">The quantity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    Task<Result> PutAwayAsync(long companyId, long warehouseId, string binCode, long itemId, decimal quantity, CancellationToken ct = default);

    /// <summary>Moves stock between bins (<c>Inv.NotInBin</c>, <c>Inv.BinFull</c>).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="warehouseId">The warehouse.</param>
    /// <param name="fromBin">From.</param>
    /// <param name="toBin">To.</param>
    /// <param name="itemId">The item.</param>
    /// <param name="quantity">The quantity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    Task<Result> MoveAsync(long companyId, long warehouseId, string fromBin, string toBin, long itemId, decimal quantity, CancellationToken ct = default);

    /// <summary>Takes picked stock out of a bin (<c>Inv.NotInBin</c>).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="warehouseId">The warehouse.</param>
    /// <param name="binCode">The bin.</param>
    /// <param name="itemId">The item.</param>
    /// <param name="quantity">The quantity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    Task<Result> TakeAsync(long companyId, long warehouseId, string binCode, long itemId, decimal quantity, CancellationToken ct = default);

    /// <summary>A pick walk for the quantities wanted: bins in pick sequence, what is not binned last.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="warehouseId">The warehouse.</param>
    /// <param name="wanted">Item and quantity pairs.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The stops, in walk order.</returns>
    Task<IReadOnlyList<PickStop>> PickRouteAsync(long companyId, long warehouseId, IReadOnlyList<(long ItemId, decimal Quantity)> wanted, CancellationToken ct = default);

    /// <summary>What every bin in a warehouse holds, in walk order.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="warehouseId">The warehouse.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The contents.</returns>
    Task<IReadOnlyList<BinContent>> ContentsAsync(long companyId, long warehouseId, CancellationToken ct = default);

    /// <summary>Sets an item's reorder level and quantity at one warehouse (used by replenishment for that warehouse).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="itemId">The item.</param>
    /// <param name="warehouseId">The warehouse.</param>
    /// <param name="reorderLevel">The reorder level.</param>
    /// <param name="reorderQuantity">The reorder quantity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    Task<Result> SetWarehouseLevelAsync(long companyId, long itemId, long warehouseId, decimal reorderLevel, decimal reorderQuantity, CancellationToken ct = default);
}
