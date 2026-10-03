using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Procurement;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Procurement;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Procurement;

/// <summary>
/// Manages purchase requisitions and their conversion into purchase orders. Requisitions are ledger-neutral;
/// conversion delegates to <see cref="IProcurementService"/> so the resulting PO behaves like any other.
/// </summary>
public sealed class PurchaseRequisitionService : IPurchaseRequisitionService
{
    private readonly One4AllDbContext _db;
    private readonly IProcurementService _procurement;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="procurement">The procurement service (conversion target).</param>
    public PurchaseRequisitionService(One4AllDbContext db, IProcurementService procurement)
    {
        _db = db;
        _procurement = procurement;
    }

    /// <inheritdoc />
    public async Task<Result<long>> CreateAsync(long companyId, long supplierId, string requisitionNumber, DateOnly requestedDate, CurrencyCode currency, IReadOnlyList<PurchaseRequisitionLineRequest> lines, CancellationToken ct = default)
    {
        if (lines is null || lines.Count == 0)
        {
            return Error.Validation("Pop.NoReqLines", "A requisition needs at least one line.");
        }

        if (lines.Any(l => l.Quantity <= 0m))
        {
            return Error.Rule("Pop.ReqLineQty", "Every requisition line quantity must be positive.");
        }

        bool supplierExists = await _db.Suppliers.AnyAsync(s => s.Id == supplierId && s.CompanyId == companyId, ct).ConfigureAwait(false);
        if (await _db.Suppliers.AnyAsync(s => s.Id == supplierId && s.CompanyId == companyId && s.IsBlacklisted, ct).ConfigureAwait(false))
        {
            return Error.Rule("Pop.SupplierBlacklisted", "The supplier is blacklisted; requisitions cannot name it.");
        }

        if (!supplierExists)
        {
            return Error.NotFound("Pop.SupplierNotFound", $"Supplier {supplierId} not found.");
        }

        PurchaseRequisition req = new(companyId, supplierId, requisitionNumber, requestedDate, currency);
        foreach (PurchaseRequisitionLineRequest line in lines)
        {
            req.AddLine(line.ItemId, line.Description, line.Quantity, line.UnitPrice);
        }

        _db.Set<PurchaseRequisition>().Add(req);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return req.Id;
    }

    /// <inheritdoc />
    public Task<Result> SubmitAsync(long companyId, long requisitionId, CancellationToken ct = default)
        => TransitionAsync(companyId, requisitionId, r => r.Status == PurchaseRequisitionStatus.Draft, r => r.Submit(), "Only a draft requisition can be submitted.", ct);

    /// <inheritdoc />
    public Task<Result> ApproveAsync(long companyId, long requisitionId, long approvedByUserId, CancellationToken ct = default)
        => TransitionAsync(companyId, requisitionId, r => r.Status == PurchaseRequisitionStatus.Submitted, r => r.Approve(approvedByUserId), "Only a submitted requisition can be approved.", ct);

    /// <inheritdoc />
    public Task<Result> RejectAsync(long companyId, long requisitionId, CancellationToken ct = default)
        => TransitionAsync(companyId, requisitionId, r => r.Status is PurchaseRequisitionStatus.Draft or PurchaseRequisitionStatus.Submitted, r => r.Reject(), "Only a draft or submitted requisition can be rejected.", ct);

    /// <inheritdoc />
    public async Task<Result<long>> ConvertToPurchaseOrderAsync(long companyId, long requisitionId, string orderNumber, DateOnly orderDate, CancellationToken ct = default)
    {
        PurchaseRequisition? req = await _db.Set<PurchaseRequisition>()
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == requisitionId && r.CompanyId == companyId, ct).ConfigureAwait(false);
        if (req is null)
        {
            return Error.NotFound("Pop.ReqNotFound", $"Requisition {requisitionId} not found.");
        }

        if (req.Status != PurchaseRequisitionStatus.Approved)
        {
            return Error.Rule("Pop.ReqNotApproved", $"Only an approved requisition can be converted; {req.RequisitionNumber} is {req.Status}.");
        }

        Result<PurchaseOrderResult> order = await _procurement.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest
        {
            CompanyId = companyId,
            SupplierId = req.SupplierId,
            OrderNumber = orderNumber,
            OrderDate = orderDate,
            Currency = req.Currency,
            Lines = req.Lines
                .Select(l => new PurchaseOrderLineRequest(l.ItemId, l.Description, l.Quantity, l.UnitPrice))
                .ToList(),
        }, ct).ConfigureAwait(false);

        if (order.IsFailure)
        {
            return order.Error;
        }

        req.MarkConverted(order.Value.PurchaseOrderId);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return order.Value.PurchaseOrderId;
    }

    private async Task<Result> TransitionAsync(long companyId, long requisitionId, Func<PurchaseRequisition, bool> allowed, Action<PurchaseRequisition> apply, string message, CancellationToken ct)
    {
        PurchaseRequisition? req = await _db.Set<PurchaseRequisition>().FirstOrDefaultAsync(r => r.Id == requisitionId && r.CompanyId == companyId, ct).ConfigureAwait(false);
        if (req is null)
        {
            return Result.Failure(Error.NotFound("Pop.ReqNotFound", $"Requisition {requisitionId} not found."));
        }

        if (!allowed(req))
        {
            return Result.Failure(Error.Rule("Pop.ReqState", message));
        }

        apply(req);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }
}
