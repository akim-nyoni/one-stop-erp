using One4All.Core.Common;

namespace One4All.Application.Abstractions.Inventory;

/// <summary>A transfer to dispatch.</summary>
public sealed class DispatchTransferRequest
{
    /// <summary>Gets the company.</summary>
    public long CompanyId { get; init; }

    /// <summary>Gets the item.</summary>
    public long ItemId { get; init; }

    /// <summary>Gets the source warehouse.</summary>
    public long FromWarehouseId { get; init; }

    /// <summary>Gets the destination warehouse.</summary>
    public long ToWarehouseId { get; init; }

    /// <summary>Gets the transit location (a warehouse flagged as transit).</summary>
    public long TransitWarehouseId { get; init; }

    /// <summary>Gets the dispatch date.</summary>
    public DateOnly Date { get; init; }

    /// <summary>Gets the quantity.</summary>
    public decimal Quantity { get; init; }
}

/// <summary>The dispatched transfer.</summary>
/// <param name="TransferId">The transfer.</param>
/// <param name="TransferNumber">Its note number.</param>
/// <param name="Value">The cost value in transit.</param>
public readonly record struct DispatchTransferResult(long TransferId, string TransferNumber, decimal Value);

/// <summary>Two-step inter-warehouse transfers through a transit location (brief §14 E 91).</summary>
public interface IStockTransferService
{
    /// <summary>Dispatches stock from the source into the transit location under a new TRF-numbered note.</summary>
    /// <param name="request">The dispatch.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The transfer, or an error.</returns>
    Task<Result<DispatchTransferResult>> DispatchAsync(DispatchTransferRequest request, CancellationToken ct = default);

    /// <summary>
    /// Receives a transfer at its destination: what arrived moves from transit to the destination; a shortfall is written off the
    /// transit location to the loss account (reason code TRANSIT when it exists). More than was dispatched cannot arrive.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="transferId">The transfer.</param>
    /// <param name="date">The receipt date.</param>
    /// <param name="receivedQuantity">The quantity that arrived.</param>
    /// <param name="lossAccountCode">The account a shortfall is written off to (required when short).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The value written off, or an error.</returns>
    Task<Result<decimal>> ReceiveAsync(long companyId, long transferId, DateOnly date, decimal receivedQuantity, string? lossAccountCode = null, CancellationToken ct = default);
}
