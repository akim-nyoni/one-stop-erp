using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Procurement;
using One4All.Application.Procurement;
using One4All.Core.Common;
using One4All.Core.Procurement;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Procurement;

/// <summary>Delivery schedules on purchase order lines and the expediting list (brief §4.3).</summary>
public sealed class DeliveryScheduleService : IDeliveryScheduleService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public DeliveryScheduleService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result> ScheduleAsync(long companyId, long purchaseOrderLineId, IReadOnlyList<(DateOnly Due, decimal Quantity)> deliveries, CancellationToken ct = default)
    {
        PurchaseOrder? order = await _db.PurchaseOrders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.CompanyId == companyId && o.Lines.Any(l => l.Id == purchaseOrderLineId), ct).ConfigureAwait(false);
        if (order is null)
        {
            return Result.Failure(Error.NotFound("Pop.LineNotFound", $"Purchase order line {purchaseOrderLineId} not found."));
        }

        if (order.Status is PurchaseOrderStatus.Cancelled or PurchaseOrderStatus.Closed)
        {
            return Result.Failure(Error.Rule("Pop.OrderNotOpen", $"Order {order.OrderNumber} is {order.Status}."));
        }

        PurchaseOrderLine line = order.Lines.Single(l => l.Id == purchaseOrderLineId);
        if (deliveries.Count == 0 || deliveries.Any(d => d.Quantity <= 0m || d.Due < order.OrderDate) || deliveries.Sum(d => d.Quantity) != line.OrderedQuantity)
        {
            return Result.Failure(Error.Validation("Pop.ScheduleInvalid",
                $"Deliveries need positive quantities on or after the order date, adding up to the {line.OrderedQuantity:0.####} ordered."));
        }

        _db.PurchaseOrderDeliveries.RemoveRange(await _db.PurchaseOrderDeliveries.Where(d => d.PurchaseOrderLineId == purchaseOrderLineId).ToListAsync(ct).ConfigureAwait(false));
        _db.PurchaseOrderDeliveries.AddRange(deliveries.OrderBy(d => d.Due).Select(d => new PurchaseOrderDelivery(companyId, purchaseOrderLineId, d.Due, d.Quantity)));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ExpediteLine>> ExpediteAsync(long companyId, DateOnly asOf, int daysAhead = 0, CancellationToken ct = default)
    {
        DateOnly horizon = asOf.AddDays(Math.Max(0, daysAhead));
        List<PurchaseOrder> orders = await _db.PurchaseOrders.AsNoTracking().Include(o => o.Lines)
            .Where(o => o.CompanyId == companyId && (o.Status == PurchaseOrderStatus.Open || o.Status == PurchaseOrderStatus.PartReceived))
            .ToListAsync(ct).ConfigureAwait(false);
        List<long> lineIds = [.. orders.SelectMany(o => o.Lines).Select(l => l.Id)];
        ILookup<long, PurchaseOrderDelivery> schedule = (await _db.PurchaseOrderDeliveries.AsNoTracking().Where(d => lineIds.Contains(d.PurchaseOrderLineId)).ToListAsync(ct).ConfigureAwait(false))
            .ToLookup(d => d.PurchaseOrderLineId);
        List<long> supplierIds = [.. orders.Select(o => o.SupplierId).Distinct()];
        List<long> itemIds = [.. orders.SelectMany(o => o.Lines).Select(l => l.ItemId).Distinct()];
        Dictionary<long, string> suppliers = await _db.Suppliers.AsNoTracking().Where(s => supplierIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Code, ct).ConfigureAwait(false);
        Dictionary<long, string> items = await _db.Items.AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Code, ct).ConfigureAwait(false);

        List<ExpediteLine> chase = [];
        foreach (PurchaseOrder order in orders)
        {
            foreach (PurchaseOrderLine line in order.Lines.Where(l => l.ReceivedQuantity < l.OrderedQuantity))
            {
                IReadOnlyList<DeliverySlot> slots = DeliverySchedule.Match(line.OrderedQuantity, line.ReceivedQuantity, schedule[line.Id].Select(d => (d.DueDate, d.Quantity)),
                    order.RequiredBy ?? order.OrderDate);
                chase.AddRange(slots.Where(s => s.Outstanding > 0m && s.DueDate <= horizon).Select(s => new ExpediteLine(order.OrderNumber, suppliers.GetValueOrDefault(order.SupplierId, string.Empty),
                    items.GetValueOrDefault(line.ItemId, string.Empty), s.DueDate, s.Quantity, s.Received, s.Outstanding, asOf.DayNumber - s.DueDate.DayNumber)));
            }
        }

        return [.. chase.OrderByDescending(c => c.DaysLate).ThenBy(c => c.OrderNumber, StringComparer.Ordinal).ThenBy(c => c.ItemCode, StringComparer.Ordinal)];
    }
}
