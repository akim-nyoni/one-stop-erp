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
        return _db.Items.AsNoTracking().IgnoreQueryFilters([Persistence.One4AllDbContext.SoftDeleteFilter]).Where(i => list.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
    }

    private async Task<string> CustomerAsync(long id, CancellationToken ct)
        => await _db.Customers.AsNoTracking().IgnoreQueryFilters([Persistence.One4AllDbContext.SoftDeleteFilter]).Where(c => c.Id == id).Select(c => c.Name + " (" + c.Code + ")").FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? "?";

    private async Task<string> SupplierAsync(long id, CancellationToken ct)
        => await _db.Suppliers.AsNoTracking().IgnoreQueryFilters([Persistence.One4AllDbContext.SoftDeleteFilter]).Where(s => s.Id == id).Select(s => s.Name + " (" + s.Code + ")").FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? "?";

    /// <inheritdoc />
    public async Task<Result<string>> QuotationAsync(long companyId, string quoteNumber, int width = BusinessDocumentFormatter.DefaultWidth, CancellationToken ct = default)
        => Text(await BuildQuotationAsync(companyId, quoteNumber, ct).ConfigureAwait(false), width);

    private async Task<Result<BusinessDocument>> BuildQuotationAsync(long companyId, string quoteNumber, CancellationToken ct)
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
        return new BusinessDocument
        {
            Title = "Quotation", CompanyName = company.Name, CompanyTaxNumber = company.TaxNumber, PartyName = await CustomerAsync(quote.CustomerId, ct).ConfigureAwait(false),
            Fields = [("Quotation", quote.QuoteNumber), ("Date", D(quote.QuoteDate)), ("Valid until", D(quote.ValidUntil)), ("Currency", quote.Currency.ToString())],
            Lines = lines, Totals = [($"TOTAL ({quote.Currency}) excl. tax", lines.Sum(l => l.Amount ?? 0m))],
            Notes = [$"Prices are valid until {D(quote.ValidUntil)}. Please quote {quote.QuoteNumber} when ordering."], Signatures = ["Accepted by"],
        };
    }

    /// <inheritdoc />
    public async Task<Result<string>> OrderConfirmationAsync(long companyId, string orderNumber, int width = BusinessDocumentFormatter.DefaultWidth, CancellationToken ct = default)
        => Text(await BuildOrderConfirmationAsync(companyId, orderNumber, ct).ConfigureAwait(false), width);

    private async Task<Result<BusinessDocument>> BuildOrderConfirmationAsync(long companyId, string orderNumber, CancellationToken ct)
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
        return new BusinessDocument
        {
            Title = "Order Confirmation", CompanyName = company.Name, CompanyTaxNumber = company.TaxNumber, PartyName = await CustomerAsync(order.CustomerId, ct).ConfigureAwait(false),
            Fields = [("Order", order.OrderNumber), ("Date", D(order.OrderDate)), ("Delivery wanted", order.RequestedDeliveryDate is DateOnly r ? D(r) : "-"), ("Status", order.Status.ToString())],
            Lines = lines, Totals = [($"ORDER TOTAL ({order.Currency}) excl. tax", lines.Sum(l => l.Amount ?? 0m))],
            Notes = ["Thank you for your order. Goods remain ours until paid for in full."],
        };
    }

    /// <inheritdoc />
    public async Task<Result<string>> DeliveryNoteAsync(long companyId, string deliveryReference, bool packing = false, int width = BusinessDocumentFormatter.DefaultWidth, CancellationToken ct = default)
        => Text(await BuildDeliveryNoteAsync(companyId, deliveryReference, packing, ct).ConfigureAwait(false), width);

    private async Task<Result<BusinessDocument>> BuildDeliveryNoteAsync(long companyId, string deliveryReference, bool packing, CancellationToken ct)
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
            notes.Add($"Signed for on {D(delivered)} by {delivery.ReceivedBy} (POD {delivery.PodReference ?? "-"}).");
        }

        return new BusinessDocument
        {
            Title = packing ? "Packing Note" : "Delivery Note", CompanyName = company.Name, CompanyTaxNumber = company.TaxNumber, PartyLabel = "Deliver to",
            PartyName = await CustomerAsync(order.CustomerId, ct).ConfigureAwait(false),
            Fields = [("Delivery", delivery.Reference), ("Order", order.OrderNumber), ("Despatched", D(delivery.DespatchedOn)), ("Vehicle", delivery.Vehicle ?? "-"), ("Route", delivery.Route ?? "-"), ("Driver", delivery.Driver ?? "-")],
            ShowPrices = false,
            Lines = [.. delivery.Lines.Select(l => new BusinessDocumentLine(items.GetValueOrDefault(l.ItemId)?.Code ?? string.Empty, descriptions.GetValueOrDefault(l.SalesOrderLineId, string.Empty), l.Quantity))],
            Notes = notes, Signatures = packing ? ["Packed by", "Checked by"] : ["Delivered by", "Received by"],
        };
    }

    /// <inheritdoc />
    public async Task<Result<string>> RequisitionAsync(long companyId, string requisitionNumber, int width = BusinessDocumentFormatter.DefaultWidth, CancellationToken ct = default)
        => Text(await BuildRequisitionAsync(companyId, requisitionNumber, ct).ConfigureAwait(false), width);

    private async Task<Result<BusinessDocument>> BuildRequisitionAsync(long companyId, string requisitionNumber, CancellationToken ct)
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
        return new BusinessDocument
        {
            Title = "Purchase Requisition", CompanyName = company.Name, CompanyTaxNumber = company.TaxNumber, PartyLabel = "Suggested supplier",
            PartyName = await SupplierAsync(requisition.SupplierId, ct).ConfigureAwait(false),
            Fields = [("Requisition", requisition.RequisitionNumber), ("Date", D(requisition.RequestedDate)), ("Currency", requisition.Currency.ToString()), ("Status", requisition.Status.ToString())],
            Lines = lines, Totals = [($"ESTIMATED TOTAL ({requisition.Currency})", lines.Sum(l => l.Amount ?? 0m))], Signatures = ["Requested by", "Budget checked by", "Approved by"],
        };
    }

    /// <inheritdoc />
    public async Task<Result<string>> GoodsReceivedNoteAsync(long companyId, string grnNumber, int width = BusinessDocumentFormatter.DefaultWidth, CancellationToken ct = default)
        => Text(await BuildGoodsReceivedNoteAsync(companyId, grnNumber, ct).ConfigureAwait(false), width);

    private async Task<Result<BusinessDocument>> BuildGoodsReceivedNoteAsync(long companyId, string grnNumber, CancellationToken ct)
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
        return new BusinessDocument
        {
            Title = "Goods Received Note", CompanyName = company.Name, CompanyTaxNumber = company.TaxNumber, PartyLabel = "Supplier", PartyName = await SupplierAsync(grn.SupplierId, ct).ConfigureAwait(false),
            Fields = [("GRN", grn.GrnNumber), ("Received", D(grn.Date)), ("Purchase order", orderNumber)],
            Lines = lines, Totals = [("VALUE RECEIVED", lines.Sum(l => l.Amount ?? 0m))], Notes = ["Quantities counted on receipt; quality inspection follows where required."],
            Signatures = ["Received by", "Store checked by"],
        };
    }

    /// <inheritdoc />
    public async Task<Result<string>> SupplierDebitNoteAsync(long companyId, string documentNumber, int width = BusinessDocumentFormatter.DefaultWidth, CancellationToken ct = default)
        => Text(await BuildSupplierDebitNoteAsync(companyId, documentNumber, ct).ConfigureAwait(false), width);

    private async Task<Result<BusinessDocument>> BuildSupplierDebitNoteAsync(long companyId, string documentNumber, CancellationToken ct)
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
        return new BusinessDocument
        {
            Title = "Debit Note", CompanyName = company.Name, CompanyTaxNumber = company.TaxNumber, PartyLabel = "Supplier", PartyName = await SupplierAsync(credit.TraderId, ct).ConfigureAwait(false),
            Fields = [("Debit note", documentNumber), ("Date", D(credit.DocumentDate)), ("Currency", credit.Currency.ToString())],
            Lines = [.. returned.Select(m => new BusinessDocumentLine(items.GetValueOrDefault(m.ItemId)?.Code ?? string.Empty, items.GetValueOrDefault(m.ItemId)?.Description ?? string.Empty, -m.Quantity, price,
                Math.Round(-m.Quantity * price, 2)))],
            Totals = [($"CREDIT CLAIMED ({credit.Currency})", claimed)], Notes = ["Goods returned to you as above. Please credit our account and confirm."], Signatures = ["Returned by", "Supplier acknowledgement"],
        };
    }

    /// <inheritdoc />
    public async Task<Result<string>> ChequeAsync(long companyId, string bankAccountCode, string chequeNumber, CancellationToken ct = default)
    {
        string code = (bankAccountCode ?? string.Empty).Trim().ToUpperInvariant(), number = (chequeNumber ?? string.Empty).Trim();
        var cheque = await (from c in _db.Cheques.AsNoTracking()
                            join b in _db.BankAccounts.AsNoTracking() on c.BankAccountId equals b.Id
                            where c.CompanyId == companyId && b.Code == code && c.ChequeNumber == number
                            select new { c.ChequeNumber, c.IssuedOn, c.Payee, c.Amount, c.DocumentNumber, c.Status, Bank = b.Name, b.Currency }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (cheque is null)
        {
            return Error.NotFound("Doc.ChequeNotFound", $"No cheque {number} on bank account {code}.");
        }

        if (cheque.Status is Core.Banking.ChequeStatus.Voided or Core.Banking.ChequeStatus.Cancelled)
        {
            return Error.Rule("Doc.ChequeNotLive", $"Cheque {number} is {cheque.Status}; it is not printed.");
        }

        string currency = cheque.Currency.ToString();

        // A company working in Portuguese writes its cheques in Portuguese (meticais, kwanzas…).
        string? locale = await _db.Companies.AsNoTracking().Where(c => c.Id == companyId).Select(c => c.Locale).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        string words = locale is string l && l.StartsWith("pt", StringComparison.OrdinalIgnoreCase)
            ? currency switch
            {
                "MZN" => One4All.Reporting.Expressions.NumberToWordsPortuguese.Amount(cheque.Amount, "metical", "meticais", "centavo", "centavos"),
                "AOA" => One4All.Reporting.Expressions.NumberToWordsPortuguese.Amount(cheque.Amount, "kwanza", "kwanzas", "cêntimo", "cêntimos"),
                "USD" => One4All.Reporting.Expressions.NumberToWordsPortuguese.Amount(cheque.Amount, "dólar", "dólares", "cêntimo", "cêntimos"),
                _ => One4All.Reporting.Expressions.NumberToWordsPortuguese.Amount(cheque.Amount, currency, currency, "cêntimo", "cêntimos"),
            }
            : One4All.Reporting.Expressions.NumberToWords.Amount(cheque.Amount, currency == "USD" ? "Dollars" : currency, "Cents");
        string[] lines =
        [
            $"{cheque.Bank}                                        Cheque {cheque.ChequeNumber}",
            $"                                                    Date {cheque.IssuedOn:dd/MM/yyyy}",
            $"Pay   {cheque.Payee}",
            $"The sum of  {words} only",
            $"                                                    {currency} ***{cheque.Amount.ToString("N2", CultureInfo.InvariantCulture)}***",
            string.Empty,
            $"For payment {cheque.DocumentNumber}                         ______________________  ______________________",
            "                                                    Signatory               Signatory",
        ];
        return string.Join(Environment.NewLine, lines);
    }

    /// <inheritdoc />
    public async Task<Result<string>> MunicipalBillAsync(long companyId, string standNumber, DateOnly month, int width = BusinessDocumentFormatter.DefaultWidth, CancellationToken ct = default)
        => Text(await BuildMunicipalBillAsync(companyId, standNumber, month, ct).ConfigureAwait(false), width);

    private async Task<Result<BusinessDocument>> BuildMunicipalBillAsync(long companyId, string standNumber, DateOnly month, CancellationToken ct)
    {
        DateOnly first = new(month.Year, month.Month, 1), last = first.AddMonths(1).AddDays(-1);
        var lines = await (from l in _db.Set<Core.Municipal.BillingRunLine>().AsNoTracking()
                           join r in _db.Set<Core.Municipal.BillingRun>().AsNoTracking() on l.BillingRunId equals r.Id
                           where r.CompanyId == companyId && l.StandNumber == standNumber && l.Included && r.Period >= first && r.Period <= last
                           select new { l.InvoiceNumber, l.Narrative, l.Charge, l.RatepayerCustomerId, r.ServiceType, r.BillDate, l.PropertyId, l.Consumption, l.ReadingIds }).ToListAsync(ct).ConfigureAwait(false);
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

        // The due date is the bills' own (the ratepayer's terms when the run posted them), not a fixed month from the bill date.
        DateOnly? due = await _db.OpenItems.AsNoTracking()
            .Where(o => o.CompanyId == companyId && o.Ledger == Core.Trading.TradingLedger.AccountsReceivable && o.TraderId == ratepayer && invoices.Contains(o.DocumentNumber))
            .MaxAsync(o => (DateOnly?)o.DueDate, ct).ConfigureAwait(false);

        // A metered service shows its readings and consumption (brief §4.9 bill content) and splits the charge into the tariff's fixed charge and the usage.
        List<long> readingIds = [.. lines.SelectMany(l => string.IsNullOrEmpty(l.ReadingIds) ? [] : l.ReadingIds.Split(',').Select(s => long.Parse(s, CultureInfo.InvariantCulture)))];
        var readings = await (from m in _db.Set<Core.Municipal.MeterReading>().AsNoTracking()
                                                          join meter in _db.Set<Core.Municipal.Meter>().AsNoTracking() on m.MeterId equals meter.Id
                                                          where readingIds.Contains(m.Id)
                                                          select new { m.Id, meter.Serial, m.ReadingDate, m.PreviousReading, m.Reading, m.BilledConsumption, m.Kind }).ToListAsync(ct).ConfigureAwait(false);
        List<long> propertyIds = [.. lines.Select(l => l.PropertyId).Distinct()];
        var tariffs = await (from s in _db.Set<Core.Municipal.PropertyServiceLink>().AsNoTracking()
                             join t in _db.Tariffs.AsNoTracking() on s.TariffId equals t.Id
                             where propertyIds.Contains(s.PropertyId) && s.IsActive
                             select new { s.PropertyId, s.ServiceType, t.Code, t.Name, t.FixedCharge }).ToListAsync(ct).ConfigureAwait(false);
        List<BusinessDocumentLine> billLines = [];
        List<string> notes = [];
        foreach (var line in lines.OrderBy(l => l.ServiceType))
        {
            var tariff = tariffs.FirstOrDefault(t => t.PropertyId == line.PropertyId && t.ServiceType == line.ServiceType);
            string service = tariff is null ? line.ServiceType.ToString() : $"{line.ServiceType} ({tariff.Code})";
            if (line.Consumption > 0m && tariff is not null && tariff.FixedCharge > 0m && tariff.FixedCharge < line.Charge)
            {
                decimal usage = line.Charge - tariff.FixedCharge;
                billLines.Add(new BusinessDocumentLine(service, "Fixed charge", 1m, tariff.FixedCharge, tariff.FixedCharge));
                billLines.Add(new BusinessDocumentLine(service, "Consumption", line.Consumption, decimal.Round(usage / line.Consumption, 4), usage));
            }
            else if (line.Consumption > 0m)
            {
                billLines.Add(new BusinessDocumentLine(service, $"{line.Narrative} - consumption", line.Consumption, decimal.Round(line.Charge / line.Consumption, 4), line.Charge));
            }
            else
            {
                billLines.Add(new BusinessDocumentLine(service, line.Narrative, 1m, line.Charge, line.Charge));
            }

            List<long> mine = string.IsNullOrEmpty(line.ReadingIds) ? [] : [.. line.ReadingIds.Split(',').Select(s => long.Parse(s, CultureInfo.InvariantCulture))];
            foreach (var meter in readings.Where(r => mine.Contains(r.Id)).GroupBy(r => r.Serial))
            {
                var firstRead = meter.OrderBy(r => r.ReadingDate).First();
                var lastRead = meter.OrderBy(r => r.ReadingDate).Last();
                bool estimated = meter.Any(r => r.Kind == Core.Municipal.MeterReadingKind.Estimated);
                notes.Add(string.Create(CultureInfo.InvariantCulture, $"{line.ServiceType} meter {meter.Key}: {firstRead.PreviousReading:0.###} to {lastRead.Reading:0.###}"));
                notes.Add(string.Create(CultureInfo.InvariantCulture,
                    $"  {meter.Sum(r => r.BilledConsumption):0.###} units, read {D(lastRead.ReadingDate)}{(estimated ? " (estimated)" : string.Empty)}"));
            }
        }

        notes.Add("Pay by the due date to avoid interest and restriction of services. Quote your stand number with every payment.");
        return new BusinessDocument
        {
            Title = "Municipal Account", CompanyName = company.Name, CompanyTaxNumber = company.TaxNumber, PartyLabel = "Ratepayer", PartyName = await CustomerAsync(ratepayer, ct).ConfigureAwait(false),
            Fields = [("Stand", standNumber), ("Billing month", first.ToString("yyyy-MM", CultureInfo.InvariantCulture)), ("Bill date", D(billDate)), ("Due by", D(due ?? billDate.AddDays(30)))],
            Lines = billLines,
            Totals = [("Balance brought forward", broughtForward), ("Current charges", current), ("TOTAL DUE", broughtForward + current)],
            Notes = notes,
        };
    }

    /// <inheritdoc />
    public async Task<Result<string>> FeeInvoiceAsync(long companyId, string invoiceNumber, int width = BusinessDocumentFormatter.DefaultWidth, CancellationToken ct = default)
        => Text(await BuildFeeInvoiceAsync(companyId, invoiceNumber, ct).ConfigureAwait(false), width);

    private async Task<Result<BusinessDocument>> BuildFeeInvoiceAsync(long companyId, string invoiceNumber, CancellationToken ct)
    {
        var invoice = await (from o in _db.OpenItems.AsNoTracking()
                             join j in _db.Journals.AsNoTracking() on o.JournalEntryId equals j.Id
                             where o.CompanyId == companyId && o.Ledger == Core.Trading.TradingLedger.AccountsReceivable && o.DocumentNumber == invoiceNumber && o.ItemType == Core.Trading.OpenItemType.Invoice
                             select new { o.TraderId, o.DocumentDate, o.DueDate, o.Currency, o.OriginalTransaction, j.Narrative }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (invoice?.Narrative is not string narrative || !narrative.Contains(" fees - ", StringComparison.Ordinal))
        {
            return Error.NotFound("Doc.FeeInvoiceNotFound", $"No fee invoice {invoiceNumber}.");
        }

        string term = narrative[..narrative.IndexOf(" fees - ", StringComparison.Ordinal)];
        int open = narrative.LastIndexOf('('), close = narrative.LastIndexOf(')');
        string admission = open >= 0 && close > open ? narrative[(open + 1)..close] : string.Empty;
        Core.Schools.Student? student = await _db.Students.AsNoTracking().IgnoreQueryFilters([Persistence.One4AllDbContext.SoftDeleteFilter]).FirstOrDefaultAsync(s => s.CompanyId == companyId && s.AdmissionNumber == admission, ct).ConfigureAwait(false);
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

        return new BusinessDocument
        {
            Title = "Fee Invoice", CompanyName = company.Name, CompanyTaxNumber = company.TaxNumber, PartyLabel = "Bill to", PartyName = await CustomerAsync(invoice.TraderId, ct).ConfigureAwait(false),
            Fields = [("Invoice", invoiceNumber), ("Date", D(invoice.DocumentDate)), ("Student", student is null ? admission : $"{student.FullName} ({student.AdmissionNumber})"), ("Grade", student?.Grade ?? "-"),
                ("Term", term), ("Due by", D(invoice.DueDate))],
            Lines = lines, Totals = [($"AMOUNT DUE ({invoice.Currency})", invoice.OriginalTransaction)], Notes = ["Please quote the student's admission number with every payment."],
        };
    }

    /// <inheritdoc />
    public async Task<Result<string>> PaymentCertificateAsync(long companyId, string certificateNumber, int width = BusinessDocumentFormatter.DefaultWidth, CancellationToken ct = default)
        => Text(await BuildPaymentCertificateAsync(companyId, certificateNumber, ct).ConfigureAwait(false), width);

    private async Task<Result<BusinessDocument>> BuildPaymentCertificateAsync(long companyId, string certificateNumber, CancellationToken ct)
    {
        Core.JobCosting.ProgressCertificate? certificate = await _db.ProgressCertificates.AsNoTracking().FirstOrDefaultAsync(c => c.CompanyId == companyId && c.CertificateNumber == certificateNumber, ct).ConfigureAwait(false);
        if (certificate is null)
        {
            return Error.NotFound("Doc.CertificateNotFound", $"No payment certificate {certificateNumber}.");
        }

        Core.JobCosting.JobContract contract = await _db.JobContracts.AsNoTracking().IgnoreQueryFilters([Persistence.One4AllDbContext.SoftDeleteFilter]).FirstAsync(c => c.Id == certificate.JobContractId, ct).ConfigureAwait(false);
        Company company = await CompanyAsync(companyId, ct).ConfigureAwait(false);
        decimal valuation = certificate.WorkDoneToDate + certificate.MaterialsOnSite;
        return new BusinessDocument
        {
            Title = "Interim Payment Certificate", CompanyName = company.Name, CompanyTaxNumber = company.TaxNumber, PartyLabel = "Employer", PartyName = await CustomerAsync(contract.CustomerId, ct).ConfigureAwait(false),
            Fields = [("Certificate", certificate.CertificateNumber), ("Date", D(certificate.Date)), ("Contract", $"{contract.ContractNumber} {contract.Name}"), ("Contract value", contract.ContractValue.ToString("N2", CultureInfo.InvariantCulture))],
            ShowPrices = false,
            Totals = [("Value of work done to date", certificate.WorkDoneToDate), ("Materials on site", certificate.MaterialsOnSite), ("Gross valuation", valuation), ("Less previously certified", -certificate.PreviouslyCertified),
                ("Gross this certificate", certificate.GrossThisCertificate), ($"Less retention ({contract.RetentionPercent.ToString("0.##", CultureInfo.InvariantCulture)}%)", -certificate.RetentionThisCertificate),
                ("NET AMOUNT DUE", certificate.NetPayable)],
            Notes = [$"Retention held to date: {contract.RetentionHeld.ToString("N2", CultureInfo.InvariantCulture)}."], Signatures = ["Certified by (quantity surveyor)", "Approved by (employer)"],
        };
    }

    /// <inheritdoc />
    public Task<Result<BusinessDocument>> BuildAsync(long companyId, string kind, string number, DateOnly? date = null, CancellationToken ct = default)
    {
        string key = DocumentKinds.Find(kind)?.Kind ?? kind?.Trim() ?? string.Empty;
        return key switch
        {
            DocumentKinds.Quotation => BuildQuotationAsync(companyId, number, ct),
            DocumentKinds.OrderConfirmation => BuildOrderConfirmationAsync(companyId, number, ct),
            DocumentKinds.DeliveryNote => BuildDeliveryNoteAsync(companyId, number, false, ct),
            DocumentKinds.PackingSlip => BuildDeliveryNoteAsync(companyId, number, true, ct),
            DocumentKinds.Requisition => BuildRequisitionAsync(companyId, number, ct),
            DocumentKinds.GoodsReceivedNote => BuildGoodsReceivedNoteAsync(companyId, number, ct),
            DocumentKinds.SupplierDebitNote => BuildSupplierDebitNoteAsync(companyId, number, ct),
            DocumentKinds.FeeInvoice => BuildFeeInvoiceAsync(companyId, number, ct),
            DocumentKinds.PaymentCertificate => BuildPaymentCertificateAsync(companyId, number, ct),
            DocumentKinds.MunicipalBill => BuildMunicipalBillAsync(companyId, number, date ?? DateOnly.FromDateTime(DateTime.Today), ct),
            _ => Task.FromResult<Result<BusinessDocument>>(Error.Validation("Doc.Kind", $"No business document of kind {kind}; the kinds are {string.Join(", ", DocumentKinds.All.Select(k => k.Kind))}.")),
        };
    }

    private static Result<string> Text(Result<BusinessDocument> built, int width) => built.IsSuccess ? BusinessDocumentFormatter.ToPlainText(built.Value, width) : built.Error;

    /// <inheritdoc />
    public async Task<Result<string>> ShelfLabelsAsync(long companyId, string priceListCode, IReadOnlyList<string>? itemCodes = null, int perRow = 2, LabelLanguage language = LabelLanguage.Text,
        CancellationToken ct = default)
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
        return labels.Count == 0 ? Error.Rule("Doc.NoLabels", "No items on the price list to label.") : language switch
        {
            LabelLanguage.Zpl => ShelfLabelFormatter.ToZpl(labels),
            LabelLanguage.Epl => ShelfLabelFormatter.ToEpl(labels),
            _ => ShelfLabelFormatter.ToPlainText(labels, perRow),
        };
    }
}
