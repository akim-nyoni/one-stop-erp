using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using One4All.Core.Accounting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Common;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Trading;

/// <summary>Runs supplier payment runs (brief §4.6): selects, pays, allocates and produces an EFT file.</summary>
public sealed class PaymentRunService : IPaymentRunService
{
    private readonly One4AllDbContext _db;
    private readonly IPurchaseLedgerService _purchaseLedger;
    private readonly IAllocationService _allocationService;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="purchaseLedger">The AP posting service.</param>
    /// <param name="allocationService">The allocation service.</param>
    public PaymentRunService(One4AllDbContext db, IPurchaseLedgerService purchaseLedger, IAllocationService allocationService)
    {
        _db = db;
        _purchaseLedger = purchaseLedger;
        _allocationService = allocationService;
    }

    /// <inheritdoc />
    public Task<Result<PaymentRunResult>> RunAsync(PaymentRunRequest request, CancellationToken ct = default)
        => _db.AtomicAsync(() => RunCoreAsync(request, ct), ct);

    // One unit of work: every posting it makes commits together or not at all (joining a caller's transaction).
    private async Task<Result<PaymentRunResult>> RunCoreAsync(PaymentRunRequest request, CancellationToken ct)
    {
        Core.Banking.BankFileLayout? layout = null;
        if (!string.IsNullOrWhiteSpace(request.EftLayoutCode))
        {
            layout = await _db.BankFileLayouts.AsNoTracking().FirstOrDefaultAsync(l => l.CompanyId == request.CompanyId && l.Code == request.EftLayoutCode.Trim().ToUpper(), ct).ConfigureAwait(false);
            if (layout is null)
            {
                return Error.NotFound("Bank.LayoutNotFound", $"No bank file layout '{request.EftLayoutCode}'.");
            }
        }

        (List<OpenItem> dueInvoices, List<OpenItem> credits, Dictionary<long, Supplier> suppliers) = await SelectAsync(request, ct).ConfigureAwait(false);
        HashSet<long> bankChanging = await BankChangingAsync(request.CompanyId, ct).ConfigureAwait(false);

        // Continue the day's numbering if an earlier run already used it.
        string prefix = $"PMT-{request.PaymentDate:yyyyMMdd}-";
        int index = await _db.OpenItems
            .CountAsync(i => i.CompanyId == request.CompanyId && i.Ledger == TradingLedger.AccountsPayable && i.DocumentNumber.StartsWith(prefix), ct)
            .ConfigureAwait(false);

        // The whole run stands or falls together: a failure part-way must not leave a payment posted but unallocated,
        // which a re-run would pay again.

        List<PaymentRunLine> lines = [];
        decimal totalBase = 0m;
        foreach (IGrouping<(long SupplierId, string Currency), OpenItem> group in dueInvoices
            .GroupBy(i => (i.TraderId, i.Currency.Value)))
        {
            // Held: on hold, or bank details changing (paying to details about to change is how payment fraud works).
            if (!suppliers.TryGetValue(group.Key.SupplierId, out Supplier? supplier) || supplier.OnHold || bankChanging.Contains(supplier.Id))
            {
                continue;
            }

            List<OpenItem> invoices = [.. group.OrderBy(i => i.DocumentDate).ThenBy(i => i.Id)];
            CurrencyCode currency = invoices[0].Currency;

            // Apply the supplier's unused credits first, oldest invoice first.
            decimal creditsApplied = 0m;
            HashSet<long> touched = [];
            foreach (OpenItem credit in credits.Where(c => c.TraderId == supplier.Id && c.Currency == currency))
            {
                foreach (OpenItem invoice in invoices.Where(i => i.OutstandingTransaction > 0m))
                {
                    if (credit.OutstandingTransaction <= 0m)
                    {
                        break;
                    }

                    decimal amount = Math.Min(credit.OutstandingTransaction, invoice.OutstandingTransaction);
                    Result<AllocationResult> applied = await _allocationService.AllocateAsync(
                        request.CompanyId, TradingLedger.AccountsPayable, credit.Id, invoice.Id, amount, request.PaymentDate, ct).ConfigureAwait(false);
                    if (applied.IsFailure)
                    {
                        return applied.Error;
                    }

                    creditsApplied += applied.Value.TransactionAmount;
                    touched.Add(invoice.Id);
                }
            }

            decimal toPay = invoices.Sum(i => i.OutstandingTransaction);
            if (toPay <= 0m)
            {
                // Settled entirely by credits: nothing to pay.
                if (creditsApplied > 0m)
                {
                    lines.Add(new PaymentRunLine(supplier.Id, supplier.Code, supplier.Name, currency.Value, 0m, string.Empty, touched.Count, creditsApplied));
                }

                continue;
            }

            index++;
            string paymentNumber = $"{prefix}{index:D4}";

            Result<TradingPostingResult> payment = await _purchaseLedger.PostPaymentAsync(new PaymentRequest
            {
                CompanyId = request.CompanyId,
                SupplierId = supplier.Id,
                DocumentNumber = paymentNumber,
                PaymentDate = request.PaymentDate,
                Currency = currency,
                Amount = toPay,
                BankAccountCode = request.BankAccountCode,
                Narrative = "Payment run",
                WithholdingTaxCode = request.WithholdingTaxCode,
            }, ct).ConfigureAwait(false);

            if (payment.IsFailure)
            {
                return payment.Error;
            }

            foreach (OpenItem invoice in invoices.Where(i => i.OutstandingTransaction > 0m))
            {
                Result<AllocationResult> alloc = await _allocationService.AllocateAsync(
                    request.CompanyId, TradingLedger.AccountsPayable, payment.Value.OpenItemId, invoice.Id, invoice.OutstandingTransaction, request.PaymentDate, ct)
                    .ConfigureAwait(false);
                if (alloc.IsFailure)
                {
                    return alloc.Error;
                }

                touched.Add(invoice.Id);
            }

            totalBase += payment.Value.BaseAmount;
            decimal withheld = payment.Value.WithheldBase == 0m ? 0m : await WithholdingOnAsync(request, supplier, toPay, ct).ConfigureAwait(false);
            lines.Add(new PaymentRunLine(supplier.Id, supplier.Code, supplier.Name, currency.Value, toPay, paymentNumber, touched.Count, creditsApplied, withheld));
        }

        byte[] eft = layout is null ? BuildEftFile(request, [.. lines.Where(l => l.Amount > 0m)], suppliers) : LayoutEftFile(request, layout, [.. lines.Where(l => l.Amount > 0m)], suppliers);
        return new PaymentRunResult(lines, totalBase, eft);
    }

    /// <inheritdoc />
    public async Task<Result<PaymentRunProposal>> ProposeAsync(PaymentRunRequest request, CancellationToken ct = default)
    {
        (List<OpenItem> dueInvoices, List<OpenItem> credits, Dictionary<long, Supplier> suppliers) = await SelectAsync(request, ct).ConfigureAwait(false);
        HashSet<long> bankChanging = await BankChangingAsync(request.CompanyId, ct).ConfigureAwait(false);

        List<PaymentProposalLine> lines = [];
        foreach (IGrouping<(long SupplierId, string Currency), OpenItem> group in dueInvoices.GroupBy(i => (i.TraderId, i.Currency.Value)))
        {
            if (!suppliers.TryGetValue(group.Key.SupplierId, out Supplier? supplier))
            {
                continue;
            }

            decimal due = group.Sum(i => i.OutstandingTransaction);
            decimal available = credits.Where(c => c.TraderId == supplier.Id && c.Currency.Value == group.Key.Currency).Sum(c => c.OutstandingTransaction);

            // Exactly what the run does: credits are set against the invoices up to what is due; the rest is paid.
            bool held = supplier.OnHold || bankChanging.Contains(supplier.Id);
            decimal apply = held ? 0m : Math.Min(available, due);
            decimal toPay = held ? 0m : due - apply;
            decimal withholding = toPay > 0m ? await WithholdingOnAsync(request, supplier, toPay, ct).ConfigureAwait(false) : 0m;
            lines.Add(new PaymentProposalLine(supplier.Id, supplier.Code, supplier.Name, group.Key.Currency, group.Count(), due, available, apply, toPay, held, withholding, bankChanging.Contains(supplier.Id)));
        }

        return new PaymentRunProposal(request.PaymentDate, [.. lines.OrderBy(l => l.SupplierCode, StringComparer.Ordinal).ThenBy(l => l.Currency, StringComparer.Ordinal)]);
    }

    private async Task<HashSet<long>> BankChangingAsync(long companyId, CancellationToken ct)
        => [.. await _db.Set<SupplierBankChange>().Where(c => c.CompanyId == companyId && c.Status == BankChangeStatus.Pending)
            .Select(c => c.SupplierId).ToListAsync(ct).ConfigureAwait(false)];

    private async Task<(List<OpenItem> DueInvoices, List<OpenItem> Credits, Dictionary<long, Supplier> Suppliers)> SelectAsync(PaymentRunRequest request, CancellationToken ct)
    {
        List<OpenItem> dueInvoices = await _db.OpenItems
            .Where(i => i.CompanyId == request.CompanyId && i.Ledger == TradingLedger.AccountsPayable &&
                        i.Sign == LedgerSign.Credit && i.OutstandingTransaction > 0m && i.DueDate <= request.DueOnOrBefore)
            .OrderBy(i => i.DocumentDate).ThenBy(i => i.Id)
            .ToListAsync(ct).ConfigureAwait(false);

        if (request.SupplierIds.Count > 0)
        {
            HashSet<long> filter = request.SupplierIds.ToHashSet();
            dueInvoices = dueInvoices.Where(i => filter.Contains(i.TraderId)).ToList();
        }

        // Credit notes, debit notes and payments on account the supplier has not yet used: they reduce what is paid.
        HashSet<long> supplierIds = [.. dueInvoices.Select(i => i.TraderId)];
        List<OpenItem> credits = await _db.OpenItems
            .Where(i => i.CompanyId == request.CompanyId && i.Ledger == TradingLedger.AccountsPayable && i.Sign == LedgerSign.Debit
                        && i.OutstandingTransaction > 0m && i.DocumentDate <= request.PaymentDate && supplierIds.Contains(i.TraderId))
            .OrderBy(i => i.DocumentDate).ThenBy(i => i.Id)
            .ToListAsync(ct).ConfigureAwait(false);

        Dictionary<long, Supplier> suppliers = await _db.Suppliers
            .Where(s => s.CompanyId == request.CompanyId)
            .ToDictionaryAsync(s => s.Id, ct).ConfigureAwait(false);

        return (dueInvoices, credits, suppliers);
    }

    // The withholding the payment run deducts from a payment (payment currency): the code's rate when the supplier has no
    // valid tax clearance on the payment date, otherwise none — the same rule the purchase ledger applies.
    private async Task<decimal> WithholdingOnAsync(PaymentRunRequest request, Supplier supplier, decimal amount, CancellationToken ct)
    {
        if (await TraderCertificates.WithholdingCodeAsync(_db, request.CompanyId, supplier, request.PaymentDate, request.WithholdingTaxCode, ct).ConfigureAwait(false) is not string code)
        {
            return 0m;
        }

        Core.Accounting.TaxCode? tax = await _db.TaxCodes.Include(t => t.Rates)
            .FirstOrDefaultAsync(t => t.CompanyId == request.CompanyId && t.Code == code && t.IsActive, ct).ConfigureAwait(false);
        return tax is null ? 0m : decimal.Round(amount * tax.RateOn(request.PaymentDate) / 100m, 4, MidpointRounding.ToEven);
    }

    // The EFT file in the bank's own layout (brief §4.15.7): net amounts, with the control total and account hash.
    private static byte[] LayoutEftFile(PaymentRunRequest request, Core.Banking.BankFileLayout layout, List<PaymentRunLine> lines, IReadOnlyDictionary<long, Supplier> suppliers)
    {
        List<Application.Banking.BankFileRow> rows = [.. lines.Select((l, i) => new Application.Banking.BankFileRow(i + 1, l.PaymentNumber, suppliers[l.SupplierId].Name, suppliers[l.SupplierId].BankName ?? string.Empty,
            string.Empty, suppliers[l.SupplierId].BankAccountNumber ?? string.Empty, l.Currency.ToString(), l.NetPaid))];
        Application.Banking.BankFileHeader header = new(request.PaymentDate, request.BankAccountCode, rows.Select(r => r.Currency).FirstOrDefault() ?? string.Empty, rows.Count, rows.Sum(r => r.Amount),
            Application.Banking.BankFileFormatter.HashTotal(rows.Select(r => r.Account)));
        return Encoding.UTF8.GetBytes(Application.Banking.BankFileFormatter.Render(layout.HeaderTemplate, layout.DetailTemplate, layout.TrailerTemplate, header, rows, layout.LineEnding));
    }

    private static byte[] BuildEftFile(PaymentRunRequest request, List<PaymentRunLine> lines, IReadOnlyDictionary<long, Supplier> suppliers)
    {
        // A generic pipe-delimited EFT template. Real banks define their own layout (brief §4.15.7 / §4.6);
        // the layout is configuration, not code, in production.
        StringBuilder sb = new();
        sb.AppendLine($"HDR|{request.PaymentDate:yyyyMMdd}|{request.BankAccountCode}|{lines.Count}|{lines.Sum(l => l.NetPaid).ToString("0.00", CultureInfo.InvariantCulture)}");
        foreach (PaymentRunLine line in lines)
        {
            Supplier supplier = suppliers[line.SupplierId];
            sb.AppendLine(string.Join('|',
                "PMT",
                line.PaymentNumber,
                supplier.Code,
                supplier.Name,
                supplier.BankName ?? string.Empty,
                supplier.BankAccountNumber ?? string.Empty,
                line.Currency,
                line.NetPaid.ToString("0.00", CultureInfo.InvariantCulture)));
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }
}
