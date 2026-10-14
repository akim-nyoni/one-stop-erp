using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Reporting;

/// <summary>Finds and describes the documents behind a journal: sub-ledger items, till sales, counter receipts and purchase or sales orders.</summary>
public sealed class SourceDocumentService : ISourceDocumentService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public SourceDocumentService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<string> DescribeAsync(long companyId, string journalNumber, CancellationToken ct = default)
    {
        var journal = await _db.Journals.AsNoTracking().Where(j => j.CompanyId == companyId && j.JournalNumber == journalNumber)
            .Select(j => new { j.Id, j.DocumentType, j.DocumentNumber, j.SourceModule }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (journal is null)
        {
            return $"Journal {journalNumber} was not found.";
        }

        List<string> blocks = [];
        string? number = string.IsNullOrWhiteSpace(journal.DocumentNumber) ? null : journal.DocumentNumber.Trim();

        // Customer and supplier items the journal raised.
        var items = await (from i in _db.OpenItems.AsNoTracking()
                           where i.CompanyId == companyId && i.JournalEntryId == journal.Id
                           select new { i.Ledger, i.TraderId, i.ItemType, i.DocumentNumber, i.DocumentDate, i.DueDate, i.Currency, i.OriginalTransaction, i.OutstandingTransaction, i.Narrative })
            .ToListAsync(ct).ConfigureAwait(false);
        foreach (var item in items)
        {
            bool customer = item.Ledger == TradingLedger.AccountsReceivable;
            var trader = customer
                ? await _db.Customers.AsNoTracking().Where(c => c.Id == item.TraderId).Select(c => new { c.Code, c.Name }).FirstOrDefaultAsync(ct).ConfigureAwait(false)
                : await _db.Suppliers.AsNoTracking().Where(s => s.Id == item.TraderId).Select(s => new { s.Code, s.Name }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            blocks.Add($"{(customer ? "Customer" : "Supplier")} {item.ItemType.ToString().ToLowerInvariant()} {item.DocumentNumber} — {trader?.Code} {trader?.Name}" + Environment.NewLine
                + $"  dated {D(item.DocumentDate)}, due {D(item.DueDate)}; {item.Currency.Value} {M(item.OriginalTransaction)}, {M(item.OutstandingTransaction)} still open"
                + (string.IsNullOrWhiteSpace(item.Narrative) ? string.Empty : $"; {item.Narrative}"));
        }

        // The till sale the journal posted.
        var sale = await _db.Sales.AsNoTracking().Include(s => s.Lines).Where(s => s.CompanyId == companyId && (s.JournalEntryId == journal.Id || (number != null && s.SaleNumber == number)))
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (sale is not null)
        {
            blocks.Add($"Till sale {sale.SaleNumber} on {D(sale.SaleDate)} ({sale.Status}), {M(sale.TotalGross)} including tax {M(sale.TotalTax)}"
                + (sale.FiscalCode is null ? string.Empty : $"; fiscal code {sale.FiscalCode}") + Environment.NewLine
                + string.Join(Environment.NewLine, sale.Lines.Select(l => $"  {Q(l.Quantity)} × {l.Description} @ {M(l.UnitPrice)} = {M(l.LineNet + l.TaxAmount)}")));
        }

        // The counter receipt.
        var receipt = await _db.Receipts.AsNoTracking().Include(r => r.Lines).Where(r => r.CompanyId == companyId && (r.JournalNumber == journalNumber || (number != null && r.ReceiptNumber == number)))
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (receipt is not null)
        {
            blocks.Add($"Receipt {receipt.ReceiptNumber} on {D(receipt.Date)} ({receipt.Status}) from a {receipt.PayerKind.ToString().ToLowerInvariant()} payer, {receipt.Currency.Value} {M(receipt.LineTotal)}"
                + Environment.NewLine + string.Join(Environment.NewLine, receipt.Lines.Select(l => $"  {l.Type}: {M(l.Amount)} {l.Service ?? l.AccountCode ?? string.Empty} {l.Narrative ?? string.Empty}".TrimEnd())));
        }

        // The purchase or sales order named on the journal.
        if (number is not null)
        {
            var order = await _db.PurchaseOrders.AsNoTracking().Include(o => o.Lines).Where(o => o.CompanyId == companyId && o.OrderNumber == number).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (order is not null)
            {
                string supplier = await _db.Suppliers.AsNoTracking().Where(s => s.Id == order.SupplierId).Select(s => s.Code + " " + s.Name).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? "?";
                blocks.Add($"Purchase order {order.OrderNumber} to {supplier} on {D(order.OrderDate)} ({order.Status}), {M(order.TotalNet)}" + Environment.NewLine
                    + string.Join(Environment.NewLine, order.Lines.Select(l => $"  {Q(l.OrderedQuantity)} × {l.Description} @ {M(l.UnitPrice)}; received {Q(l.ReceivedQuantity)}, invoiced {Q(l.InvoicedQuantity)}")));
            }

            var salesOrder = await _db.Set<Core.SalesOrders.SalesOrder>().AsNoTracking().Include(o => o.Lines).Where(o => o.CompanyId == companyId && o.OrderNumber == number)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (salesOrder is not null)
            {
                string customer = await _db.Customers.AsNoTracking().Where(c => c.Id == salesOrder.CustomerId).Select(c => c.Code + " " + c.Name).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? "?";
                blocks.Add($"Sales order {salesOrder.OrderNumber} for {customer} on {D(salesOrder.OrderDate)} ({salesOrder.Status})" + Environment.NewLine
                    + string.Join(Environment.NewLine, salesOrder.Lines.Select(l => $"  {Q(l.OrderedQuantity)} × {l.Description} @ {M(l.UnitPrice)}; despatched {Q(l.DespatchedQuantity)}, invoiced {Q(l.InvoicedQuantity)}")));
            }
        }

        return blocks.Count > 0
            ? string.Join(Environment.NewLine, blocks)
            : $"No originating document is held for {journal.SourceModule} {journal.DocumentType} {number}".TrimEnd() + " — the journal is its own source.";
    }

    private static string D(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string M(decimal amount) => amount.ToString("N2", CultureInfo.InvariantCulture);

    private static string Q(decimal quantity) => quantity.ToString("0.###", CultureInfo.InvariantCulture);
}
