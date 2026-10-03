using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Procurement;
using One4All.Core.Common;
using One4All.Core.Procurement;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Procurement;

/// <summary>Purchase order amendments, their history and re-approval.</summary>
public sealed class PurchaseOrderAmendmentService : IPurchaseOrderAmendmentService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public PurchaseOrderAmendmentService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public Task<Result<AmendmentResult>> AmendAsync(long companyId, long purchaseOrderId, AmendmentRequest request, CancellationToken ct = default)
        => _db.AtomicAsync(() => AmendCoreAsync(companyId, purchaseOrderId, request, ct), ct);

    // One unit: the retained version and the changed order are saved together.
    private async Task<Result<AmendmentResult>> AmendCoreAsync(long companyId, long purchaseOrderId, AmendmentRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Lines.Count == 0)
        {
            return Error.Validation("Pop.AmendInvalid", "An amendment needs a reason and at least one line change.");
        }

        PurchaseOrder? order = await _db.Set<PurchaseOrder>().Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == purchaseOrderId && o.CompanyId == companyId, ct).ConfigureAwait(false);
        if (order is null)
        {
            return Error.NotFound("Pop.OrderNotFound", $"Purchase order {purchaseOrderId} not found.");
        }

        if (order.Status is PurchaseOrderStatus.Cancelled or PurchaseOrderStatus.Closed)
        {
            return Error.Rule("Pop.AmendClosed", $"Purchase order {order.OrderNumber} is {order.Status} and cannot be amended.");
        }

        decimal before = order.TotalNet;
        string snapshot = JsonSerializer.Serialize(order.Lines.Select(l => new AmendedLine(l.Id, l.OrderedQuantity, l.UnitPrice)).ToList());
        _db.Set<PurchaseOrderRevision>().Add(new PurchaseOrderRevision(companyId, order.Id, order.Version, before, snapshot, request.Reason.Trim(), request.UserId, request.AmendedOn));

        foreach (AmendedLine change in request.Lines)
        {
            Result amended = order.AmendLine(change.PurchaseOrderLineId, change.Quantity, change.UnitPrice);
            if (amended.IsFailure)
            {
                return amended.Error;
            }
        }

        decimal after = order.TotalNet;
        bool needsReapproval = before == 0m ? after > 0m : (after - before) / before * 100m > request.ReapprovalThresholdPercent;
        order.MarkAmended(request.UserId, needsReapproval);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new AmendmentResult(order.Version, before, after, order.AwaitingReapproval);
    }

    /// <inheritdoc />
    public async Task<Result> ReapproveAsync(long companyId, long purchaseOrderId, long userId, CancellationToken ct = default)
    {
        PurchaseOrder? order = await _db.Set<PurchaseOrder>().FirstOrDefaultAsync(o => o.Id == purchaseOrderId && o.CompanyId == companyId, ct).ConfigureAwait(false);
        if (order is null)
        {
            return Result.Failure(Error.NotFound("Pop.OrderNotFound", $"Purchase order {purchaseOrderId} not found."));
        }

        Result approved = order.Reapprove(userId);
        if (approved.IsFailure)
        {
            return approved;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OrderVersion>> HistoryAsync(long companyId, long purchaseOrderId, CancellationToken ct = default)
    {
        List<PurchaseOrderRevision> revisions = await _db.Set<PurchaseOrderRevision>().AsNoTracking()
            .Where(r => r.CompanyId == companyId && r.PurchaseOrderId == purchaseOrderId)
            .OrderBy(r => r.Version)
            .ToListAsync(ct).ConfigureAwait(false);
        return [.. revisions.Select(r => new OrderVersion(r.Version, r.TotalNet,
            JsonSerializer.Deserialize<List<AmendedLine>>(r.LinesSnapshot) ?? [], r.Reason, r.AmendedByUserId, r.AmendedOn))];
    }
}
