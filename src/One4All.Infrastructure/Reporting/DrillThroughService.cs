using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Payroll;
using One4All.Application.Abstractions.Procurement;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Housing;
using One4All.Core.Payroll;
using One4All.Core.Procurement;
using One4All.Core.Receipting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Reporting;

/// <summary>
/// Renders the record behind a report cell as text (brief §9 drill-through): documents through the invoice and purchase order
/// document services where one exists, otherwise a summary straight from the registers.
/// </summary>
public sealed class DrillThroughService : IDrillThroughService
{
    private readonly One4AllDbContext _db;
    private readonly IInvoiceDocumentService? _invoices;
    private readonly IPurchaseOrderDocumentService? _orders;
    private readonly IPayrollService? _payroll;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="invoices">The invoice document service, when registered.</param>
    /// <param name="orders">The purchase order document service, when registered.</param>
    /// <param name="payroll">The payroll service (payslips), when registered.</param>
    public DrillThroughService(One4AllDbContext db, IInvoiceDocumentService? invoices = null, IPurchaseOrderDocumentService? orders = null, IPayrollService? payroll = null)
    {
        _db = db;
        _invoices = invoices;
        _orders = orders;
        _payroll = payroll;
    }

    /// <inheritdoc />
    public async Task<Result<string>> DescribeAsync(long companyId, string target, string key, CancellationToken ct = default)
    {
        string code = (key ?? string.Empty).Trim();
        if (code.Length == 0)
        {
            return Error.Validation("Drill.Key", "Nothing to drill into.");
        }

        string? text = (target ?? string.Empty).Trim() switch
        {
            "Document" => await DocumentAsync(companyId, code, ct).ConfigureAwait(false),
            "PurchaseOrder" => await PurchaseOrderAsync(companyId, code, ct).ConfigureAwait(false),
            "Grn" => await GrnAsync(companyId, code, ct).ConfigureAwait(false),
            "Receipt" => await ReceiptAsync(companyId, code, ct).ConfigureAwait(false),
            "Stand" => await StandAsync(companyId, code, ct).ConfigureAwait(false),
            "Payslip" => await PayslipAsync(companyId, code, ct).ConfigureAwait(false),
            "TaxCode" => await TaxCodeAsync(companyId, code, ct).ConfigureAwait(false),
            _ => null,
        };
        return text is null ? Error.NotFound("Drill.NotFound", $"{target} {code} was not found.") : text;
    }

    // An invoice, credit note, receipt, payment or bill by document number: the printed document where there is one, else the open item and what was allocated to it.
    private async Task<string?> DocumentAsync(long companyId, string number, CancellationToken ct)
    {
        OpenItem? item = await _db.OpenItems.AsNoTracking().Where(i => i.CompanyId == companyId && i.DocumentNumber == number).OrderByDescending(i => i.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (item is null)
        {
            return null;
        }

        if (_invoices is not null && item.ItemType is OpenItemType.Invoice or OpenItemType.CreditNote or OpenItemType.DebitNote)
        {
            Result<string> printed = await _invoices.RenderTextAsync(companyId, item.Id, ct: ct).ConfigureAwait(false);
            if (printed.IsSuccess)
            {
                return printed.Value;
            }
        }

        string trader = item.Ledger == TradingLedger.AccountsReceivable
            ? await _db.Customers.AsNoTracking().Where(c => c.Id == item.TraderId).Select(c => c.Code + "  " + c.Name).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? "?"
            : await _db.Suppliers.AsNoTracking().Where(s => s.Id == item.TraderId).Select(s => s.Code + "  " + s.Name).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? "?";
        StringBuilder text = new();
        text.AppendLine(CultureInfo.InvariantCulture, $"{item.ItemType} {item.DocumentNumber}   {item.DocumentDate:yyyy-MM-dd}   due {item.DueDate:yyyy-MM-dd}   {(item.Ledger == TradingLedger.AccountsReceivable ? "Customer" : "Supplier")} {trader}");
        text.AppendLine(CultureInfo.InvariantCulture, $"{item.Currency.Value} {item.OriginalTransaction:N2} ({item.Sign}); outstanding {item.OutstandingTransaction:N2}; base {item.OriginalBase:N2} at {item.ExchangeRate:0.####}");
        if (item.Narrative is string narrative)
        {
            text.AppendLine(narrative);
        }

        List<Allocation> allocations = await _db.Set<Allocation>().AsNoTracking().Where(a => a.CompanyId == companyId && (a.DebitItemId == item.Id || a.CreditItemId == item.Id)).OrderBy(a => a.AllocationDate).ThenBy(a => a.Id)
            .ToListAsync(ct).ConfigureAwait(false);
        if (allocations.Count > 0)
        {
            List<long> others = [.. allocations.Select(a => a.DebitItemId == item.Id ? a.CreditItemId : a.DebitItemId)];
            Dictionary<long, string> numbers = await _db.OpenItems.AsNoTracking().Where(i => others.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => $"{i.ItemType} {i.DocumentNumber}", ct).ConfigureAwait(false);
            text.AppendLine();
            text.AppendLine("Allocations");
            foreach (Allocation allocation in allocations)
            {
                long other = allocation.DebitItemId == item.Id ? allocation.CreditItemId : allocation.DebitItemId;
                text.AppendLine(CultureInfo.InvariantCulture, $"  {allocation.AllocationDate:yyyy-MM-dd}  {numbers.GetValueOrDefault(other, "?"),-28}{allocation.TransactionAmount,14:N2}");
            }
        }

        return text.ToString();
    }

    private async Task<string?> PurchaseOrderAsync(long companyId, string number, CancellationToken ct)
    {
        PurchaseOrder? order = await _db.PurchaseOrders.AsNoTracking().FirstOrDefaultAsync(o => o.CompanyId == companyId && o.OrderNumber == number, ct).ConfigureAwait(false);
        if (order is null)
        {
            return null;
        }

        if (_orders is not null)
        {
            Result<string> printed = await _orders.RenderTextAsync(companyId, order.Id, ct: ct).ConfigureAwait(false);
            if (printed.IsSuccess)
            {
                return printed.Value;
            }
        }

        string supplier = await _db.Suppliers.AsNoTracking().Where(s => s.Id == order.SupplierId).Select(s => s.Code + "  " + s.Name).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? "?";
        return $"Purchase order {order.OrderNumber}   {order.OrderDate:yyyy-MM-dd}   {supplier}   status {order.Status}   {order.Currency.Value}";
    }

    private async Task<string?> GrnAsync(long companyId, string number, CancellationToken ct)
    {
        GoodsReceivedNote? grn = await _db.GoodsReceivedNotes.AsNoTracking().Include(g => g.Lines).FirstOrDefaultAsync(g => g.CompanyId == companyId && g.GrnNumber == number, ct).ConfigureAwait(false);
        if (grn is null)
        {
            return null;
        }

        string supplier = await _db.Suppliers.AsNoTracking().Where(s => s.Id == grn.SupplierId).Select(s => s.Code + "  " + s.Name).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? "?";
        string? order = await _db.PurchaseOrders.AsNoTracking().Where(o => o.Id == grn.PurchaseOrderId).Select(o => o.OrderNumber).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        List<long> itemIds = [.. grn.Lines.Select(l => l.ItemId).Distinct()];
        Dictionary<long, string> items = await _db.Items.AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => $"{i.Code}  {i.Description}", ct).ConfigureAwait(false);
        StringBuilder text = new();
        text.AppendLine(CultureInfo.InvariantCulture, $"GRN {grn.GrnNumber}   supplier {supplier}   order {order ?? "?"}");
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"{"Item",-40}{"Quantity",14}");
        foreach (GrnLine line in grn.Lines)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"{items.GetValueOrDefault(line.ItemId, line.ItemId.ToString(CultureInfo.InvariantCulture)),-40}{line.Quantity,14:N3}");
        }

        return text.ToString();
    }

    // A cash-office receipt by number, else a sales-ledger receipt open item.
    private async Task<string?> ReceiptAsync(long companyId, string number, CancellationToken ct)
    {
        Receipt? receipt = await _db.Receipts.AsNoTracking().Include(r => r.Tenders).FirstOrDefaultAsync(r => r.CompanyId == companyId && r.ReceiptNumber == number, ct).ConfigureAwait(false);
        if (receipt is null)
        {
            OpenItem? item = await _db.OpenItems.AsNoTracking().FirstOrDefaultAsync(i => i.CompanyId == companyId && i.DocumentNumber == number && i.ItemType == OpenItemType.Receipt, ct).ConfigureAwait(false);
            return item is null ? null : await DocumentAsync(companyId, number, ct).ConfigureAwait(false);
        }

        string cashier = await _db.Users.AsNoTracking().Where(u => u.Id == receipt.CashierUserId).Select(u => u.Username).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? receipt.CashierUserId.ToString(CultureInfo.InvariantCulture);
        StringBuilder text = new();
        text.AppendLine(CultureInfo.InvariantCulture, $"Receipt {receipt.ReceiptNumber}   {receipt.Date:yyyy-MM-dd}   {receipt.PayerKind} {receipt.PayerAccountId?.ToString(CultureInfo.InvariantCulture) ?? ""}   cashier {cashier}   status {receipt.Status}");
        text.AppendLine();
        foreach (ReceiptTender tender in receipt.Tenders)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"  {tender.Type,-14}{tender.Currency.Value,-5}{tender.Amount,14:N2}  {tender.Reference}");
        }

        return text.ToString();
    }

    // The stand's register entry and chain of title.
    private async Task<string?> StandAsync(long companyId, string number, CancellationToken ct)
    {
        HousingStand? stand = await _db.HousingStands.AsNoTracking().Include(s => s.Ownership).FirstOrDefaultAsync(s => s.CompanyId == companyId && s.StandNumber == number, ct).ConfigureAwait(false);
        if (stand is null)
        {
            return null;
        }

        string? parcel = await _db.LandParcels.AsNoTracking().Where(p => p.Id == stand.LandParcelId).Select(p => p.ParcelNumber).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        string? scheme = stand.SchemeId is long schemeId ? await _db.HousingSchemes.AsNoTracking().Where(s => s.Id == schemeId).Select(s => s.Code).FirstOrDefaultAsync(ct).ConfigureAwait(false) : null;
        string holder = stand.CurrentHolderCustomerId is long holderId
            ? await _db.Customers.AsNoTracking().Where(c => c.Id == holderId).Select(c => c.Code + "  " + c.Name).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? "?" : "-";
        StringBuilder text = new();
        text.AppendLine(CultureInfo.InvariantCulture, $"Stand {stand.StandNumber}   parcel {parcel ?? "?"}   scheme {scheme ?? "-"}   {stand.SizeSquareMetres:N0} m²   status {stand.Status}   holder {holder}");
        text.AppendLine();
        text.AppendLine("Chain of title");
        foreach (OwnershipRecord record in stand.Ownership)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"  {record.Sequence,3}  {record.EffectiveDate:yyyy-MM-dd}  {record.ChangeType,-14}{record.HolderName,-30}{record.Consideration,14:N2}");
        }

        return text.ToString();
    }

    // "employee:period" → the payslip as printed.
    private async Task<string?> PayslipAsync(long companyId, string key, CancellationToken ct)
    {
        string[] parts = key.Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || _payroll is null)
        {
            return null;
        }

        string employeeNumber = parts[0].ToUpperInvariant(), period = parts[1];
        long? employeeId = await _db.Employees.AsNoTracking().Where(e => e.CompanyId == companyId && e.EmployeeNumber.ToUpper() == employeeNumber).Select(e => (long?)e.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (employeeId is not long employee)
        {
            return null;
        }

        Payslip? payslip = await _db.Payslips.AsNoTracking().Where(p => p.CompanyId == companyId && p.EmployeeId == employee && p.PeriodLabel == period).OrderByDescending(p => p.PayDate).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (payslip is null)
        {
            return null;
        }

        Result<string> text = await _payroll.RenderPayslipTextAsync(companyId, employee, period, payslip.PayDate, ct: ct).ConfigureAwait(false);
        return text.IsSuccess ? text.Value : $"Payslip {employeeNumber} {period}: gross {payslip.GrossEarnings:N2}, deductions {payslip.TotalDeductions:N2}, net {payslip.NetPay:N2} ({text.Error.Message})";
    }

    // The VAT drill-down: every posted line carrying the tax code in the last twelve months, by date, with totals.
    private async Task<string?> TaxCodeAsync(long companyId, string code, CancellationToken ct)
    {
        string taxCode = code.ToUpperInvariant();
        TaxCode? tax = await _db.TaxCodes.AsNoTracking().FirstOrDefaultAsync(t => t.CompanyId == companyId && t.Code == taxCode, ct).ConfigureAwait(false);
        if (tax is null)
        {
            return null;
        }

        DateOnly finish = DateOnly.FromDateTime(DateTime.Today), start = finish.AddMonths(-12).AddDays(1);
        var lines = await (from l in _db.Set<JournalLine>().AsNoTracking()
                           join j in _db.Journals.AsNoTracking() on l.JournalEntryId equals j.Id
                           where j.CompanyId == companyId && l.TaxCode == taxCode && (j.Status == JournalStatus.Posted || j.Status == JournalStatus.Reversed) && j.PostingDate >= start && j.PostingDate <= finish
                           orderby j.PostingDate, j.JournalNumber, l.Id
                           select new { j.PostingDate, j.JournalNumber, j.DocumentNumber, l.AccountCode, l.BaseDebit, l.BaseCredit, l.Narrative }).ToListAsync(ct).ConfigureAwait(false);
        StringBuilder text = new();
        text.AppendLine(CultureInfo.InvariantCulture, $"Tax code {tax.Code}  {tax.Name}  ({tax.Type}, output {tax.OutputAccountCode ?? "-"}, input {tax.InputAccountCode ?? "-"})   {start:yyyy-MM-dd} to {finish:yyyy-MM-dd}");
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"{"Date",-12}{"Journal",-20}{"Document",-16}{"Account",-10}{"Debit",14}{"Credit",14}  Narrative");
        foreach (var line in lines)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"{line.PostingDate:yyyy-MM-dd}  {line.JournalNumber,-20}{line.DocumentNumber ?? "-",-16}{line.AccountCode,-10}{line.BaseDebit,14:N2}{line.BaseCredit,14:N2}  {line.Narrative}");
        }

        text.AppendLine(CultureInfo.InvariantCulture, $"{string.Empty,-58}{lines.Sum(l => l.BaseDebit),14:N2}{lines.Sum(l => l.BaseCredit),14:N2}   {lines.Count} line(s)");
        return text.ToString();
    }
}
