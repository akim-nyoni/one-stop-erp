using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Documents;
using One4All.Application.Documents;
using One4All.Core.Common;
using One4All.Core.Inventory;
using One4All.Core.Procurement;
using One4All.Core.SalesOrders;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Documents;

/// <summary>Builds the standard business documents from their records and renders them as fixed-width text.</summary>
public sealed class DocumentLayoutService : IDocumentLayoutService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public DocumentLayoutService(One4AllDbContext db) => _db = db;

    private static string D(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private Task<Company> CompanyAsync(long companyId, CancellationToken ct) => _db.Companies.AsNoTracking().FirstAsync(c => c.Id == companyId, ct);

    private Task<Dictionary<long, Item>> ItemsAsync(IEnumerable<long> ids, CancellationToken ct)
    {
        List<long> list = [.. ids.Distinct()];
        return _db.Items.AsNoTracking().IgnoreQueryFilters().Where(i => list.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
    }

    private async Task<string> CustomerAsync(long id, CancellationToken ct)
        => await _db.Customers.AsNoTracking().IgnoreQueryFilters().Where(c => c.Id == id).Select(c => c.Name + " (" + c.Code + ")").FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? "?";

    private async Task<string> SupplierAsync(long id, CancellationToken ct)
        => await _db.Suppliers.AsNoTracking().IgnoreQueryFilters().Where(s => s.Id == id).Select(s => s.Name + " (" + s.Code + ")").FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? "?";

    /// <inheritdoc />
    public async Task<Result<string>> QuotationAsync(long companyId, string quoteNumber, int width = BusinessDocumentFormatter.DefaultWidth, CancellationToken ct = default)
    {
        Quotation? quote = await _db.Quotations.AsNoTracking().Include(q => q.Lines).FirstOrDefaultAsync(q => q.CompanyId == companyId && q.QuoteNumber == quoteNumber, ct).ConfigureAwait(false);
        if (quote is null)
        {
            return Error.NotFound("Doc.QuotationNotFound", $"No quotation {quoteNumber}.");
        }

        Company company = await CompanyAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<long, Item> items = await ItemsAsync(quote.Lines.Select(l => l.ItemId), ct).ConfigureAwait(false);
        List<BusinessDocumentLine> lines = [.. quote.Lines.Select(l => new BusinessDocumentLine(items.GetValueOrDefault(l.ItemId)?.Code ?? string.Empty, l.Description, l.Quantity, l.UnitPrice,
            Math.Round(l.Quantity * l.UnitPrice, 2)))];
        return BusinessDocumentFormatter.ToPlainText(new BusinessDocument
        {
            Title = "Quotation", CompanyName = company.Name, CompanyTaxNumber = company.TaxNumber, PartyName = await CustomerAsync(quote.CustomerId, ct).ConfigureAwait(false),
            Fields = [("Quotation", quote.QuoteNumber), ("Date", D(quote.QuoteDate)), ("Valid until", D(quote.ValidUntil)), ("Currency", quote.Currency.ToString())],
            Lines = lines, Totals = [($"TOTAL ({quote.Currency}) excl. tax", lines.Sum(l => l.Amount ?? 0m))],
            Notes = [$"Prices are valid until {D(quote.ValidUntil)}. Please quote {quote.QuoteNumber} when ordering."], Signatures = ["Accepted by"],
        }, width);
    }

    /// <inheritdoc />
    public async Task<Result<string>> OrderConfirmationAsync(long companyId, string orderNumber, int width = BusinessDocumentFormatter.DefaultWidth, CancellationToken ct = default)
    {
        SalesOrder? order = await _db.SalesOrders.AsNoTracking().Include(o => o.Lines).FirstOrDefaultAsync(o => o.CompanyId == companyId && o.OrderNumber == orderNumber, ct).ConfigureAwait(false);
        if (order is null)
        {
            return Error.NotFound("Doc.OrderNotFound", $"No sales order {orderNumber}.");
        }

        Company company = await CompanyAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<long, Item> items = await ItemsAsync(order.Lines.Select(l => l.ItemId), ct).ConfigureAwait(false);
        List<BusinessDocumentLine> lines = [.. order.Lines.Select(l => new BusinessDocumentLine(items.GetValueOrDefault(l.ItemId)?.Code ?? string.Empty, l.Description, l.OrderedQuantity, l.UnitPrice,
            Math.Round(l.OrderedQuantity * l.UnitPrice, 2)))];
        return BusinessDocumentFormatter.ToPlainText(new BusinessDocument
        {
            Title = "Order Confirmation", CompanyName = company.Name, CompanyTaxNumber = company.TaxNumber, PartyName = await CustomerAsync(order.CustomerId, ct).ConfigureAwait(false),
            Fields = [("Order", order.OrderNumber), ("Date", D(order.OrderDate)), ("Delivery wanted", order.RequestedDeliveryDate is DateOnly r ? D(r) : "—"), ("Status", order.Status.ToString())],
            Lines = lines, Totals = [($"ORDER TOTAL ({order.Currency}) excl. tax", lines.Sum(l => l.Amount ?? 0m))],
            Notes = ["Thank you for your order. Goods remain ours until paid for in full."],
        }, width);
    }

    /// <inheritdoc />
    public async Task<Result<string>> DeliveryNoteAsync(long companyId, string deliveryReference, bool packing = false, int width = BusinessDocumentFormatter.DefaultWidth, CancellationToken ct = default)
    {
        SalesDelivery? delivery = await _db.SalesDeliveries.AsNoTracking().Include(d => d.Lines).FirstOrDefaultAsync(d => d.CompanyId == companyId && d.Reference == deliveryReference, ct).ConfigureAwait(false);
        if (delivery is null)
        {
            return Error.NotFound("Doc.DeliveryNotFound", $"No delivery {deliveryReference}.");
        }

        SalesOrder order = await _db.SalesOrders.AsNoTracking().Include(o => o.Lines).FirstAsync(o => o.Id == delivery.SalesOrderId, ct).ConfigureAwait(false);
        Company company = await CompanyAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<long, Item> items = await ItemsAsync(delivery.Lines.Select(l => l.ItemId), ct).ConfigureAwait(false);
        Dictionary<long, string> descriptions = order.Lines.ToDictionary(l => l.Id, l => l.Description);
        decimal weight = delivery.Lines.Sum(l => l.Quantity * (items.GetValueOrDefault(l.ItemId)?.UnitWeightKg ?? 0m));
        List<string> notes = packing ? [$"Total weight: {weight.ToString("0.##", CultureInfo.InvariantCulture)} kg. Check the goods against this note before signing."] : ["Received in good order and condition."];
        if (delivery.DeliveredOn is DateOnly delivered)
        {
            notes.Add($"Signed for on {D(delivered)} by {delivery.ReceivedBy} (POD {delivery.PodReference ?? "—"}).");
        }

        return BusinessDocumentFormatter.ToPlainText(new BusinessDocument
        {
            Title = packing ? "Packing Note" : "Delivery Note", CompanyName = company.Name, CompanyTaxNumber = company.TaxNumber, PartyLabel = "Deliver to",
            PartyName = await CustomerAsync(order.CustomerId, ct).ConfigureAwait(false),
            Fields = [("Delivery", delivery.Reference), ("Order", order.OrderNumber), ("Despatched", D(delivery.DespatchedOn)), ("Vehicle", delivery.Vehicle ?? "—"), ("Route", delivery.Route ?? "—"), ("Driver", delivery.Driver ?? "—")],
            ShowPrices = false,
            Lines = [.. delivery.Lines.Select(l => new BusinessDocumentLine(items.GetValueOrDefault(l.ItemId)?.Code ?? string.Empty, descriptions.GetValueOrDefault(l.SalesOrderLineId, string.Empty), l.Quantity))],
            Notes = notes, Signatures = packing ? ["Packed by", "Checked by"] : ["Delivered by", "Received by"],
        }, width);
    }

    /// <inheritdoc />
    public async Task<Result<string>> RequisitionAsync(long companyId, string requisitionNumber, int width = BusinessDocumentFormatter.DefaultWidth, CancellationToken ct = default)
    {
        PurchaseRequisition? requisition = await _db.PurchaseRequisitions.AsNoTracking().Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.CompanyId == companyId && r.RequisitionNumber == requisitionNumber, ct).ConfigureAwait(false);
        if (requisition is null)
        {
            return Error.NotFound("Doc.RequisitionNotFound", $"No requisition {requisitionNumber}.");
        }

        Company company = await CompanyAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<long, Item> items = await ItemsAsync(requisition.Lines.Select(l => l.ItemId), ct).ConfigureAwait(false);
        List<BusinessDocumentLine> lines = [.. requisition.Lines.Select(l => new BusinessDocumentLine(items.GetValueOrDefault(l.ItemId)?.Code ?? string.Empty,
            l.Description + (l.ExpenseAccountCode is null ? string.Empty : $" [{l.ExpenseAccountCode}]"), l.Quantity, l.UnitPrice, Math.Round(l.Quantity * l.UnitPrice, 2)))];
        return BusinessDocumentFormatter.ToPlainText(new BusinessDocument
        {
            Title = "Purchase Requisition", CompanyName = company.Name, CompanyTaxNumber = company.TaxNumber, PartyLabel = "Suggested supplier",
            PartyName = await SupplierAsync(requisition.SupplierId, ct).ConfigureAwait(false),
            Fields = [("Requisition", requisition.RequisitionNumber), ("Date", D(requisition.RequestedDate)), ("Currency", requisition.Currency.ToString()), ("Status", requisition.Status.ToString())],
            Lines = lines, Totals = [($"ESTIMATED TOTAL ({requisition.Currency})", lines.Sum(l => l.Amount ?? 0m))], Signatures = ["Requested by", "Budget checked by", "Approved by"],
        }, width);
    }

    /// <inheritdoc />
    public async Task<Result<string>> GoodsReceivedNoteAsync(long companyId, string grnNumber, int width = BusinessDocumentFormatter.DefaultWidth, CancellationToken ct = default)
    {
        GoodsReceivedNote? grn = await _db.GoodsReceivedNotes.AsNoTracking().Include(g => g.Lines).FirstOrDefaultAsync(g => g.CompanyId == companyId && g.GrnNumber == grnNumber, ct).ConfigureAwait(false);
        if (grn is null)
        {
            return Error.NotFound("Doc.GrnNotFound", $"No goods received note {grnNumber}.");
        }

        string orderNumber = await _db.Set<PurchaseOrder>().AsNoTracking().Where(o => o.Id == grn.PurchaseOrderId).Select(o => o.OrderNumber).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? "?";
        Company company = await CompanyAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<long, Item> items = await ItemsAsync(grn.Lines.Select(l => l.ItemId), ct).ConfigureAwait(false);
        List<BusinessDocumentLine> lines = [.. grn.Lines.Select(l => new BusinessDocumentLine(items.GetValueOrDefault(l.ItemId)?.Code ?? string.Empty, items.GetValueOrDefault(l.ItemId)?.Description ?? string.Empty,
            l.Quantity, l.UnitPrice, Math.Round(l.Quantity * l.UnitPrice, 2)))];
        return BusinessDocumentFormatter.ToPlainText(new BusinessDocument
        {
            Title = "Goods Received Note", CompanyName = company.Name, CompanyTaxNumber = company.TaxNumber, PartyLabel = "Supplier", PartyName = await SupplierAsync(grn.SupplierId, ct).ConfigureAwait(false),
            Fields = [("GRN", grn.GrnNumber), ("Received", D(grn.Date)), ("Purchase order", orderNumber)],
            Lines = lines, Totals = [("VALUE RECEIVED", lines.Sum(l => l.Amount ?? 0m))], Notes = ["Quantities counted on receipt; quality inspection follows where required."],
            Signatures = ["Received by", "Store checked by"],
        }, width);
    }

    /// <inheritdoc />
    public async Task<Result<string>> ShelfLabelsAsync(long companyId, string priceListCode, IReadOnlyList<string>? itemCodes = null, int perRow = 2, CancellationToken ct = default)
    {
        PriceList? list = await _db.PriceLists.AsNoTracking().Include(p => p.Lines).FirstOrDefaultAsync(p => p.CompanyId == companyId && p.Code == priceListCode, ct).ConfigureAwait(false);
        if (list is null)
        {
            return Error.NotFound("Doc.PriceListNotFound", $"No price list {priceListCode}.");
        }

        Dictionary<long, Item> items = await ItemsAsync(list.Lines.Select(l => l.ItemId), ct).ConfigureAwait(false);
        HashSet<string>? wanted = itemCodes is { Count: > 0 } ? [.. itemCodes.Select(c => c.Trim().ToUpperInvariant())] : null;
        List<ShelfLabel> labels = [.. list.Lines.Where(l => l.MinimumQuantity <= 1m && items.ContainsKey(l.ItemId) && (wanted is null || wanted.Contains(items[l.ItemId].Code)))
            .OrderBy(l => items[l.ItemId].Code, StringComparer.Ordinal)
            .Select(l => new ShelfLabel(items[l.ItemId].Code, items[l.ItemId].Description, l.UnitPrice, list.Currency.ToString(), items[l.ItemId].Barcode ?? items[l.ItemId].Code))];
        return labels.Count == 0 ? Error.Rule("Doc.NoLabels", "No items on the price list to label.") : ShelfLabelFormatter.ToPlainText(labels, perRow);
    }
}
