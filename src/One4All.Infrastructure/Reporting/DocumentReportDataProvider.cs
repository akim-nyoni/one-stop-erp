using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Documents;
using One4All.Application.Abstractions.Procurement;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Documents;
using One4All.Application.Procurement;
using One4All.Application.Trading;
using One4All.Core.Common;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Reporting;

/// <summary>
/// Rows for the designable document reports (DOC01–DOC46): one business document, tax invoice or purchase order by its number, one row per line with
/// the heading, party, reference fields and totals on every row, so a company lays out its own invoice, quotation or delivery note in the designer; and,
/// for the text-backed documents (statements, payslips, vouchers, certificates, tickets …), one row per line of the standard text to frame.
/// </summary>
public sealed class DocumentReportDataProvider : IReportDataProvider
{
    private const string Prefix = "Doc";
    private readonly One4AllDbContext _db;
    private readonly IDocumentLayoutService _documents;
    private readonly IInvoiceDocumentService _invoices;
    private readonly IPurchaseOrderDocumentService _orders;
    private readonly ITextDocumentService _text;

    /// <summary>Creates the provider.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="documents">The business documents.</param>
    /// <param name="invoices">Tax invoices and credit notes.</param>
    /// <param name="orders">Purchase orders.</param>
    /// <param name="text">The text-backed documents.</param>
    public DocumentReportDataProvider(One4AllDbContext db, IDocumentLayoutService documents, IInvoiceDocumentService invoices, IPurchaseOrderDocumentService orders, ITextDocumentService text)
    {
        _text = text;
        _db = db;
        _documents = documents;
        _invoices = invoices;
        _orders = orders;
    }

    /// <summary>The data-source command for a kind.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The command.</returns>
    public static string CommandFor(string kind) => Prefix + kind;

    /// <inheritdoc />
    public bool CanHandle(string command) => command.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) && DocumentKinds.Find(command[Prefix.Length..]) is not null;

    /// <inheritdoc />
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetRowsAsync(long companyId, string command, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        DocumentKind kind = DocumentKinds.Find(command[Prefix.Length..]) ?? throw new ArgumentException($"Unknown document command {command}.", nameof(command));
        string number = (parameters.TryGetValue("Number", out object? n) ? n?.ToString() : null)?.Trim() ?? string.Empty;
        DateOnly? date = parameters.TryGetValue("Date", out object? d) && d is DateOnly given ? given : null;
        if (number.Length == 0)
        {
            return [];
        }

        if (kind.Text)
        {
            Result<string> text = await _text.RenderAsync(companyId, kind.Kind, number, date, ct).ConfigureAwait(false);
            return text.IsSuccess ? TextRows(kind, number, date, text.Value) : [];
        }

        switch (kind.Kind)
        {
            case DocumentKinds.TaxInvoice:
            {
                long? item = await _db.OpenItems.AsNoTracking().Where(o => o.CompanyId == companyId && o.Ledger == TradingLedger.AccountsReceivable && o.DocumentNumber == number
                        && (o.ItemType == OpenItemType.Invoice || o.ItemType == OpenItemType.CreditNote)).Select(o => (long?)o.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
                Result<InvoiceDocument> invoice = item is null ? Error.NotFound("Doc.InvoiceNotFound", $"No invoice {number}.") : await _invoices.BuildAsync(companyId, item.Value, ct).ConfigureAwait(false);
                return invoice.IsSuccess ? InvoiceRows(invoice.Value) : [];
            }

            case DocumentKinds.PurchaseOrder:
            {
                long? order = await _db.PurchaseOrders.AsNoTracking().Where(o => o.CompanyId == companyId && o.OrderNumber == number).Select(o => (long?)o.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
                Result<PurchaseOrderDocument> document = order is null ? Error.NotFound("Doc.OrderNotFound", $"No purchase order {number}.") : await _orders.BuildAsync(companyId, order.Value, ct).ConfigureAwait(false);
                return document.IsSuccess ? OrderRows(document.Value) : [];
            }

            default:
            {
                Result<BusinessDocument> document = await _documents.BuildAsync(companyId, kind.Kind, number, date, ct).ConfigureAwait(false);
                return document.IsSuccess ? BusinessDocumentRows.Of(document.Value) : [];
            }
        }
    }

    // One row per line of the standard text, the heading fields on every row, so the detail band prints the document in a monospaced font inside the company's frame.
    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> TextRows(DocumentKind kind, string number, DateOnly? date, string text)
    {
        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n').Split('\n');
        return [.. lines.Select((line, index) => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Title"] = kind.Name, ["Number"] = number, ["Date"] = date, ["LineNo"] = index + 1, ["Line"] = line.TrimEnd(),
        })];
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> InvoiceRows(InvoiceDocument invoice)
    {
        IReadOnlyList<InvoiceDocumentLine> lines = invoice.Lines.Count == 0 ? [new InvoiceDocumentLine(invoice.Narrative ?? string.Empty, invoice.Net)] : invoice.Lines;
        return [.. lines.Select((line, index) => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Kind"] = invoice.IsCreditNote ? "Credit note" : "Tax invoice", ["CompanyName"] = invoice.CompanyName, ["CompanyTaxNumber"] = invoice.CompanyTaxNumber,
            ["CustomerCode"] = invoice.CustomerCode, ["CustomerName"] = invoice.CustomerName, ["CustomerTaxNumber"] = invoice.CustomerTaxNumber,
            ["CustomerAddress"] = string.Join(", ", invoice.CustomerAddress ?? []), ["Number"] = invoice.DocumentNumber, ["Date"] = invoice.DocumentDate, ["DueDate"] = invoice.DueDate,
            ["Currency"] = invoice.Currency, ["LineNo"] = index + 1, ["Description"] = line.Description, ["Amount"] = line.Amount,
            ["Net"] = invoice.Net, ["Tax"] = invoice.Tax, ["Gross"] = invoice.Gross, ["Outstanding"] = invoice.Outstanding, ["Narrative"] = invoice.Narrative ?? string.Empty,
        })];
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> OrderRows(PurchaseOrderDocument order)
    {
        IReadOnlyList<PurchaseOrderDocumentLine> lines = order.Lines.Count == 0 ? [new PurchaseOrderDocumentLine(string.Empty, string.Empty, 0m, 0m)] : order.Lines;
        return [.. lines.Select((line, index) => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["CompanyName"] = order.CompanyName, ["CompanyTaxNumber"] = order.CompanyTaxNumber, ["SupplierCode"] = order.SupplierCode, ["SupplierName"] = order.SupplierName,
            ["SupplierAddress"] = string.Join(", ", order.SupplierAddress ?? []), ["Number"] = order.OrderNumber, ["Date"] = order.OrderDate, ["Currency"] = order.Currency,
            ["PaymentTermsDays"] = order.PaymentTermsDays, ["Status"] = order.Status, ["LineNo"] = order.Lines.Count == 0 ? 0 : index + 1, ["Code"] = line.ItemCode, ["Description"] = line.Description,
            ["Quantity"] = line.Quantity, ["UnitPrice"] = line.UnitPrice, ["Amount"] = line.LineTotal, ["Total"] = order.Total,
            ["TermsText"] = $"Payment within {order.PaymentTermsDays.ToString(CultureInfo.InvariantCulture)} days",
        })];
    }
}
