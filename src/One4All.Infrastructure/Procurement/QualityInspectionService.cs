using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Procurement;
using One4All.Core.Common;
using One4All.Core.Inventory;
using One4All.Core.Procurement;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Procurement;

/// <summary>Releases or returns goods held in quarantine, through the inventory and procurement services.</summary>
public sealed class QualityInspectionService : IQualityInspectionService
{
    private readonly One4AllDbContext _db;
    private readonly IInventoryService _inventory;
    private readonly IProcurementService _procurement;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="inventory">The inventory service, which moves passed goods.</param>
    /// <param name="procurement">The procurement service, which returns rejected goods.</param>
    public QualityInspectionService(One4AllDbContext db, IInventoryService inventory, IProcurementService procurement)
    {
        _db = db;
        _inventory = inventory;
        _procurement = procurement;
    }

    /// <inheritdoc />
    public Task<Result<InspectionOutcome>> InspectAsync(long companyId, long goodsReceivedNoteId, InspectionRequest request, CancellationToken ct = default)
        => _db.AtomicAsync(() => InspectCoreAsync(companyId, goodsReceivedNoteId, request, ct), ct);

    // One unit: releases, returns and the inspection record stand or fall together.
    private async Task<Result<InspectionOutcome>> InspectCoreAsync(long companyId, long goodsReceivedNoteId, InspectionRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        GoodsReceivedNote? grn = await _db.Set<GoodsReceivedNote>().Include(g => g.Lines)
            .FirstOrDefaultAsync(g => g.Id == goodsReceivedNoteId && g.CompanyId == companyId, ct).ConfigureAwait(false);
        if (grn is null)
        {
            return Error.NotFound("Qc.GrnNotFound", $"Goods received note {goodsReceivedNoteId} not found.");
        }

        if (request.Results.Count == 0 || request.Results.Any(r => r.Passed < 0m || r.Rejected < 0m || r.Passed + r.Rejected <= 0m)
            || request.Results.Any(r => r.Rejected > 0m && string.IsNullOrWhiteSpace(r.RejectionReason)))
        {
            return Error.Validation("Qc.Invalid", "Each result needs a quantity passed or rejected, and a reason for any rejection.");
        }

        // Where the receipt went: inspection applies only to goods held in a quarantine warehouse.
        var held = await (
            from m in _db.StockMovements
            join w in _db.Warehouses on m.WarehouseId equals w.Id
            where m.CompanyId == companyId && m.Type == StockMovementType.Receipt && m.Reference == grn.GrnNumber
            select new { m.ItemId, m.WarehouseId, w.IsQuarantine })
            .ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, decimal> inspected = await _db.Set<GrnInspection>()
            .Where(i => i.GoodsReceivedNoteId == goodsReceivedNoteId)
            .SelectMany(i => i.Lines)
            .GroupBy(l => l.ItemId)
            .Select(g => new { ItemId = g.Key, Quantity = g.Sum(l => l.Passed + l.Rejected) })
            .ToDictionaryAsync(x => x.ItemId, x => x.Quantity, ct).ConfigureAwait(false);

        GrnInspection inspection = new(companyId, goodsReceivedNoteId, request.InspectedOn, request.InspectorUserId, request.ReleaseWarehouseId);
        decimal released = 0m;
        decimal returned = 0m;
        decimal credit = 0m;
        foreach (InspectionResult result in request.Results)
        {
            var hold = held.FirstOrDefault(h => h.ItemId == result.ItemId);
            if (hold is null || !hold.IsQuarantine)
            {
                return Error.Rule("Qc.NotHeld", $"Item {result.ItemId} on {grn.GrnNumber} was not received into quarantine.");
            }

            decimal received = grn.Lines.Where(l => l.ItemId == result.ItemId).Sum(l => l.Quantity);
            decimal outstanding = received - inspected.GetValueOrDefault(result.ItemId);
            if (result.Passed + result.Rejected > outstanding)
            {
                return Error.Rule("Qc.Exceeds", $"Only {outstanding} of item {result.ItemId} on {grn.GrnNumber} is still awaiting inspection.");
            }

            if (result.Passed > 0m)
            {
                Result<decimal> moved = await _inventory.TransferAsync(companyId, result.ItemId, hold.WarehouseId, request.ReleaseWarehouseId, request.InspectedOn, result.Passed, $"{grn.GrnNumber}-QC", ct).ConfigureAwait(false);
                if (moved.IsFailure)
                {
                    return moved.Error;
                }

                released += result.Passed;
            }

            if (result.Rejected > 0m)
            {
                decimal price = grn.Lines.First(l => l.ItemId == result.ItemId).UnitPrice;
                Result<SupplierReturnResult> back = await _procurement.ReturnToSupplierAsync(companyId, grn.SupplierId, result.ItemId, hold.WarehouseId, result.Rejected, price,
                    request.InspectedOn, $"{grn.GrnNumber}-REJ-{result.ItemId}", request.PriceVarianceAccountCode, ct).ConfigureAwait(false);
                if (back.IsFailure)
                {
                    return back.Error;
                }

                returned += result.Rejected;
                credit += back.Value.CreditValue;
            }

            inspection.AddResult(result.ItemId, result.Passed, result.Rejected, string.IsNullOrWhiteSpace(result.RejectionReason) ? null : result.RejectionReason.Trim());
        }

        _db.Set<GrnInspection>().Add(inspection);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new InspectionOutcome(inspection.Id, released, returned, credit);
    }
}
