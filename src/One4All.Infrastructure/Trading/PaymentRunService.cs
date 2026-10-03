using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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
    public async Task<Result<PaymentRunResult>> RunAsync(PaymentRunRequest request, CancellationToken ct = default)
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

        // Continue the day's numbering if an earlier run already used it.
        string prefix = $"PMT-{request.PaymentDate:yyyyMMdd}-";
        int index = await _db.OpenItems
            .CountAsync(i => i.CompanyId == request.CompanyId && i.Ledger == TradingLedger.AccountsPayable && i.DocumentNumber.StartsWith(prefix), ct)
            .ConfigureAwait(false);

        // The whole run stands or falls together: a failure part-way must not leave a payment posted but unallocated,
        // which a re-run would pay again.
        await using IDbContextTransaction transaction = await _db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

        List<PaymentRunLine> lines = [];
        decimal totalBase = 0m;
        foreach (IGrouping<(long SupplierId, string Currency), OpenItem> group in dueInvoices
            .GroupBy(i => (i.TraderId, i.Currency.Value)))
        {
            if (!suppliers.TryGetValue(group.Key.SupplierId, out Supplier? supplier) || supplier.OnHold)
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
            lines.Add(new PaymentRunLine(supplier.Id, supplier.Code, supplier.Name, currency.Value, toPay, paymentNumber, touched.Count, creditsApplied));
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);

        byte[] eft = BuildEftFile(request, [.. lines.Where(l => l.Amount > 0m)], suppliers);
        return new PaymentRunResult(lines, totalBase, eft);
    }

    private static byte[] BuildEftFile(PaymentRunRequest request, List<PaymentRunLine> lines, IReadOnlyDictionary<long, Supplier> suppliers)
    {
        // A generic pipe-delimited EFT template. Real banks define their own layout (brief §4.15.7 / §4.6);
        // the layout is configuration, not code, in production.
        StringBuilder sb = new();
        sb.AppendLine($"HDR|{request.PaymentDate:yyyyMMdd}|{request.BankAccountCode}|{lines.Count}|{lines.Sum(l => l.Amount).ToString("0.00", CultureInfo.InvariantCulture)}");
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
                line.Amount.ToString("0.00", CultureInfo.InvariantCulture)));
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }
}
