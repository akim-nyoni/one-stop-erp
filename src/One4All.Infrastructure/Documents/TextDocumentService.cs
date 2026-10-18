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

            case DocumentKinds.StockTransferNote:
                return await StockTransferNoteAsync(companyId, found, key, ct).ConfigureAwait(false);

            case DocumentKinds.AssetTag:
                return await AssetTagAsync(companyId, found, key, ct).ConfigureAwait(false);

            case DocumentKinds.LeaveApplication:
                return await LeaveApplicationAsync(companyId, found, key, ct).ConfigureAwait(false);

            case DocumentKinds.LeaveBalanceCertificate:
                return await LeaveBalanceCertificateAsync(companyId, found, key, asAt, ct).ConfigureAwait(false);

            case DocumentKinds.LoanStatement:
                return await LoanStatementAsync(companyId, found, key, ct).ConfigureAwait(false);

            case DocumentKinds.GiftVoucher:
                return await GiftVoucherAsync(companyId, found, key, ct).ConfigureAwait(false);

            case DocumentKinds.EftAdvice:
                return await EftAdviceAsync(companyId, found, key, ct).ConfigureAwait(false);

            case DocumentKinds.SubcontractCertificate:
                return await SubcontractCertificateAsync(companyId, found, key, ct).ConfigureAwait(false);

            case DocumentKinds.LicenceCertificate:
                return await LicenceCertificateAsync(companyId, found, key, ct).ConfigureAwait(false);

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

    private Task<string> CompanyNameAsync(long companyId, CancellationToken ct)
        => _db.Companies.AsNoTracking().Where(c => c.Id == companyId).Select(c => c.Name).FirstAsync(ct);

    private static string Heading(string company, string title)
    {
        System.Text.StringBuilder text = new();
        text.AppendLine(company.ToUpperInvariant());
        text.AppendLine(title);
        text.AppendLine(new string('=', 64));
        return text.ToString();
    }

    private static string D(DateOnly date) => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static string N(decimal amount) => amount.ToString("N2", System.Globalization.CultureInfo.InvariantCulture);

    // DOC34: the note that travels with stock moved between warehouses (brief §S "stock transfer note").
    private async Task<Result<string>> StockTransferNoteAsync(long companyId, DocumentKind kind, string number, CancellationToken ct)
    {
        Core.Inventory.StockTransfer? transfer = await _db.StockTransfers.AsNoTracking().FirstOrDefaultAsync(t => t.CompanyId == companyId && t.TransferNumber == number, ct).ConfigureAwait(false);
        if (transfer is null)
        {
            return Missing(kind, number);
        }

        Dictionary<long, string> warehouses = await _db.Warehouses.AsNoTracking().Where(w => w.CompanyId == companyId).ToDictionaryAsync(w => w.Id, w => w.Code + " " + w.Name, ct).ConfigureAwait(false);
        var item = await _db.Items.AsNoTracking().Where(i => i.Id == transfer.ItemId).Select(i => new { i.Code, i.Description }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        System.Text.StringBuilder text = new(Heading(await CompanyNameAsync(companyId, ct).ConfigureAwait(false), "STOCK TRANSFER NOTE"));
        text.AppendLine($"Transfer:   {transfer.TransferNumber}   Dispatched: {D(transfer.DispatchedOn)}");
        text.AppendLine($"From:       {warehouses.GetValueOrDefault(transfer.FromWarehouseId, "?")}");
        text.AppendLine($"Via:        {warehouses.GetValueOrDefault(transfer.TransitWarehouseId, "?")} (in transit)");
        text.AppendLine($"To:         {warehouses.GetValueOrDefault(transfer.ToWarehouseId, "?")}");
        text.AppendLine();
        text.AppendLine($"Item:       {item?.Code} {item?.Description}");
        text.AppendLine($"Quantity:   {transfer.Quantity.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}   Value at cost: {N(transfer.Value)}");
        text.AppendLine(transfer.ReceivedOn is DateOnly received
            ? $"Received:   {D(received)}, quantity {transfer.ReceivedQuantity?.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}"
            : "Received:   (not yet)");
        text.AppendLine();
        text.AppendLine("Dispatched by: ____________________   Driver: ____________________");
        text.AppendLine("Received by:   ____________________   Date:   ____________________");
        return text.ToString();
    }

    // DOC35: a fixed asset's tag (brief §S "asset tag"), with a barcode of the asset number for counts.
    private async Task<Result<string>> AssetTagAsync(long companyId, DocumentKind kind, string number, CancellationToken ct)
    {
        Core.FixedAssets.FixedAsset? asset = await _db.FixedAssets.AsNoTracking().FirstOrDefaultAsync(a => a.CompanyId == companyId && a.AssetNumber == number, ct).ConfigureAwait(false);
        if (asset is null)
        {
            return Missing(kind, number);
        }

        string company = await CompanyNameAsync(companyId, ct).ConfigureAwait(false);
        System.Text.StringBuilder text = new();
        text.AppendLine("+" + new string('-', 38) + "+");
        text.AppendLine($"| {Fit(company.ToUpperInvariant(), 36)} |");
        text.AppendLine($"| {Fit("PROPERTY OF THE ABOVE - DO NOT REMOVE", 36)} |");
        text.AppendLine($"| {Fit("Asset: " + asset.AssetNumber, 36)} |");
        text.AppendLine($"| {Fit(asset.Description, 36)} |");
        text.AppendLine($"| {Fit("Class: " + asset.AssetClass, 36)} |");
        text.AppendLine($"| {Fit("Tag: " + (asset.TagNumber ?? asset.AssetNumber) + "  Acquired: " + D(asset.AcquisitionDate), 36)} |");
        text.AppendLine($"| {Fit("*" + (asset.TagNumber ?? asset.AssetNumber) + "*", 36)} |");
        text.AppendLine("+" + new string('-', 38) + "+");
        return text.ToString();
    }

    private static string Fit(string value, int width) => value.Length > width ? value[..width] : value.PadRight(width);

    // DOC36: a leave application form, keyed by employee number and the first day of leave (brief §S "leave application").
    private async Task<Result<string>> LeaveApplicationAsync(long companyId, DocumentKind kind, string key, CancellationToken ct)
    {
        string[] parts = key.Split('/', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !DateOnly.TryParseExact(parts[1], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateOnly from))
        {
            return Error.Validation("Doc.LeaveKey", "Give the leave application as employee number/start date, e.g. E001/2026-07-01.");
        }

        var employee = await _db.Employees.AsNoTracking().Where(e => e.CompanyId == companyId && e.EmployeeNumber == parts[0]).Select(e => new { e.Id, e.EmployeeNumber, e.FullName }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        Core.Payroll.LeaveApplication? leave = employee is null ? null
            : await _db.LeaveApplications.AsNoTracking().Where(l => l.CompanyId == companyId && l.EmployeeId == employee.Id && l.FromDate == from).OrderByDescending(l => l.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (employee is null || leave is null)
        {
            return Missing(kind, key);
        }

        string balanceType = leave.LeaveType.Trim().ToUpperInvariant(); // balances keep the type in upper case
        decimal balance = await _db.EmployeeLeaveBalances.AsNoTracking().Where(b => b.CompanyId == companyId && b.EmployeeId == employee.Id && b.LeaveType == balanceType)
            .Select(b => b.AccruedDays - b.TakenDays).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        string decidedBy = leave.DecidedByUserId is long user ? await _db.Users.AsNoTracking().Where(u => u.Id == user).Select(u => u.DisplayName).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? $"user {user}" : "-";
        System.Text.StringBuilder text = new(Heading(await CompanyNameAsync(companyId, ct).ConfigureAwait(false), "LEAVE APPLICATION"));
        text.AppendLine($"Employee:   {employee.EmployeeNumber} {employee.FullName}");
        text.AppendLine($"Leave type: {leave.LeaveType}");
        text.AppendLine($"From:       {D(leave.FromDate)}   To: {D(leave.ToDate)}   Days: {leave.Days.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}");
        text.AppendLine($"Balance:    {balance.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)} day(s) of {leave.LeaveType} leave available");
        if (!string.IsNullOrWhiteSpace(leave.Note))
        {
            text.AppendLine($"Note:       {leave.Note}");
        }

        text.AppendLine($"Status:     {leave.Status}{(leave.DecidedByUserId is null ? string.Empty : " by " + decidedBy)}");
        text.AppendLine();
        text.AppendLine("Employee signature:   ____________________   Date: ____________");
        text.AppendLine("Supervisor approval:  ____________________   Date: ____________");
        return text.ToString();
    }

    // DOC37: what leave an employee has at a date, per leave type (brief §S "leave balance certificate").
    private async Task<Result<string>> LeaveBalanceCertificateAsync(long companyId, DocumentKind kind, string number, DateOnly asAt, CancellationToken ct)
    {
        var employee = await _db.Employees.AsNoTracking().Where(e => e.CompanyId == companyId && e.EmployeeNumber == number).Select(e => new { e.Id, e.EmployeeNumber, e.FullName }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (employee is null)
        {
            return Missing(kind, number);
        }

        var balances = await _db.EmployeeLeaveBalances.AsNoTracking().Where(b => b.CompanyId == companyId && b.EmployeeId == employee.Id).OrderBy(b => b.LeaveType)
            .Select(b => new { b.LeaveType, b.AccruedDays, b.TakenDays }).ToListAsync(ct).ConfigureAwait(false);
        System.Text.StringBuilder text = new(Heading(await CompanyNameAsync(companyId, ct).ConfigureAwait(false), "LEAVE BALANCE CERTIFICATE"));
        text.AppendLine($"Employee:   {employee.EmployeeNumber} {employee.FullName}");
        text.AppendLine($"As at:      {D(asAt)}");
        text.AppendLine();
        text.AppendLine($"{"Leave type",-20}{"Accrued",12}{"Taken",12}{"Balance",12}");
        foreach (var b in balances)
        {
            text.AppendLine($"{b.LeaveType,-20}{b.AccruedDays,12:0.##}{b.TakenDays,12:0.##}{b.AccruedDays - b.TakenDays,12:0.##}");
        }

        if (balances.Count == 0)
        {
            text.AppendLine("No leave balances are kept for this employee.");
        }

        text.AppendLine();
        text.AppendLine("Certified by (HR): ____________________   Date: ____________");
        return text.ToString();
    }

    // DOC38: an employee loan's terms, its balance and the repayments still to come (brief §S "loan agreement and statement").
    private async Task<Result<string>> LoanStatementAsync(long companyId, DocumentKind kind, string reference, CancellationToken ct)
    {
        Core.Payroll.EmployeeLoan? loan = await _db.EmployeeLoans.AsNoTracking().FirstOrDefaultAsync(l => l.CompanyId == companyId && l.Reference == reference, ct).ConfigureAwait(false);
        if (loan is null)
        {
            return Missing(kind, reference);
        }

        var employee = await _db.Employees.AsNoTracking().Where(e => e.Id == loan.EmployeeId).Select(e => new { e.EmployeeNumber, e.FullName }).FirstAsync(ct).ConfigureAwait(false);
        System.Text.StringBuilder text = new(Heading(await CompanyNameAsync(companyId, ct).ConfigureAwait(false), "EMPLOYEE LOAN AGREEMENT AND STATEMENT"));
        text.AppendLine($"Loan:        {loan.Reference}   Granted: {D(loan.GrantedOn)}   Status: {loan.Status}{(loan.Suspended ? " (repayments suspended)" : string.Empty)}");
        text.AppendLine($"Employee:    {employee.EmployeeNumber} {employee.FullName}");
        text.AppendLine($"Principal:   {N(loan.Principal)}   Instalment: {N(loan.Installment)}   Interest: {loan.InterestRatePercent.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}% a year");
        text.AppendLine($"Repaid:      {(loan.DeductFromPay ? "by deduction from pay each month" : "directly")}");
        text.AppendLine($"Outstanding: {N(loan.Outstanding)}");
        text.AppendLine();
        text.AppendLine("Repayments still to come (monthly):");
        text.AppendLine($"{"No",4}{"Interest",14}{"Repayment",14}{"Balance after",16}");
        decimal outstanding = loan.Outstanding;
        for (int n = 1; n <= 120 && outstanding > 0m && loan.Installment > 0m; n++)
        {
            (decimal interest, decimal due) = Core.Payroll.EmployeeLoan.Instalment(outstanding, loan.Installment, loan.InterestRatePercent, 12);
            outstanding = decimal.Round(outstanding + interest - due, 2, MidpointRounding.ToEven);
            text.AppendLine($"{n,4}{N(interest),14}{N(due),14}{N(Math.Max(0m, outstanding)),16}");
        }

        text.AppendLine();
        text.AppendLine("I agree to repay this loan on the terms above.");
        text.AppendLine("Employee: ____________________   Employer: ____________________   Date: ____________");
        return text.ToString();
    }

    // DOC39: a gift voucher as handed to the customer (brief §S "gift voucher").
    private async Task<Result<string>> GiftVoucherAsync(long companyId, DocumentKind kind, string number, CancellationToken ct)
    {
        string cardNumber = number.ToUpperInvariant(); // card numbers are kept in upper case
        Core.Vouchers.GiftCard? card = await _db.GiftCards.AsNoTracking().FirstOrDefaultAsync(c => c.CompanyId == companyId && c.CardNumber == cardNumber, ct).ConfigureAwait(false);
        if (card is null)
        {
            return Missing(kind, number);
        }

        string company = await CompanyNameAsync(companyId, ct).ConfigureAwait(false);
        System.Text.StringBuilder text = new();
        text.AppendLine("+" + new string('=', 46) + "+");
        text.AppendLine($"| {Fit(company.ToUpperInvariant(), 44)} |");
        text.AppendLine($"| {Fit("GIFT VOUCHER", 44)} |");
        text.AppendLine($"| {Fit(string.Empty, 44)} |");
        text.AppendLine($"| {Fit($"Value:   {card.Currency} {N(card.OriginalValue)}", 44)} |");
        text.AppendLine($"| {Fit($"Balance: {card.Currency} {N(card.Balance)}", 44)} |");
        text.AppendLine($"| {Fit($"Card:    {card.CardNumber}", 44)} |");
        text.AppendLine($"| {Fit($"Issued:  {D(card.IssuedDate)}", 44)} |");
        text.AppendLine($"| {Fit(card.ExpiryDate is DateOnly expires ? $"Valid until {D(expires)}" : "No expiry date", 44)} |");
        text.AppendLine($"| {Fit($"Status:  {card.Status}", 44)} |");
        text.AppendLine($"| {Fit("*" + card.CardNumber + "*", 44)} |");
        text.AppendLine($"| {Fit("Redeemable at any till. Not exchangeable for cash.", 44)} |");
        text.AppendLine("+" + new string('=', 46) + "+");
        return text.ToString();
    }

    // DOC40: the advice that goes to the bank with a payment run's EFT file (brief §S "EFT advice"): each included supplier, the account paid into and the amount.
    private async Task<Result<string>> EftAdviceAsync(long companyId, DocumentKind kind, string number, CancellationToken ct)
    {
        Core.Trading.PaymentRunBatch? run = await _db.Set<Core.Trading.PaymentRunBatch>().AsNoTracking().Include(b => b.Lines)
            .FirstOrDefaultAsync(b => b.CompanyId == companyId && b.Number == number, ct).ConfigureAwait(false);
        if (run is null)
        {
            return Missing(kind, number);
        }

        List<Core.Trading.PaymentRunBatchLine> lines = [.. run.Lines.Where(l => l.Included).OrderBy(l => l.SupplierCode, StringComparer.Ordinal)];
        List<long> supplierIds = [.. lines.Select(l => l.SupplierId).Distinct()];
        Dictionary<long, Core.Trading.Supplier> suppliers = await _db.Suppliers.AsNoTracking().Where(s => supplierIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct).ConfigureAwait(false);
        System.Text.StringBuilder text = new(Heading(await CompanyNameAsync(companyId, ct).ConfigureAwait(false), "EFT PAYMENT ADVICE"));
        text.AppendLine($"Payment run: {run.Number}   Payment date: {D(run.PaymentDate)}   Status: {run.Status}");
        text.AppendLine($"Paid from:   {run.BankAccountCode}   Method: {run.Method}{(string.IsNullOrWhiteSpace(run.EftLayoutCode) ? string.Empty : "   File layout: " + run.EftLayoutCode)}");
        text.AppendLine();
        text.AppendLine($"{"Supplier",-26}{"Bank",-16}{"Account",-18}{"Cur",4}{"Amount",14}");
        foreach (Core.Trading.PaymentRunBatchLine line in lines)
        {
            Core.Trading.Supplier? supplier = suppliers.GetValueOrDefault(line.SupplierId);
            text.AppendLine($"{Fit(line.SupplierCode + " " + (supplier?.Name ?? string.Empty), 25)} {Fit(supplier?.BankName ?? "-", 15)} {Fit(supplier?.BankAccountNumber ?? "(no account)", 17)} {line.Currency,4}{N(line.Amount),14}");
        }

        foreach (var total in lines.GroupBy(l => l.Currency).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            text.AppendLine($"{"Total " + total.Key + $" ({total.Count()} payment(s))",-64}{N(total.Sum(l => l.Amount)),14}");
        }

        if (lines.Count == 0)
        {
            text.AppendLine("No payments are included in this run.");
        }

        text.AppendLine();
        text.AppendLine("Authorised signatory 1: ____________________   Authorised signatory 2: ____________________");
        return text.ToString();
    }

    // DOC41: a subcontractor's payment certificate (brief §S "subcontractor certificate"): work done to date, previously certified, retention, advance recovery and tax withheld.
    private async Task<Result<string>> SubcontractCertificateAsync(long companyId, DocumentKind kind, string number, CancellationToken ct)
    {
        Core.JobCosting.Subcontract? subcontract = await _db.Subcontracts.AsNoTracking().Include(s => s.Certificates)
            .FirstOrDefaultAsync(s => s.CompanyId == companyId && s.Certificates.Any(c => c.Number == number), ct).ConfigureAwait(false);
        Core.JobCosting.SubcontractCertificate? certificate = subcontract?.Certificates.FirstOrDefault(c => c.Number == number);
        if (subcontract is null || certificate is null)
        {
            return Missing(kind, number);
        }

        var job = await _db.JobContracts.AsNoTracking().Where(j => j.Id == subcontract.JobContractId).Select(j => new { j.ContractNumber, j.Name }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var supplier = await _db.Suppliers.AsNoTracking().Where(s => s.Id == subcontract.SupplierId).Select(s => new { s.Code, s.Name }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        decimal previous = subcontract.Certificates.Where(c => c.Date < certificate.Date || (c.Date == certificate.Date && c.Id < certificate.Id)).Sum(c => c.Gross);
        decimal payable = certificate.Gross - certificate.Retention - certificate.AdvanceRecovered - certificate.Withheld;
        System.Text.StringBuilder text = new(Heading(await CompanyNameAsync(companyId, ct).ConfigureAwait(false), "SUBCONTRACTOR PAYMENT CERTIFICATE"));
        text.AppendLine($"Certificate:   {certificate.Number}   Date: {D(certificate.Date)}   Invoice: {certificate.InvoiceNumber}");
        text.AppendLine($"Contract:      {job?.ContractNumber} {job?.Name}");
        text.AppendLine($"Subcontract:   {subcontract.Reference} {subcontract.Description}");
        text.AppendLine($"Subcontractor: {supplier?.Code} {supplier?.Name}");
        text.AppendLine();
        text.AppendLine($"{"Subcontract value",-40}{N(subcontract.Value),16}");
        text.AppendLine($"{"Work done to date",-40}{N(certificate.WorkDoneToDate),16}");
        text.AppendLine($"{"Less previously certified",-40}{N(previous),16}");
        text.AppendLine($"{"Gross this certificate",-40}{N(certificate.Gross),16}");
        text.AppendLine($"{$"Less retention ({subcontract.RetentionPercent.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}%)",-40}{N(certificate.Retention),16}");
        text.AppendLine($"{"Less advance recovered",-40}{N(certificate.AdvanceRecovered),16}");
        text.AppendLine($"{"Less tax withheld",-40}{N(certificate.Withheld),16}");
        text.AppendLine($"{"Amount payable",-40}{N(payable),16}");
        text.AppendLine();
        text.AppendLine($"Retention held to date: {N(subcontract.Certificates.Sum(c => c.Retention) - subcontract.RetentionReleased)}");
        text.AppendLine();
        text.AppendLine("Certified by (site engineer): ____________________   Approved: ____________________");
        return text.ToString();
    }

    // DOC42: a council licence or permit to display at the premises (brief §S "permits and licence certificates").
    private async Task<Result<string>> LicenceCertificateAsync(long companyId, DocumentKind kind, string number, CancellationToken ct)
    {
        Core.Municipal.CouncilLicence? licence = await _db.Set<Core.Municipal.CouncilLicence>().AsNoTracking()
            .FirstOrDefaultAsync(l => l.CompanyId == companyId && l.LicenceNumber == number, ct).ConfigureAwait(false);
        if (licence is null)
        {
            return Missing(kind, number);
        }

        string holder = await _db.Customers.AsNoTracking().Where(c => c.Id == licence.HolderCustomerId).Select(c => c.Name + " (" + c.Code + ")").FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? "-";
        string? stand = licence.PropertyId is long propertyId
            ? await _db.Properties.AsNoTracking().Where(p => p.Id == propertyId).Select(p => p.StandNumber).FirstOrDefaultAsync(ct).ConfigureAwait(false) : null;
        System.Text.StringBuilder text = new(Heading(await CompanyNameAsync(companyId, ct).ConfigureAwait(false), $"{licence.Kind.ToString().ToUpperInvariant()} LICENCE"));
        text.AppendLine($"Licence:     {licence.LicenceNumber}");
        text.AppendLine($"Holder:      {holder}");
        if (stand is not null)
        {
            text.AppendLine($"Premises:    stand {stand}");
        }

        text.AppendLine($"Activity:    {licence.Description}");
        text.AppendLine($"Issued:      {D(licence.IssuedOn)}   Expires: {D(licence.ExpiresOn)}   Renewals: {licence.Renewals}");
        text.AppendLine($"Fee paid:    {N(licence.Fee)}   Status: {licence.Status}");
        if (!string.IsNullOrWhiteSpace(licence.RevokedReason))
        {
            text.AppendLine($"Revoked:     {licence.RevokedReason}");
        }

        text.AppendLine();
        text.AppendLine("This licence must be displayed in a conspicuous place at the premises.");
        text.AppendLine("It is not transferable and lapses on the expiry date unless renewed.");
        text.AppendLine();
        text.AppendLine("Licensing officer: ____________________   Official stamp:");
        return text.ToString();
    }

    private static Error Missing(DocumentKind kind, string key) => Error.NotFound("Doc.NotFound", $"No {kind.Name.ToLowerInvariant()} {key} ({kind.NumberLabel}).");

    private T Get<T>() where T : notnull => _services.GetRequiredService<T>();

    private Task<long?> CustomerAsync(long companyId, string code, CancellationToken ct)
        => _db.Customers.AsNoTracking().Where(c => c.CompanyId == companyId && c.Code == code).Select(c => (long?)c.Id).FirstOrDefaultAsync(ct);
}
