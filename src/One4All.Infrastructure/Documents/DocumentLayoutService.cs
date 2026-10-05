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
    public async Task<Result<string>> SupplierDebitNoteAsync(long companyId, string documentNumber, int width = BusinessDocumentFormatter.DefaultWidth, CancellationToken ct = default)
    {
        List<StockMovement> returned = await _db.StockMovements.AsNoTracking()
            .Where(m => m.CompanyId == companyId && m.Type == StockMovementType.SupplierReturn && m.Reference == documentNumber).ToListAsync(ct).ConfigureAwait(false);
        Core.Trading.OpenItem? credit = await _db.OpenItems.AsNoTracking()
            .FirstOrDefaultAsync(o => o.CompanyId == companyId && o.Ledger == Core.Trading.TradingLedger.AccountsPayable && o.DocumentNumber == documentNumber, ct).ConfigureAwait(false);
        if (returned.Count == 0 || credit is null)
        {
            return Error.NotFound("Doc.DebitNoteNotFound", $"No supplier return {documentNumber}.");
        }

        Company company = await CompanyAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<long, Item> items = await ItemsAsync(returned.Select(m => m.ItemId), ct).ConfigureAwait(false);
        decimal units = -returned.Sum(m => m.Quantity), claimed = Math.Abs(credit.OriginalTransaction), price = units == 0m ? 0m : Math.Round(claimed / units, 4);
        return BusinessDocumentFormatter.ToPlainText(new BusinessDocument
        {
            Title = "Debit Note", CompanyName = company.Name, CompanyTaxNumber = company.TaxNumber, PartyLabel = "Supplier", PartyName = await SupplierAsync(credit.TraderId, ct).ConfigureAwait(false),
            Fields = [("Debit note", documentNumber), ("Date", D(credit.DocumentDate)), ("Currency", credit.Currency.ToString())],
            Lines = [.. returned.Select(m => new BusinessDocumentLine(items.GetValueOrDefault(m.ItemId)?.Code ?? string.Empty, items.GetValueOrDefault(m.ItemId)?.Description ?? string.Empty, -m.Quantity, price,
                Math.Round(-m.Quantity * price, 2)))],
            Totals = [($"CREDIT CLAIMED ({credit.Currency})", claimed)], Notes = ["Goods returned to you as above. Please credit our account and confirm."], Signatures = ["Returned by", "Supplier acknowledgement"],
        }, width);
    }

    /// <inheritdoc />
    public async Task<Result<string>> MunicipalBillAsync(long companyId, string standNumber, DateOnly month, int width = BusinessDocumentFormatter.DefaultWidth, CancellationToken ct = default)
    {
        DateOnly first = new(month.Year, month.Month, 1), last = first.AddMonths(1).AddDays(-1);
        var lines = await (from l in _db.Set<Core.Municipal.BillingRunLine>().AsNoTracking()
                           join r in _db.Set<Core.Municipal.BillingRun>().AsNoTracking() on l.BillingRunId equals r.Id
                           where r.CompanyId == companyId && l.StandNumber == standNumber && l.Included && r.Period >= first && r.Period <= last
                           select new { l.InvoiceNumber, l.Narrative, l.Charge, l.RatepayerCustomerId, r.ServiceType, r.BillDate }).ToListAsync(ct).ConfigureAwait(false);
        if (lines.Count == 0)
        {
            return Error.NotFound("Doc.BillNotFound", $"Stand {standNumber} was not billed in {first:yyyy-MM}.");
        }

        long ratepayer = lines[0].RatepayerCustomerId;
        DateOnly billDate = lines.Max(l => l.BillDate);
        List<string> invoices = [.. lines.Select(l => l.InvoiceNumber)];
        decimal broughtForward = (await Trading.OpenItemsAsAt.LoadAsync(_db, companyId, Core.Trading.TradingLedger.AccountsReceivable, billDate, ct).ConfigureAwait(false))
            .Where(o => o.Item.TraderId == ratepayer && !invoices.Contains(o.Item.DocumentNumber)).Sum(o => o.Signed(Core.Trading.LedgerSign.Debit));
        decimal current = lines.Sum(l => l.Charge);
        Company company = await CompanyAsync(companyId, ct).ConfigureAwait(false);
        return BusinessDocumentFormatter.ToPlainText(new BusinessDocument
        {
            Title = "Municipal Account", CompanyName = company.Name, CompanyTaxNumber = company.TaxNumber, PartyLabel = "Ratepayer", PartyName = await CustomerAsync(ratepayer, ct).ConfigureAwait(false),
            Fields = [("Stand", standNumber), ("Billing month", first.ToString("yyyy-MM", CultureInfo.InvariantCulture)), ("Bill date", D(billDate)), ("Due by", D(billDate.AddDays(30)))],
            Lines = [.. lines.OrderBy(l => l.ServiceType).Select(l => new BusinessDocumentLine(l.ServiceType.ToString(), l.Narrative, 1m, l.Charge, l.Charge))],
            Totals = [("Balance brought forward", broughtForward), ("Current charges", current), ("TOTAL DUE", broughtForward + current)],
            Notes = ["Pay by the due date to avoid interest and restriction of services. Quote your stand number with every payment."],
        }, width);
    }

    /// <inheritdoc />
    public async Task<Result<string>> FeeInvoiceAsync(long companyId, string invoiceNumber, int width = BusinessDocumentFormatter.DefaultWidth, CancellationToken ct = default)
    {
        var invoice = await (from o in _db.OpenItems.AsNoTracking()
                             join j in _db.Journals.AsNoTracking() on o.JournalEntryId equals j.Id
                             where o.CompanyId == companyId && o.Ledger == Core.Trading.TradingLedger.AccountsReceivable && o.DocumentNumber == invoiceNumber && o.ItemType == Core.Trading.OpenItemType.Invoice
                             select new { o.TraderId, o.DocumentDate, o.DueDate, o.Currency, o.OriginalTransaction, j.Narrative }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (invoice?.Narrative is not string narrative || !narrative.Contains(" fees — ", StringComparison.Ordinal))
        {
            return Error.NotFound("Doc.FeeInvoiceNotFound", $"No fee invoice {invoiceNumber}.");
        }

        string term = narrative[..narrative.IndexOf(" fees — ", StringComparison.Ordinal)];
        int open = narrative.LastIndexOf('('), close = narrative.LastIndexOf(')');
        string admission = open >= 0 && close > open ? narrative[(open + 1)..close] : string.Empty;
        Core.Schools.Student? student = await _db.Students.AsNoTracking().IgnoreQueryFilters().FirstOrDefaultAsync(s => s.CompanyId == companyId && s.AdmissionNumber == admission, ct).ConfigureAwait(false);
        Company company = await CompanyAsync(companyId, ct).ConfigureAwait(false);
        List<BusinessDocumentLine> lines;
        int tag = narrative.IndexOf('[', StringComparison.Ordinal);
        if (tag >= 0)
        {
            lines = [new BusinessDocumentLine(string.Empty, narrative[(tag + 1)..narrative.IndexOf(']', tag)], 1m, invoice.OriginalTransaction, invoice.OriginalTransaction)];
        }
        else
        {
            Core.Schools.FeeStructure? structure = student is null ? null : await _db.FeeStructures.AsNoTracking().Include(f => f.Items)
                .Where(f => f.CompanyId == companyId && f.Grade == student.Grade && f.Term == term).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            lines = [.. (structure?.Items.Where(i => i.IsCompulsory) ?? []).Select(i => new BusinessDocumentLine(string.Empty, i.Description, 1m, i.Amount, i.Amount))];
            decimal discount = lines.Sum(l => l.Amount ?? 0m) - invoice.OriginalTransaction;
            if (discount > 0m)
            {
                lines.Add(new BusinessDocumentLine(string.Empty, $"Less bursary / discount ({student?.BursaryType})", 1m, -discount, -discount));
            }
        }

        return BusinessDocumentFormatter.ToPlainText(new BusinessDocument
        {
            Title = "Fee Invoice", CompanyName = company.Name, CompanyTaxNumber = company.TaxNumber, PartyLabel = "Bill to", PartyName = await CustomerAsync(invoice.TraderId, ct).ConfigureAwait(false),
            Fields = [("Invoice", invoiceNumber), ("Date", D(invoice.DocumentDate)), ("Student", student is null ? admission : $"{student.FullName} ({student.AdmissionNumber})"), ("Grade", student?.Grade ?? "—"),
                ("Term", term), ("Due by", D(invoice.DueDate))],
            Lines = lines, Totals = [($"AMOUNT DUE ({invoice.Currency})", invoice.OriginalTransaction)], Notes = ["Please quote the student's admission number with every payment."],
        }, width);
    }

    /// <inheritdoc />
    public async Task<Result<string>> PaymentCertificateAsync(long companyId, string certificateNumber, int width = BusinessDocumentFormatter.DefaultWidth, CancellationToken ct = default)
    {
        Core.JobCosting.ProgressCertificate? certificate = await _db.ProgressCertificates.AsNoTracking().FirstOrDefaultAsync(c => c.CompanyId == companyId && c.CertificateNumber == certificateNumber, ct).ConfigureAwait(false);
        if (certificate is null)
        {
            return Error.NotFound("Doc.CertificateNotFound", $"No payment certificate {certificateNumber}.");
        }

        Core.JobCosting.JobContract contract = await _db.JobContracts.AsNoTracking().IgnoreQueryFilters().FirstAsync(c => c.Id == certificate.JobContractId, ct).ConfigureAwait(false);
        Company company = await CompanyAsync(companyId, ct).ConfigureAwait(false);
        decimal valuation = certificate.WorkDoneToDate + certificate.MaterialsOnSite;
        return BusinessDocumentFormatter.ToPlainText(new BusinessDocument
        {
            Title = "Interim Payment Certificate", CompanyName = company.Name, CompanyTaxNumber = company.TaxNumber, PartyLabel = "Employer", PartyName = await CustomerAsync(contract.CustomerId, ct).ConfigureAwait(false),
            Fields = [("Certificate", certificate.CertificateNumber), ("Date", D(certificate.Date)), ("Contract", $"{contract.ContractNumber} {contract.Name}"), ("Contract value", contract.ContractValue.ToString("N2", CultureInfo.InvariantCulture))],
            ShowPrices = false,
            Totals = [("Value of work done to date", certificate.WorkDoneToDate), ("Materials on site", certificate.MaterialsOnSite), ("Gross valuation", valuation), ("Less previously certified", -certificate.PreviouslyCertified),
                ("Gross this certificate", certificate.GrossThisCertificate), ($"Less retention ({contract.RetentionPercent.ToString("0.##", CultureInfo.InvariantCulture)}%)", -certificate.RetentionThisCertificate),
                ("NET AMOUNT DUE", certificate.NetPayable)],
            Notes = [$"Retention held to date: {contract.RetentionHeld.ToString("N2", CultureInfo.InvariantCulture)}."], Signatures = ["Certified by (quantity surveyor)", "Approved by (employer)"],
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
