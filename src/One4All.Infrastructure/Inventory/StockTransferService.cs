using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Numbering;
using One4All.Core.Common;
using One4All.Core.Inventory;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Inventory;

/// <summary>Dispatches transfers into a transit location and receives them at the destination.</summary>
public sealed class StockTransferService : IStockTransferService
{
    private readonly One4AllDbContext _db;
    private readonly IInventoryService _inventory;
    private readonly INumberSequenceService _numbers;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="inventory">Moves and adjusts the stock.</param>
    /// <param name="numbers">Numbers the transfer notes.</param>
    public StockTransferService(One4AllDbContext db, IInventoryService inventory, INumberSequenceService numbers)
    {
        _db = db;
        _inventory = inventory;
        _numbers = numbers;
    }

    /// <inheritdoc />
    public Task<Result<DispatchTransferResult>> DispatchAsync(DispatchTransferRequest request, CancellationToken ct = default)
        => _db.AtomicAsync(() => DispatchCoreAsync(request, ct), ct);

    // One unit of work: the stock move and the transfer note commit together.
    private async Task<Result<DispatchTransferResult>> DispatchCoreAsync(DispatchTransferRequest request, CancellationToken ct)
    {
        if (request.FromWarehouseId == request.ToWarehouseId)
        {
            return Error.Rule("Inv.SameWarehouse", "A transfer needs different source and destination warehouses.");
        }

        Warehouse? transit = await _db.Warehouses.AsNoTracking().FirstOrDefaultAsync(w => w.Id == request.TransitWarehouseId && w.CompanyId == request.CompanyId, ct).ConfigureAwait(false);
        if (transit is not { IsTransit: true })
        {
            return Error.Validation("Inv.NotTransit", "The transit location must be a warehouse flagged as transit.");
        }

        if (request.FromWarehouseId == transit.Id || request.ToWarehouseId == transit.Id)
        {
            return Error.Rule("Inv.TransitEndpoint", "The transit location cannot be the source or destination.");
        }

        Result<string> number = await _numbers.AllocateAsync(request.CompanyId, "TRF", request.Date, ct: ct).ConfigureAwait(false);
        if (number.IsFailure)
        {
            return number.Error;
        }

        Result<decimal> moved = await _inventory.TransferAsync(request.CompanyId, request.ItemId, request.FromWarehouseId, transit.Id, request.Date, request.Quantity, number.Value, ct).ConfigureAwait(false);
        if (moved.IsFailure)
        {
            return moved.Error;
        }

        StockTransfer transfer = new(request.CompanyId, number.Value, request.ItemId, request.FromWarehouseId, transit.Id, request.ToWarehouseId, request.Date, request.Quantity, moved.Value);
        _db.StockTransfers.Add(transfer);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new DispatchTransferResult(transfer.Id, transfer.TransferNumber, moved.Value);
    }

    /// <inheritdoc />
    public Task<Result<decimal>> ReceiveAsync(long companyId, long transferId, DateOnly date, decimal receivedQuantity, string? lossAccountCode = null, CancellationToken ct = default)
        => _db.AtomicAsync(() => ReceiveCoreAsync(companyId, transferId, date, receivedQuantity, lossAccountCode, ct), ct);

    // One unit of work: the onward move, the shortfall write-off and the note's status commit together.
    private async Task<Result<decimal>> ReceiveCoreAsync(long companyId, long transferId, DateOnly date, decimal receivedQuantity, string? lossAccountCode, CancellationToken ct)
    {
        StockTransfer? transfer = await _db.StockTransfers.FirstOrDefaultAsync(t => t.Id == transferId && t.CompanyId == companyId, ct).ConfigureAwait(false);
        if (transfer is null)
        {
            return Error.NotFound("Inv.TransferNotFound", $"Transfer {transferId} not found.");
        }

        if (transfer.Status != StockTransferStatus.InTransit)
        {
            return Error.Rule("Inv.TransferReceived", $"Transfer {transfer.TransferNumber} has already been received.");
        }

        if (receivedQuantity < 0m || receivedQuantity > transfer.Quantity)
        {
            return Error.Validation("Inv.ReceivedQuantity", $"Between 0 and the {transfer.Quantity:0.###} dispatched can arrive.");
        }

        if (date < transfer.DispatchedOn)
        {
            return Error.Validation("Inv.ReceivedBeforeDispatch", "A transfer cannot arrive before it was dispatched.");
        }

        decimal shortfall = transfer.Quantity - receivedQuantity;
        if (shortfall > 0m && string.IsNullOrWhiteSpace(lossAccountCode))
        {
            return Error.Validation("Inv.LossAccountRequired", "A short receipt needs the account the shortfall is written off to.");
        }

        if (receivedQuantity > 0m)
        {
            Result<decimal> moved = await _inventory.TransferAsync(companyId, transfer.ItemId, transfer.TransitWarehouseId, transfer.ToWarehouseId, date, receivedQuantity, transfer.TransferNumber, ct)
                .ConfigureAwait(false);
            if (moved.IsFailure)
            {
                return moved.Error;
            }
        }

        decimal writtenOff = 0m;
        if (shortfall > 0m)
        {
            decimal left = await _db.StockBalances.AsNoTracking().Where(b => b.CompanyId == companyId && b.ItemId == transfer.ItemId && b.WarehouseId == transfer.TransitWarehouseId)
                .Select(b => b.QuantityOnHand).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            string? reason = await _db.StockReasonCodes.AnyAsync(r => r.CompanyId == companyId && r.Code == "TRANSIT" && r.IsActive, ct).ConfigureAwait(false) ? "TRANSIT" : null;
            Result<decimal> adjusted = await _inventory.AdjustAsync(companyId, transfer.ItemId, transfer.TransitWarehouseId, date, Math.Max(0m, left - shortfall), lossAccountCode!, transfer.TransferNumber, reason, ct)
                .ConfigureAwait(false);
            if (adjusted.IsFailure)
            {
                return adjusted.Error;
            }

            writtenOff = -adjusted.Value;
        }

        transfer.Receive(date, receivedQuantity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return writtenOff;
    }
}
