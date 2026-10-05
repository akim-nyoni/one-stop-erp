using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Procurement;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Inventory;
using One4All.Core.Procurement;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Trading;

namespace One4All.Infrastructure.Reporting;

/// <summary>Serves the AP-numbered payables reports (brief §14 C): every data command whose name starts with <c>Payb</c>.</summary>
public sealed class PayablesReportDataProvider : IReportDataProvider
{
    private static readonly OpenItemType[] PurchaseTypes = [OpenItemType.Invoice, OpenItemType.CreditNote, OpenItemType.DebitNote];

    private readonly One4AllDbContext _db;
    private readonly ISupplierScorecardService _scorecard;

    /// <summary>Creates the provider.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="scorecard">The supplier scorecard.</param>
    public PayablesReportDataProvider(One4AllDbContext db, ISupplierScorecardService scorecard)
    {
        _db = db;
        _scorecard = scorecard;
    }

    /// <inheritdoc />
    public bool CanHandle(string command) => command.StartsWith("Payb", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetRowsAsync(long companyId, string command, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct = default)
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        DateOnly asAt = DateParameter(parameters, "AsAtDate") ?? today;
        DateOnly toDate = DateParameter(parameters, "ToDate") ?? today;
        DateOnly fromDate = DateParameter(parameters, "FromDate") ?? new DateOnly(toDate.Year, 1, 1);
        return command.ToUpperInvariant() switch
        {
            "PAYBLEDGER" => await LedgerAsync(companyId, parameters, ct).ConfigureAwait(false),
            "PAYBDAYBOOK" => await DayBookAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "PAYBPAYMENTS" => await PaymentsAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "PAYBGRNI" => await GrniAsync(companyId, asAt, ct).ConfigureAwait(false),
            "PAYBDISPUTES" => await DisputesAsync(companyId, ct).ConfigureAwait(false),
            "PAYBWITHHOLDING" => await WithholdingAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "PAYBSCORECARD" => await ScorecardAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "PAYBBALANCES" => await BalancesAsync(companyId, asAt, ct).ConfigureAwait(false),
            "PAYBBANKCHANGES" => await BankChangesAsync(companyId, ct).ConfigureAwait(false),
            "PAYBTOPSUPPLIERS" => await TopSuppliersAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            _ => [],
        };
    }

    // On the payables ledger a credit is owed to the supplier, so the running balance counts credits up and debits down.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> LedgerAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        string? code = parameters.TryGetValue("SupplierCode", out object? value) && value is string text && text.Trim().Length > 0 ? text.Trim() : null;
        Supplier? supplier = code is null ? null : await _db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == companyId && s.Code == code, ct).ConfigureAwait(false);
        if (supplier is null)
        {
            return [];
        }

        List<OpenItem> items = await _db.OpenItems.AsNoTracking().Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsPayable && i.TraderId == supplier.Id)
            .OrderBy(i => i.DocumentDate).ThenBy(i => i.Id).ToListAsync(ct).ConfigureAwait(false);
        decimal balance = 0m;
        List<IReadOnlyDictionary<string, object?>> rows = [];
        foreach (OpenItem item in items)
        {
            bool credit = item.Sign == LedgerSign.Credit;
            balance += credit ? item.OriginalBase : -item.OriginalBase;
            rows.Add(Row(("Date", Date(item.DocumentDate)), ("Type", item.ItemType.ToString()), ("Document", item.DocumentNumber), ("Due", Date(item.DueDate)),
                ("Debit", credit ? string.Empty : Money(item.OriginalBase)), ("Credit", credit ? Money(item.OriginalBase) : string.Empty), ("Balance", Money(balance)),
                ("Outstanding", credit ? item.OutstandingBase : -item.OutstandingBase)));
        }

        return rows;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> DayBookAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        Dictionary<long, string> suppliers = await SuppliersAsync(companyId, ct).ConfigureAwait(false);
        List<OpenItem> items = await _db.OpenItems.AsNoTracking().Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsPayable && PurchaseTypes.Contains(i.ItemType)
            && i.DocumentDate >= fromDate && i.DocumentDate <= toDate).OrderBy(i => i.DocumentDate).ThenBy(i => i.DocumentNumber).ToListAsync(ct).ConfigureAwait(false);
        return [.. items.Select(i =>
        {
            decimal sign = i.ItemType == OpenItemType.CreditNote ? -1m : 1m;
            return Row(("Date", Date(i.DocumentDate)), ("Document", i.DocumentNumber), ("Type", i.ItemType.ToString()), ("Supplier", suppliers.GetValueOrDefault(i.TraderId, "?")),
                ("Currency", i.Currency.Value), ("Transaction", Money(sign * i.OriginalTransaction)), ("Base", sign * i.OriginalBase));
        })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> PaymentsAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        Dictionary<long, string> suppliers = await SuppliersAsync(companyId, ct).ConfigureAwait(false);
        List<OpenItem> payments = await _db.OpenItems.AsNoTracking().Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsPayable && i.ItemType == OpenItemType.Payment
            && i.DocumentDate >= fromDate && i.DocumentDate <= toDate).OrderBy(i => i.DocumentDate).ThenBy(i => i.DocumentNumber).ToListAsync(ct).ConfigureAwait(false);
        List<long> ids = [.. payments.Select(p => p.Id)];
        Dictionary<long, decimal> withheld = await _db.Set<WithholdingCertificate>().AsNoTracking().Where(c => ids.Contains(c.PaymentOpenItemId))
            .GroupBy(c => c.PaymentOpenItemId).Select(g => new { g.Key, Amount = g.Sum(c => c.WithheldAmount) }).ToDictionaryAsync(x => x.Key, x => x.Amount, ct).ConfigureAwait(false);
        return [.. payments.Select(p => Row(("Date", Date(p.DocumentDate)), ("Document", p.DocumentNumber), ("Supplier", suppliers.GetValueOrDefault(p.TraderId, "?")), ("Currency", p.Currency.Value),
            ("Transaction", Money(p.OriginalTransaction)), ("Withheld", withheld.TryGetValue(p.Id, out decimal w) ? Money(w) : "—"), ("Base", p.OriginalBase)))];
    }

    // The accrual: received but not invoiced, at the order price (the price the GRN accrued at).
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GrniAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        Dictionary<long, string> suppliers = await SuppliersAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<long, string> items = await _db.Set<Item>().AsNoTracking().IgnoreQueryFilters().Where(i => i.CompanyId == companyId).ToDictionaryAsync(i => i.Id, i => i.Code + " " + i.Description, ct).ConfigureAwait(false);
        List<PurchaseOrder> orders = await _db.Set<PurchaseOrder>().AsNoTracking().Include(o => o.Lines)
            .Where(o => o.CompanyId == companyId && o.Lines.Any(l => l.ReceivedQuantity > l.InvoicedQuantity)).OrderBy(o => o.OrderDate).ThenBy(o => o.OrderNumber).ToListAsync(ct).ConfigureAwait(false);
        List<long> orderIds = [.. orders.Select(o => o.Id)];
        Dictionary<long, DateOnly> lastReceipt = await (from g in _db.Set<GoodsReceivedNote>().AsNoTracking() where orderIds.Contains(g.PurchaseOrderId) && g.Date <= asAt
                                                       group g by g.PurchaseOrderId into r select new { r.Key, Last = r.Max(x => x.Date) }).ToDictionaryAsync(x => x.Key, x => x.Last, ct).ConfigureAwait(false);
        return [.. orders.SelectMany(o => o.Lines.Where(l => l.ReceivedQuantity > l.InvoicedQuantity).Select(l => Row(("Order", o.OrderNumber), ("Supplier", suppliers.GetValueOrDefault(o.SupplierId, "?")),
            ("Item", items.GetValueOrDefault(l.ItemId, l.Description)), ("Received", Qty(l.ReceivedQuantity)), ("Invoiced", Qty(l.InvoicedQuantity)), ("Price", Money(l.UnitPrice)),
            ("LastReceipt", lastReceipt.TryGetValue(o.Id, out DateOnly last) ? Date(last) : "—"), ("Days", lastReceipt.TryGetValue(o.Id, out DateOnly d) ? Math.Max(0, asAt.DayNumber - d.DayNumber).ToString(CultureInfo.InvariantCulture) : "—"),
            ("Value", Math.Round((l.ReceivedQuantity - l.InvoicedQuantity) * l.UnitPrice, 2, MidpointRounding.AwayFromZero)))))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> DisputesAsync(long companyId, CancellationToken ct)
    {
        Dictionary<long, string> suppliers = await SuppliersAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<long, string> orders = await _db.Set<PurchaseOrder>().AsNoTracking().Where(o => o.CompanyId == companyId).ToDictionaryAsync(o => o.Id, o => o.OrderNumber, ct).ConfigureAwait(false);
        List<InvoiceDispute> disputes = await _db.Set<InvoiceDispute>().AsNoTracking().Include(d => d.Lines).Where(d => d.CompanyId == companyId).OrderBy(d => d.InvoiceDate).ThenBy(d => d.InvoiceNumber)
            .ToListAsync(ct).ConfigureAwait(false);
        return [.. disputes.Select(d => Row(("Invoice", d.InvoiceNumber), ("Date", Date(d.InvoiceDate)), ("Supplier", suppliers.GetValueOrDefault(d.SupplierId, "?")), ("Order", orders.GetValueOrDefault(d.PurchaseOrderId, "?")),
            ("Reason", $"{d.ReasonCode}: {d.Reason}"), ("Status", d.Status.ToString()), ("Resolution", d.ResolutionNote ?? "—"),
            ("Value", d.Lines.Sum(l => Math.Round(l.Quantity * l.UnitPrice, 2, MidpointRounding.AwayFromZero)))))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> WithholdingAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        Dictionary<long, string> suppliers = await SuppliersAsync(companyId, ct).ConfigureAwait(false);
        List<WithholdingCertificate> certificates = await _db.Set<WithholdingCertificate>().AsNoTracking()
            .Where(c => c.CompanyId == companyId && c.PaymentDate >= fromDate && c.PaymentDate <= toDate).OrderBy(c => c.PaymentDate).ThenBy(c => c.CertificateNumber).ToListAsync(ct).ConfigureAwait(false);
        return [.. certificates.Select(c => Row(("Certificate", c.CertificateNumber), ("Supplier", suppliers.GetValueOrDefault(c.SupplierId, "?")), ("Payment", c.PaymentDocumentNumber), ("Date", Date(c.PaymentDate)),
            ("Tax", c.TaxCode), ("Rate", c.RatePercent.ToString("0.##", CultureInfo.InvariantCulture)), ("Gross", Money(c.GrossAmount)), ("Remitted", c.RemittedOn is DateOnly r ? Date(r) : "Not yet"),
            ("Withheld", c.WithheldAmount)))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ScorecardAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
        => [.. (await _scorecard.ScorecardAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false)).OrderBy(s => s.SupplierName, StringComparer.Ordinal)
            .Select(s => Row(("Supplier", s.SupplierName), ("Deliveries", s.Deliveries), ("OnTime", s.OnTimePercent is decimal o ? $"{o:0.#}% of {s.DeliveriesJudged}" : "—"),
                ("Received", Qty(s.QuantityReceived)), ("Returned", Qty(s.QuantityReturned)), ("Quality", s.QualityPercent is decimal q ? $"{q:0.#}%" : "—"), ("Spend", s.Spend)))];

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> BalancesAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        Dictionary<long, Supplier> suppliers = await _db.Suppliers.AsNoTracking().IgnoreQueryFilters().Where(s => s.CompanyId == companyId).ToDictionaryAsync(s => s.Id, ct).ConfigureAwait(false);
        List<OpenItemAsAt> items = await OpenItemsAsAt.LoadAsync(_db, companyId, TradingLedger.AccountsPayable, asAt, ct).ConfigureAwait(false);
        return [.. items.Where(i => suppliers.ContainsKey(i.Item.TraderId)).GroupBy(i => i.Item.TraderId)
            .Select(g => (Supplier: suppliers[g.Key], Balance: g.Sum(i => i.Signed(LedgerSign.Credit)))).Where(x => x.Balance != 0m)
            .OrderBy(x => x.Balance > 0m).ThenBy(x => x.Supplier.Name, StringComparer.Ordinal)
            .Select(x => Row(("Supplier", x.Supplier.Name), ("Currency", x.Supplier.Currency.Value), ("Flag", x.Balance < 0m ? "Debit balance" : string.Empty),
                ("Status", x.Supplier.IsBlacklisted ? "Blacklisted" : x.Supplier.OnHold ? "On hold" : "Active"), ("Balance", x.Balance)))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> BankChangesAsync(long companyId, CancellationToken ct)
    {
        Dictionary<long, string> suppliers = await SuppliersAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<long, string> users = await _db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Username, ct).ConfigureAwait(false);
        List<SupplierBankChange> changes = await _db.Set<SupplierBankChange>().AsNoTracking().Where(c => c.CompanyId == companyId).OrderBy(c => c.CreatedOnUtc).ThenBy(c => c.Id).ToListAsync(ct).ConfigureAwait(false);
        static string Mask(string? number) => string.IsNullOrEmpty(number) ? "—" : number.Length <= 4 ? new string('*', number.Length) : new string('*', number.Length - 4) + number[^4..];
        return [.. changes.Select(c => Row(("Requested", c.CreatedOnUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)), ("Supplier", suppliers.GetValueOrDefault(c.SupplierId, "?")),
            ("RequestedBy", users.GetValueOrDefault(c.RequestedByUserId, $"user {c.RequestedByUserId}")), ("NewDetails", $"{c.BankName ?? "—"} / {c.AccountName ?? "—"} / {Mask(c.AccountNumber)}"),
            ("Status", c.Status.ToString()), ("ReviewedBy", c.ReviewedByUserId is long r ? users.GetValueOrDefault(r, $"user {r}") : "—"), ("Reason", c.RejectionReason ?? string.Empty)))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> TopSuppliersAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        Dictionary<long, string> suppliers = await SuppliersAsync(companyId, ct).ConfigureAwait(false);
        var purchases = await _db.OpenItems.AsNoTracking().Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsPayable && PurchaseTypes.Contains(i.ItemType)
            && i.DocumentDate >= fromDate && i.DocumentDate <= toDate)
            .GroupBy(i => i.TraderId).Select(g => new { g.Key, Count = g.Count(), Net = g.Sum(i => i.ItemType == OpenItemType.CreditNote ? -i.OriginalBase : i.OriginalBase) })
            .ToListAsync(ct).ConfigureAwait(false);
        decimal total = purchases.Sum(p => p.Net);
        decimal running = 0m;
        int rank = 0;
        return [.. purchases.OrderByDescending(p => p.Net).ThenBy(p => suppliers.GetValueOrDefault(p.Key), StringComparer.Ordinal).Select(p =>
        {
            running += p.Net;
            return Row(("Rank", ++rank), ("Supplier", suppliers.GetValueOrDefault(p.Key, "?")), ("Documents", p.Count), ("Share", Percent(p.Net, total)), ("Cumulative", Percent(running, total)),
                ("Purchases", p.Net));
        })];
    }

    private Task<Dictionary<long, string>> SuppliersAsync(long companyId, CancellationToken ct)
        => _db.Suppliers.AsNoTracking().IgnoreQueryFilters().Where(s => s.CompanyId == companyId).ToDictionaryAsync(s => s.Id, s => s.Name, ct);

    private static DateOnly? DateParameter(IReadOnlyDictionary<string, object?> parameters, string name) => parameters.TryGetValue(name, out object? value) && value is DateOnly date ? date : null;

    private static string Percent(decimal part, decimal total) => total == 0m ? "—" : (part * 100m / total).ToString("0.0", CultureInfo.InvariantCulture) + "%";

    private static string Qty(decimal quantity) => quantity.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Money(decimal amount) => amount.ToString("N2", CultureInfo.InvariantCulture);

    private static Dictionary<string, object?> Row(params (string Key, object? Value)[] cells)
    {
        Dictionary<string, object?> row = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, object? value) in cells)
        {
            row[key] = value;
        }

        return row;
    }
}
