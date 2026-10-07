using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.SalesOrders;
using One4All.Core.Common;
using One4All.Core.SalesOrders;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.SalesOrders;

/// <summary>Sales order amendments with their history (brief §4.13).</summary>
public sealed class SalesOrderAmendmentService : ISalesOrderAmendmentService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public SalesOrderAmendmentService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public Task<Result<SalesOrderAmendmentResult>> AmendAsync(long companyId, long salesOrderId, SalesOrderAmendment amendment, CancellationToken ct = default)
        => _db.AtomicAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(amendment);
            if (string.IsNullOrWhiteSpace(amendment.Reason) || amendment.Lines.Count == 0)
            {
                return Error.Validation("Sop.AmendInvalid", "An amendment needs a reason and at least one line change.");
            }

            SalesOrder? order = await _db.SalesOrders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == salesOrderId && o.CompanyId == companyId, ct).ConfigureAwait(false);
            if (order is null)
            {
                return Error.NotFound("Sop.OrderNotFound", $"Sales order {salesOrderId} not found.");
            }

            if (order.Status is SalesOrderStatus.Cancelled or SalesOrderStatus.Invoiced)
            {
                return Error.Rule("Sop.AmendClosed", $"Sales order {order.OrderNumber} is {order.Status} and cannot be amended.");
            }

            foreach (SalesOrderLineChange change in amendment.Lines)
            {
                Result allowed = order.CheckAmendment(change.SalesOrderLineId, change.Quantity, change.UnitPrice);
                if (allowed.IsFailure)
                {
                    return allowed.Error;
                }
            }

            decimal before = order.TotalNet;
            string snapshot = JsonSerializer.Serialize(order.Lines.Select(l => new SalesOrderLineChange(l.Id, l.OrderedQuantity, l.UnitPrice)).ToList());
            _db.Set<SalesOrderRevision>().Add(new SalesOrderRevision(companyId, order.Id, order.Version, before, snapshot, amendment.Reason.Trim(), amendment.UserId, amendment.AmendedOn));
            foreach (SalesOrderLineChange change in amendment.Lines)
            {
                order.AmendLine(change.SalesOrderLineId, change.Quantity, change.UnitPrice);
            }

            order.MarkAmended(amendment.UserId);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result<SalesOrderAmendmentResult>.Success(new SalesOrderAmendmentResult(order.Version, before, order.TotalNet));
        }, ct);

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<SalesOrderVersion>>> HistoryAsync(long companyId, long salesOrderId, CancellationToken ct = default)
    {
        SalesOrder? order = await _db.SalesOrders.AsNoTracking().Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == salesOrderId && o.CompanyId == companyId, ct).ConfigureAwait(false);
        if (order is null)
        {
            return Error.NotFound("Sop.OrderNotFound", $"Sales order {salesOrderId} not found.");
        }

        List<SalesOrderRevision> revisions = await _db.Set<SalesOrderRevision>().AsNoTracking().Where(r => r.SalesOrderId == salesOrderId).OrderBy(r => r.Version).ToListAsync(ct).ConfigureAwait(false);
        List<SalesOrderVersion> versions = [.. revisions.Select(r => new SalesOrderVersion(r.Version, r.TotalNet,
            JsonSerializer.Deserialize<List<SalesOrderLineChange>>(r.LinesSnapshot) ?? [], r.Reason, r.AmendedByUserId, r.AmendedOn))];
        versions.Add(new SalesOrderVersion(order.Version, order.TotalNet, [.. order.Lines.Select(l => new SalesOrderLineChange(l.Id, l.OrderedQuantity, l.UnitPrice))], null, null, null));
        return versions;
    }
}
