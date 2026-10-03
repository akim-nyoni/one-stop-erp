using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Numbering;
using One4All.Core.Common;
using One4All.Core.Inventory;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Inventory;

/// <summary>Store requisitions and issues through the inventory service.</summary>
public sealed class StoreRequisitionService : IStoreRequisitionService
{
    private readonly One4AllDbContext _db;
    private readonly IInventoryService _inventory;
    private readonly INumberSequenceService _numbers;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="inventory">The inventory service, which issues the goods.</param>
    /// <param name="numbers">The number sequences.</param>
    public StoreRequisitionService(One4AllDbContext db, IInventoryService inventory, INumberSequenceService numbers)
    {
        _db = db;
        _inventory = inventory;
        _numbers = numbers;
    }

    /// <inheritdoc />
    public Task<Result<(long Id, string Number)>> RaiseAsync(long companyId, StoreRequisitionRequest request, CancellationToken ct = default)
        => _db.AtomicAsync(() => RaiseCoreAsync(companyId, request, ct), ct);

    private async Task<Result<(long Id, string Number)>> RaiseCoreAsync(long companyId, StoreRequisitionRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Lines.Count == 0 || request.Lines.Any(l => l.Quantity <= 0m) || string.IsNullOrWhiteSpace(request.ChargeAccountCode)
            || request.Lines.Select(l => l.ItemId).Distinct().Count() != request.Lines.Count)
        {
            return Error.Validation("Store.Invalid", "A requisition needs an expense account and positive quantities, one line per item.");
        }

        Result<string> number = await _numbers.AllocateAsync(companyId, "SR", request.Date, ct: ct).ConfigureAwait(false);
        if (number.IsFailure)
        {
            return number.Error;
        }

        StoreRequisition requisition = new(companyId, number.Value, request.Date, request.WarehouseId, request.ChargeAccountCode.Trim(),
            string.IsNullOrWhiteSpace(request.CostCentre) ? null : request.CostCentre.Trim(), request.RequestedByUserId);
        foreach (StoreRequisitionLineRequest line in request.Lines)
        {
            requisition.AddLine(line.ItemId, line.Quantity);
        }

        _db.Set<StoreRequisition>().Add(requisition);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return (requisition.Id, requisition.Number);
    }

    /// <inheritdoc />
    public async Task<Result> ApproveAsync(long companyId, long requisitionId, long approverUserId, CancellationToken ct = default)
    {
        StoreRequisition? requisition = await _db.Set<StoreRequisition>().FirstOrDefaultAsync(r => r.Id == requisitionId && r.CompanyId == companyId, ct).ConfigureAwait(false);
        if (requisition is null)
        {
            return Result.Failure(Error.NotFound("Store.NotFound", $"Store requisition {requisitionId} not found."));
        }

        if (!requisition.Approve(approverUserId))
        {
            return Result.Failure(Error.Rule("Store.CannotApprove", $"{requisition.Number} is {requisition.Status}, or the approver raised it."));
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public Task<Result<StoreIssueResult>> IssueAsync(long companyId, long requisitionId, DateOnly date, IReadOnlyList<StoreRequisitionLineRequest> lines, CancellationToken ct = default)
        => _db.AtomicAsync(() => IssueCoreAsync(companyId, requisitionId, date, lines, ct), ct);

    // One unit: the issue note number, every stock issue and the requisition's progress.
    private async Task<Result<StoreIssueResult>> IssueCoreAsync(long companyId, long requisitionId, DateOnly date, IReadOnlyList<StoreRequisitionLineRequest> lines, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(lines);
        StoreRequisition? requisition = await _db.Set<StoreRequisition>().Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == requisitionId && r.CompanyId == companyId, ct).ConfigureAwait(false);
        if (requisition is null)
        {
            return Error.NotFound("Store.NotFound", $"Store requisition {requisitionId} not found.");
        }

        if (requisition.Status is not (StoreRequisitionStatus.Approved or StoreRequisitionStatus.PartIssued))
        {
            return Error.Rule("Store.NotApproved", $"{requisition.Number} is {requisition.Status}; only an approved requisition can be issued.");
        }

        foreach (StoreRequisitionLineRequest line in lines)
        {
            StoreRequisitionLine? asked = requisition.Lines.FirstOrDefault(l => l.ItemId == line.ItemId);
            if (asked is null || line.Quantity <= 0m || line.Quantity > asked.Outstanding)
            {
                return Error.Rule("Store.OverIssue", $"Item {line.ItemId} is not on {requisition.Number} or is more than the {asked?.Outstanding ?? 0m} still outstanding.");
            }
        }

        Result<string> note = await _numbers.AllocateAsync(companyId, "SIN", date, ct: ct).ConfigureAwait(false);
        if (note.IsFailure)
        {
            return note.Error;
        }

        decimal cost = 0m;
        foreach (StoreRequisitionLineRequest line in lines)
        {
            Result<StockMovementResult> issued = await _inventory.IssueAsync(new IssueStockRequest
            {
                CompanyId = companyId, ItemId = line.ItemId, WarehouseId = requisition.WarehouseId, Date = date, Quantity = line.Quantity,
                Reference = note.Value, ChargeAccountCode = requisition.ChargeAccountCode, CostCentre = requisition.CostCentre,
            }, ct).ConfigureAwait(false);
            if (issued.IsFailure)
            {
                return issued.Error;
            }

            cost += issued.Value.Value;
            requisition.RecordIssue(line.ItemId, line.Quantity);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new StoreIssueResult(note.Value, cost, requisition.Status == StoreRequisitionStatus.Issued);
    }
}
