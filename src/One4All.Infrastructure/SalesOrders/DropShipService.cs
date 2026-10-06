using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.SalesOrders;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Common;
using One4All.Core.SalesOrders;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.SalesOrders;

/// <summary>Drop-shipment: the supplier delivers straight to the customer (brief §4.13).</summary>
public sealed class DropShipService : IDropShipService
{
    private readonly One4AllDbContext _db;
    private readonly IPurchaseLedgerService _purchases;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="purchases">The purchase ledger the supplier's invoice is posted through.</param>
    public DropShipService(One4AllDbContext db, IPurchaseLedgerService purchases)
    {
        _db = db;
        _purchases = purchases;
    }

    /// <inheritdoc />
    public async Task<Result<long>> OrderAsync(long companyId, long salesOrderId, long supplierId, string reference, DateOnly date, IReadOnlyList<DropShipLine> lines, CancellationToken ct = default)
    {
        string key = (reference ?? string.Empty).Trim().ToUpperInvariant();
        if (key.Length == 0 || lines.Count == 0 || lines.Any(l => l.Quantity <= 0m || l.UnitCost < 0m))
        {
            return Error.Validation("Sop.DropShipInvalid", "A drop-shipment needs a reference and positive quantities at a price.");
        }

        SalesOrder? order = await _db.SalesOrders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == salesOrderId && o.CompanyId == companyId, ct).ConfigureAwait(false);
        if (order is null)
        {
            return Error.NotFound("Sop.OrderNotFound", $"Sales order {salesOrderId} not found.");
        }

        if (!await _db.Suppliers.AnyAsync(s => s.Id == supplierId && s.CompanyId == companyId, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Sop.SupplierNotFound", $"Supplier {supplierId} not found.");
        }

        if (await _db.DropShipments.AnyAsync(d => d.CompanyId == companyId && d.Reference == key, ct).ConfigureAwait(false)
            || await _db.SalesDeliveries.AnyAsync(d => d.CompanyId == companyId && d.Reference == key, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Sop.DropShipDuplicate", $"Reference {key} is already in use.");
        }

        // What is already on its way from suppliers counts against what is left to despatch.
        List<DropShipmentLine> pending = await _db.DropShipments.Where(d => d.SalesOrderId == salesOrderId && d.DeliveredOn == null).SelectMany(d => d.Lines).ToListAsync(ct).ConfigureAwait(false);
        foreach (IGrouping<long, DropShipLine> line in lines.GroupBy(l => l.SalesOrderLineId))
        {
            SalesOrderLine? orderLine = order.Lines.FirstOrDefault(l => l.Id == line.Key);
            if (orderLine is null)
            {
                return Error.Validation("Sop.LineNotOnOrder", $"Line {line.Key} is not on order {order.OrderNumber}.");
            }

            decimal open = orderLine.OrderedQuantity - orderLine.DespatchedQuantity - pending.Where(p => p.SalesOrderLineId == line.Key).Sum(p => p.Quantity);
            if (line.Sum(l => l.Quantity) > open)
            {
                return Error.Rule("Sop.DropShipExceeded", $"Only {open:0.##} of {orderLine.Description} remain to be sent on order {order.OrderNumber}.");
            }
        }

        DropShipment shipment = new(companyId, salesOrderId, supplierId, key, date);
        foreach (DropShipLine line in lines)
        {
            shipment.AddLine(line.SalesOrderLineId, line.Quantity, line.UnitCost);
        }

        _db.DropShipments.Add(shipment);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return shipment.Id;
    }

    /// <inheritdoc />
    public Task<Result<DropShipDelivered>> ConfirmDeliveredAsync(long companyId, string reference, DateOnly date, string supplierInvoiceNumber, string costOfSalesAccountCode, CancellationToken ct = default)
        => _db.AtomicAsync(() => ConfirmCoreAsync(companyId, reference, date, supplierInvoiceNumber, costOfSalesAccountCode, ct), ct);

    private async Task<Result<DropShipDelivered>> ConfirmCoreAsync(long companyId, string reference, DateOnly date, string supplierInvoiceNumber, string costOfSalesAccountCode, CancellationToken ct)
    {
        string key = (reference ?? string.Empty).Trim().ToUpperInvariant();
        DropShipment? shipment = await _db.DropShipments.Include(d => d.Lines).FirstOrDefaultAsync(d => d.CompanyId == companyId && d.Reference == key, ct).ConfigureAwait(false);
        if (shipment is null)
        {
            return Error.NotFound("Sop.DropShipNotFound", $"No drop-shipment '{key}'.");
        }

        if (shipment.DeliveredOn is not null)
        {
            return Error.Conflict("Sop.DropShipDelivered", $"Drop-shipment {key} was confirmed on {shipment.DeliveredOn:yyyy-MM-dd}.");
        }

        if (date < shipment.OrderedOn)
        {
            return Error.Validation("Sop.DeliveredBeforeOrder", "The supplier cannot deliver before the drop-shipment was placed.");
        }

        Supplier supplier = await _db.Suppliers.FirstAsync(s => s.Id == shipment.SupplierId, ct).ConfigureAwait(false);
        Result<TradingPostingResult> invoice = await _purchases.PostInvoiceAsync(new PurchaseInvoiceRequest
        {
            CompanyId = companyId, SupplierId = supplier.Id, DocumentNumber = supplierInvoiceNumber, DocumentDate = date, Currency = supplier.Currency, NetAmount = shipment.Cost, TaxAmount = 0m,
            ExpenseAccountCode = costOfSalesAccountCode, Narrative = $"Drop-shipment {key} delivered to the customer",
        }, ct).ConfigureAwait(false);
        if (invoice.IsFailure)
        {
            return invoice.Error;
        }

        // The goods count as despatched (with a delivery note for POD and charges) without passing through stock.
        SalesOrder order = await _db.SalesOrders.Include(o => o.Lines).FirstAsync(o => o.Id == shipment.SalesOrderId, ct).ConfigureAwait(false);
        SalesDelivery delivery = new(companyId, order.Id, key, date);
        foreach (DropShipmentLine line in shipment.Lines)
        {
            SalesOrderLine orderLine = order.Lines.First(l => l.Id == line.SalesOrderLineId);
            orderLine.Despatch(line.Quantity);
            delivery.AddLine(orderLine.Id, orderLine.ItemId, line.Quantity);
        }

        order.RecomputeStatus();
        _db.SalesDeliveries.Add(delivery);
        shipment.Delivered(date, supplierInvoiceNumber);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new DropShipDelivered(shipment.Cost, invoice.Value.JournalNumber);
    }
}
