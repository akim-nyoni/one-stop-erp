using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
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

        Dictionary<long, Supplier> suppliers = await _db.Suppliers
            .Where(s => s.CompanyId == request.CompanyId)
            .ToDictionaryAsync(s => s.Id, ct).ConfigureAwait(false);

        List<PaymentRunLine> lines = [];
        int index = 0;
        foreach (IGrouping<(long SupplierId, string Currency), OpenItem> group in dueInvoices
            .GroupBy(i => (i.TraderId, i.Currency.Value)))
        {
            if (!suppliers.TryGetValue(group.Key.SupplierId, out Supplier? supplier) || supplier.OnHold)
            {
                continue;
            }

            decimal amount = group.Sum(i => i.OutstandingTransaction);
            if (amount <= 0m)
            {
                continue;
            }

            index++;
            string paymentNumber = $"PMT-{request.PaymentDate:yyyyMMdd}-{index:D4}";

            Result<TradingPostingResult> payment = await _purchaseLedger.PostPaymentAsync(new PaymentRequest
            {
                CompanyId = request.CompanyId,
                SupplierId = supplier.Id,
                DocumentNumber = paymentNumber,
                PaymentDate = request.PaymentDate,
                Currency = supplier.Currency,
                Amount = amount,
                BankAccountCode = request.BankAccountCode,
                Narrative = "Payment run",
            }, ct).ConfigureAwait(false);

            if (payment.IsFailure)
            {
                return payment.Error;
            }

            int invoicesPaid = 0;
            foreach (OpenItem invoice in group.OrderBy(i => i.DocumentDate))
            {
                Result<AllocationResult> alloc = await _allocationService.AllocateAsync(
                    request.CompanyId, TradingLedger.AccountsPayable, payment.Value.OpenItemId, invoice.Id, invoice.OutstandingTransaction, request.PaymentDate, ct)
                    .ConfigureAwait(false);
                if (alloc.IsFailure)
                {
                    return alloc.Error;
                }

                invoicesPaid++;
            }

            lines.Add(new PaymentRunLine(supplier.Id, supplier.Code, supplier.Name, supplier.Currency.Value, amount, paymentNumber, invoicesPaid));
        }

        byte[] eft = BuildEftFile(request, lines, suppliers);
        decimal total = lines.Sum(l => l.Amount);
        return new PaymentRunResult(lines, total, eft);
    }

    private static byte[] BuildEftFile(PaymentRunRequest request, IReadOnlyList<PaymentRunLine> lines, IReadOnlyDictionary<long, Supplier> suppliers)
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
