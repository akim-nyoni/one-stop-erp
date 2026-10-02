using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.SalesOrders;
using One4All.Application.SalesOrders;
using One4All.Core.Common;
using One4All.Core.Inventory;
using One4All.Core.SalesOrders;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.SalesOrders;

/// <summary>Builds pick lists from a sales order's undespatched lines and the picking warehouse's balances.</summary>
public sealed class PickListService : IPickListService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public PickListService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result<PickListDocument>> BuildAsync(long companyId, long salesOrderId, long warehouseId, CancellationToken ct = default)
    {
        SalesOrder? order = await _db.Set<SalesOrder>().Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == salesOrderId && o.CompanyId == companyId, ct).ConfigureAwait(false);
        if (order is null)
        {
            return Error.NotFound("Sop.OrderNotFound", $"Sales order {salesOrderId} not found.");
        }

        if (order.Status == SalesOrderStatus.Cancelled)
        {
            return Error.Rule("Sop.Cancelled", $"Sales order {order.OrderNumber} is cancelled; nothing to pick.");
        }

        Warehouse? warehouse = await _db.Warehouses.FirstOrDefaultAsync(w => w.Id == warehouseId && w.CompanyId == companyId, ct).ConfigureAwait(false);
        if (warehouse is null)
        {
            return Error.NotFound("Inv.WarehouseNotFound", $"Warehouse {warehouseId} not found.");
        }

        var customer = await _db.Customers.Where(c => c.Id == order.CustomerId)
            .Select(c => new { c.Code, c.Name }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        string companyName = await _db.Companies.Where(c => c.Id == companyId).Select(c => c.Name).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? string.Empty;

        List<long> itemIds = order.Lines.Select(l => l.ItemId).Distinct().ToList();
        Dictionary<long, string> codes = await _db.Items
            .Where(i => itemIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, i => i.Code, ct).ConfigureAwait(false);
        Dictionary<long, decimal> onHand = await _db.Set<StockBalance>()
            .Where(b => b.CompanyId == companyId && b.WarehouseId == warehouseId && itemIds.Contains(b.ItemId))
            .ToDictionaryAsync(b => b.ItemId, b => b.QuantityOnHand, ct).ConfigureAwait(false);

        List<PickListLine> lines = order.Lines
            .Where(l => l.OrderedQuantity > l.DespatchedQuantity)
            .Select(l => new PickListLine(codes.GetValueOrDefault(l.ItemId, string.Empty), l.Description, l.OrderedQuantity - l.DespatchedQuantity, onHand.GetValueOrDefault(l.ItemId)))
            .ToList();

        return new PickListDocument(companyName, order.OrderNumber, order.OrderDate, customer?.Code ?? string.Empty, customer?.Name ?? string.Empty, warehouse.Code, lines);
    }

    /// <inheritdoc />
    public async Task<Result<string>> RenderTextAsync(long companyId, long salesOrderId, long warehouseId, int width = PickListFormatter.DefaultWidth, CancellationToken ct = default)
    {
        Result<PickListDocument> document = await BuildAsync(companyId, salesOrderId, warehouseId, ct).ConfigureAwait(false);
        return document.IsFailure ? document.Error : PickListFormatter.ToPlainText(document.Value, width);
    }
}
