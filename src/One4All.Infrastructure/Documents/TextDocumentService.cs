using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Budgeting;
using One4All.Application.Abstractions.Documents;
using One4All.Application.Abstractions.JobCosting;
using One4All.Application.Abstractions.Payroll;
using One4All.Application.Abstractions.Pos;
using One4All.Application.Abstractions.Procurement;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.SalesOrders;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Abstractions.Vouchers;
using One4All.Application.Documents;
using One4All.Application.Trading;
using One4All.Core.Common;
using One4All.Core.Trading;
using One4All.Core.Vouchers;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Documents;

/// <summary>
/// Every document of the §S list that the system renders as text, found by its number (and a date where the document needs one) and rendered through
/// the service that owns it: statements, reminders, remittances, payslips, vouchers, certificates, tickets, pick lists, RFQs, the chain of title, cheques
/// and shelf labels, as well as the business documents, tax invoices and purchase orders. The designable DOC reports take these lines as their rows, so
/// a company frames any of them with its own logo, header and footer in the report designer.
/// </summary>
public sealed class TextDocumentService : ITextDocumentService
{
    private readonly One4AllDbContext _db;
    private readonly IServiceProvider _services;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="services">The scoped services the documents are rendered by.</param>
    public TextDocumentService(One4AllDbContext db, IServiceProvider services)
    {
        _db = db;
        _services = services;
    }

    /// <inheritdoc />
    public async Task<Result<string>> RenderAsync(long companyId, string kind, string number, DateOnly? date = null, CancellationToken ct = default)
    {
        DocumentKind? found = DocumentKinds.Find(kind);
        if (found is null)
        {
            return Error.Validation("Doc.Kind", $"No document of kind {kind}; the kinds are {string.Join(", ", DocumentKinds.All.Select(k => k.Kind))}.");
        }

        string key = (number ?? string.Empty).Trim();
        if (key.Length == 0)
        {
            return Error.Validation("Doc.Number", $"{found.NumberLabel} is required for a {found.Name.ToLowerInvariant()}.");
        }

        DateOnly asAt = date ?? DateOnly.FromDateTime(DateTime.Today);
        switch (found.Kind)
        {
            case DocumentKinds.TaxInvoice:
            {
                long? item = await _db.OpenItems.AsNoTracking().Where(o => o.CompanyId == companyId && o.Ledger == TradingLedger.AccountsReceivable && o.DocumentNumber == key
                        && (o.ItemType == OpenItemType.Invoice || o.ItemType == OpenItemType.CreditNote)).Select(o => (long?)o.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
                return item is null ? Missing(found, key) : await Get<IInvoiceDocumentService>().RenderTextAsync(companyId, item.Value, ct: ct).ConfigureAwait(false);
            }

            case DocumentKinds.PurchaseOrder:
            {
                long? order = await _db.PurchaseOrders.AsNoTracking().Where(o => o.CompanyId == companyId && o.OrderNumber == key).Select(o => (long?)o.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
                return order is null ? Missing(found, key) : await Get<IPurchaseOrderDocumentService>().RenderTextAsync(companyId, order.Value, ct: ct).ConfigureAwait(false);
            }

            case DocumentKinds.PosReceipt:
            {
                long? sale = await _db.Sales.AsNoTracking().Where(s => s.CompanyId == companyId && s.SaleNumber == key).Select(s => (long?)s.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
                return sale is null ? Missing(found, key) : await Get<IPosReceiptService>().RenderReceiptTextAsync(companyId, sale.Value, ct: ct).ConfigureAwait(false);
            }

            case DocumentKinds.CustomerStatement:
            {
                long? customer = await CustomerAsync(companyId, key, ct).ConfigureAwait(false);
                return customer is null ? Missing(found, key) : await Get<IStatementService>().RenderCustomerStatementTextAsync(companyId, customer.Value, asAt, ct: ct).ConfigureAwait(false);
            }

            case DocumentKinds.BalanceForwardStatement:
            {
                long? customer = await CustomerAsync(companyId, key, ct).ConfigureAwait(false);
                return customer is null ? Missing(found, key)
                    : await Get<IStatementService>().RenderCustomerBalanceForwardTextAsync(companyId, customer.Value, new DateOnly(asAt.Year, asAt.Month, 1), asAt, ct: ct).ConfigureAwait(false);
            }

            case DocumentKinds.SupplierStatement:
            {
                long? supplier = await _db.Suppliers.AsNoTracking().Where(s => s.CompanyId == companyId && s.Code == key).Select(s => (long?)s.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
                return supplier is null ? Missing(found, key) : await Get<IStatementService>().RenderSupplierStatementTextAsync(companyId, supplier.Value, asAt, ct: ct).ConfigureAwait(false);
            }

            case DocumentKinds.DunningLetter:
            {
                long? customer = await CustomerAsync(companyId, key, ct).ConfigureAwait(false);
                return customer is null ? Missing(found, key) : await Get<IDunningService>().RenderReminderTextAsync(companyId, customer.Value, asAt, DunningStages.Default, ct: ct).ConfigureAwait(false);
            }

            case DocumentKinds.RemittanceAdvice:
            {
                long? payment = await _db.OpenItems.AsNoTracking().Where(o => o.CompanyId == companyId && o.Ledger == TradingLedger.AccountsPayable && o.DocumentNumber == key)
                    .Select(o => (long?)o.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
                return payment is null ? Missing(found, key) : await Get<IRemittanceService>().RenderRemittanceTextAsync(companyId, payment.Value, ct: ct).ConfigureAwait(false);
            }

            case DocumentKinds.Payslip:
            {
                var slip = await (from p in _db.Payslips.AsNoTracking()
                                  join e in _db.Employees.AsNoTracking() on p.EmployeeId equals e.Id
                                  where p.CompanyId == companyId && e.EmployeeNumber == key && (date == null || p.PayDate == date)
                                  orderby p.PayDate descending
                                  select new { p.EmployeeId, p.PeriodLabel, p.PayDate }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
                return slip is null ? Missing(found, key) : await Get<IPayrollService>().RenderPayslipTextAsync(companyId, slip.EmployeeId, slip.PeriodLabel, slip.PayDate, ct: ct).ConfigureAwait(false);
            }

            case DocumentKinds.PaymentVoucher:
            case DocumentKinds.ReceiptVoucher:
            case DocumentKinds.JournalVoucher:
            case DocumentKinds.PettyCashVoucher:
            {
                AccountingVoucherType type = found.Kind switch
                {
                    DocumentKinds.PaymentVoucher => AccountingVoucherType.Payment,
                    DocumentKinds.ReceiptVoucher => AccountingVoucherType.Receipt,
                    DocumentKinds.JournalVoucher => AccountingVoucherType.Journal,
                    _ => AccountingVoucherType.PettyCash,
                };
                long? voucher = await _db.Set<AccountingVoucher>().AsNoTracking().Where(v => v.CompanyId == companyId && v.Number == key && v.Type == type).Select(v => (long?)v.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
                return voucher is null ? Missing(found, key) : await Get<IAccountingVoucherService>().RenderAsync(companyId, voucher.Value, ct).ConfigureAwait(false);
            }

            case DocumentKinds.WithholdingCertificate:
            {
                long? certificate = await _db.Set<WithholdingCertificate>().AsNoTracking().Where(c => c.CompanyId == companyId && c.CertificateNumber == key).Select(c => (long?)c.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
                return certificate is null ? Missing(found, key) : await Get<IWithholdingTaxService>().CertificateTextAsync(companyId, certificate.Value, ct).ConfigureAwait(false);
            }

            case DocumentKinds.WeighbridgeTicket:
                return await Get<IWeighbridgeService>().RenderTicketAsync(companyId, key, ct).ConfigureAwait(false);

            case DocumentKinds.PickList:
            {
                string[] parts = key.Split('/', 2, StringSplitOptions.TrimEntries);
                long? order = await _db.SalesOrders.AsNoTracking().Where(o => o.CompanyId == companyId && o.OrderNumber == parts[0]).Select(o => (long?)o.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
                if (order is null)
                {
                    return Missing(found, parts[0]);
                }

                IQueryable<Core.Inventory.Warehouse> warehouses = _db.Warehouses.AsNoTracking().Where(w => w.CompanyId == companyId);
                long? warehouse = parts.Length == 2
                    ? await warehouses.Where(w => w.Code == parts[1]).Select(w => (long?)w.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false)
                    : await warehouses.OrderBy(w => w.Code).Select(w => (long?)w.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
                return warehouse is null ? Error.NotFound("Doc.WarehouseNotFound", parts.Length == 2 ? $"No warehouse {parts[1]}." : "No warehouse to pick from.")
                    : await Get<IPickListService>().RenderTextAsync(companyId, order.Value, warehouse.Value, ct: ct).ConfigureAwait(false);
            }

            case DocumentKinds.Rfq:
            {
                long? rfq = await _db.Set<Core.Procurement.Rfq>().AsNoTracking().Where(r => r.CompanyId == companyId && r.RfqNumber == key).Select(r => (long?)r.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
                return rfq is null ? Missing(found, key) : await Get<IRfqService>().RenderAsync(companyId, rfq.Value, ct).ConfigureAwait(false);
            }

            case DocumentKinds.CertificateOfService:
                return await Get<ITerminationService>().CertificateOfServiceAsync(companyId, key, ct).ConfigureAwait(false);

            case DocumentKinds.BudgetHolderStatement:
                return await Get<IBudgetNotificationService>().HolderStatementAsync(companyId, key, asAt, ct).ConfigureAwait(false);

            case DocumentKinds.ChainOfTitle:
                return await Get<IDrillThroughService>().DescribeAsync(companyId, "Stand", key, ct).ConfigureAwait(false);

            case DocumentKinds.Cheque:
            {
                string[] parts = key.Split('/', 2, StringSplitOptions.TrimEntries);
                return parts.Length == 2 ? await Get<IDocumentLayoutService>().ChequeAsync(companyId, parts[0], parts[1], ct).ConfigureAwait(false)
                    : Error.Validation("Doc.Cheque", "Give the cheque as bank account code/cheque number, e.g. FNB-MAIN/000123.");
            }

            case DocumentKinds.ClearanceCertificate:
                return await ClearanceCertificateTextAsync(companyId, found, key, ct).ConfigureAwait(false);

            case DocumentKinds.ShelfLabels:
                return await Get<IDocumentLayoutService>().ShelfLabelsAsync(companyId, key, ct: ct).ConfigureAwait(false);

            default:
            {
                Result<BusinessDocument> built = await Get<IDocumentLayoutService>().BuildAsync(companyId, found.Kind, key, date, ct).ConfigureAwait(false);
                return built.IsSuccess ? BusinessDocumentFormatter.ToPlainText(built.Value) : built.Error;
            }
        }
    }

    // The rates clearance certificate a conveyancer lodges with a transfer (brief §14 report 153; §4.9 clearance).
    private async Task<Result<string>> ClearanceCertificateTextAsync(long companyId, DocumentKind kind, string number, CancellationToken ct)
    {
        var certificate = await (from c in _db.Set<Core.Municipal.ClearanceCertificate>().AsNoTracking()
                                 join p in _db.Properties.AsNoTracking() on c.PropertyId equals p.Id
                                 where c.CompanyId == companyId && c.CertificateNumber == number
                                 select new { c.CertificateNumber, c.IssuedOn, c.ValidUntil, c.AdvanceHeld, c.IssuedByUserId, p.StandNumber, p.Ward, p.RatepayerCustomerId })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (certificate is null)
        {
            return Missing(kind, number);
        }

        string council = await _db.Companies.AsNoTracking().Where(c => c.Id == companyId).Select(c => c.Name).FirstAsync(ct).ConfigureAwait(false);
        string owner = await _db.Customers.AsNoTracking().Where(c => c.Id == certificate.RatepayerCustomerId).Select(c => c.Name + " (" + c.Code + ")").FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? "-";
        string issuer = await _db.Users.AsNoTracking().Where(u => u.Id == certificate.IssuedByUserId).Select(u => u.DisplayName).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            ?? $"user {certificate.IssuedByUserId}";
        System.Globalization.CultureInfo inv = System.Globalization.CultureInfo.InvariantCulture;
        System.Text.StringBuilder text = new();
        text.AppendLine(council.ToUpperInvariant());
        text.AppendLine("RATES CLEARANCE CERTIFICATE");
        text.AppendLine(new string('=', 64));
        text.AppendLine(inv, $"Certificate: {certificate.CertificateNumber}");
        text.AppendLine(inv, $"Issued:      {certificate.IssuedOn:yyyy-MM-dd}");
        text.AppendLine(inv, $"Property:    stand {certificate.StandNumber}{(string.IsNullOrWhiteSpace(certificate.Ward) ? string.Empty : ", " + certificate.Ward)}");
        text.AppendLine(inv, $"Ratepayer:   {owner}");
        text.AppendLine();
        text.AppendLine("This certifies that all rates, service charges and other amounts due");
        text.AppendLine(inv, $"to the council on the property above have been paid up to and including {certificate.ValidUntil:yyyy-MM-dd}.");
        if (certificate.AdvanceHeld > 0m)
        {
            text.AppendLine(inv, $"An advance of {certificate.AdvanceHeld:N2} is held to cover charges to that date.");
        }

        text.AppendLine(inv, $"This certificate is valid until {certificate.ValidUntil:yyyy-MM-dd} for the transfer of the property.");
        text.AppendLine();
        text.AppendLine(inv, $"Issued by: {issuer}");
        text.AppendLine();
        text.AppendLine("Signature: ____________________________   Date: ______________");
        text.AppendLine("Official stamp:");
        return text.ToString();
    }

    private static Error Missing(DocumentKind kind, string key) => Error.NotFound("Doc.NotFound", $"No {kind.Name.ToLowerInvariant()} {key} ({kind.NumberLabel}).");

    private T Get<T>() where T : notnull => _services.GetRequiredService<T>();

    private Task<long?> CustomerAsync(long companyId, string code, CancellationToken ct)
        => _db.Customers.AsNoTracking().Where(c => c.CompanyId == companyId && c.Code == code).Select(c => (long?)c.Id).FirstOrDefaultAsync(ct);
}
