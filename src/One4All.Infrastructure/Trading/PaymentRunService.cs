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
    private readonly Lazy<Application.Abstractions.Workflow.IWorkflowService>? _workflow;
    private readonly IPurchaseLedgerService _purchaseLedger;
    private readonly IAllocationService _allocationService;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="purchaseLedger">The AP posting service.</param>
    /// <param name="allocationService">The allocation service.</param>
    public PaymentRunService(One4AllDbContext db, IPurchaseLedgerService purchaseLedger, IAllocationService allocationService, Lazy<Application.Abstractions.Workflow.IWorkflowService>? workflow = null,
        Application.Abstractions.Banking.IChequeService? cheques = null)
    {
        _cheques = cheques;
        _workflow = workflow;
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
        if (await _db.Companies.AnyAsync(c => c.Id == request.CompanyId && c.PaymentRunApprovalRequired, ct).ConfigureAwait(false))
        {
            return Error.Rule("Ap.RunApprovalRequired", "Payment runs must be saved and approved by a second person: save a proposal instead.");
        }

        return await PayAsync(request, null, ct).ConfigureAwait(false);
    }

    // Pays the run: everything due, or — for an approved saved run — only its included lines at their approved amounts (a part-payment
    // settles the oldest invoices first).
    private readonly Application.Abstractions.Banking.IChequeService? _cheques;

    // The cheque number so many on from the first, keeping its width ("000123" → "000124").
    internal static string? ChequeNumber(string first, int offset)
        => long.TryParse(first, out long start) && start >= 0 ? (start + offset).ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(first.Length, '0') : null;

    private async Task<Result<PaymentRunResult>> PayAsync(PaymentRunRequest request, IReadOnlyDictionary<(long SupplierId, string Currency), decimal>? approved, CancellationToken ct)
    {
        // A cheque run writes cheques from the bank account's cheque book, numbered on from the first.
        long chequeBank = 0;
        if (request.Method == Core.Trading.PaymentRunMethod.Cheque)
        {
            if (string.IsNullOrWhiteSpace(request.FirstChequeNumber) || ChequeNumber(request.FirstChequeNumber.Trim(), 0) is null || _cheques is null)
            {
                return Error.Validation("Ap.ChequeNumber", "A cheque run needs the first cheque number (digits).");
            }

            chequeBank = await _db.BankAccounts.Where(b => b.CompanyId == request.CompanyId && b.GlAccountCode == request.BankAccountCode.Trim()).Select(b => b.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (chequeBank == 0)
            {
                return Error.NotFound("Ap.ChequeBank", $"No bank account is kept on ledger account {request.BankAccountCode} to write cheques from.");
            }
        }

        Core.Banking.BankFileLayout? layout = null;
        if (request.Method == Core.Trading.PaymentRunMethod.MobileMoney && !string.IsNullOrWhiteSpace(request.EftLayoutCode))
        {
            return Error.Validation("Ap.MobileLayout", "A mobile-money run makes its own payout file; leave the bank file layout blank.");
        }

        bool pain001 = string.Equals(request.EftLayoutCode?.Trim(), Application.Banking.Pain001.LayoutCode, StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(request.EftLayoutCode) && !pain001)
        {
            layout = await _db.BankFileLayouts.AsNoTracking().FirstOrDefaultAsync(l => l.CompanyId == request.CompanyId && l.Code == request.EftLayoutCode.Trim().ToUpper(), ct).ConfigureAwait(false);
            if (layout is null)
            {
                return Error.NotFound("Bank.LayoutNotFound", $"No bank file layout '{request.EftLayoutCode}'.");
            }
        }

        (List<OpenItem> dueInvoices, List<OpenItem> credits, Dictionary<long, Supplier> suppliers) = await SelectAsync(request, ct).ConfigureAwait(false);
        HashSet<long> bankChanging = await BankChangingAsync(request.CompanyId, ct).ConfigureAwait(false);

        // A mobile-money run pays wallets: every supplier it would pay needs one.
        if (request.Method == Core.Trading.PaymentRunMethod.MobileMoney
            && dueInvoices.Select(i => i.TraderId).Distinct().Where(id => suppliers.TryGetValue(id, out Supplier? s) && !s.OnHold && !bankChanging.Contains(id)
                && (approved is null || approved.Any(a => a.Key.SupplierId == id && a.Value > 0m)))
                .Select(id => suppliers[id]).FirstOrDefault(s => string.IsNullOrWhiteSpace(s.MobileMoneyNumber)) is Supplier walletless)
        {
            return Error.Validation("Ap.NoWallet", $"Supplier {walletless.Code} has no mobile-money wallet; pay it another way or request its wallet as a bank-detail change.");
        }

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

            decimal? cap = null;
            if (approved is not null)
            {
                if (!approved.TryGetValue((group.Key.SupplierId, group.Key.Currency), out decimal agreed))
                {
                    continue;
                }

                cap = agreed;
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

            decimal toPay = Math.Min(invoices.Sum(i => i.OutstandingTransaction), cap ?? decimal.MaxValue);
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

            decimal unallocated = toPay;
            foreach (OpenItem invoice in invoices.Where(i => i.OutstandingTransaction > 0m))
            {
                if (unallocated <= 0m)
                {
                    break;
                }

                decimal share = Math.Min(unallocated, invoice.OutstandingTransaction);
                Result<AllocationResult> alloc = await _allocationService.AllocateAsync(
                    request.CompanyId, TradingLedger.AccountsPayable, payment.Value.OpenItemId, invoice.Id, share, request.PaymentDate, ct)
                    .ConfigureAwait(false);
                if (alloc.IsFailure)
                {
                    return alloc.Error;
                }

                unallocated -= share;
                touched.Add(invoice.Id);
            }

            totalBase += payment.Value.BaseAmount;
            decimal withheld = payment.Value.WithheldBase == 0m ? 0m : await WithholdingOnAsync(request, supplier, toPay, ct).ConfigureAwait(false);
            lines.Add(new PaymentRunLine(supplier.Id, supplier.Code, supplier.Name, currency.Value, toPay, paymentNumber, touched.Count, creditsApplied, withheld));
        }

        List<string> chequeNumbers = [];
        if (request.Method == Core.Trading.PaymentRunMethod.Cheque)
        {
            foreach (PaymentRunLine line in lines.Where(l => l.NetPaid > 0m))
            {
                string number = ChequeNumber(request.FirstChequeNumber!.Trim(), chequeNumbers.Count)!;
                Result<long> written = await _cheques!.IssueAsync(request.CompanyId, chequeBank, number, request.PaymentDate, line.SupplierName, line.NetPaid, line.PaymentNumber, ct).ConfigureAwait(false);
                if (written.IsFailure)
                {
                    return written.Error;
                }

                chequeNumbers.Add(number);
            }
        }

        byte[] eft = request.Method == Core.Trading.PaymentRunMethod.MobileMoney ? BuildMobileMoneyFile(request, [.. lines.Where(l => l.Amount > 0m)], suppliers)
            : request.Method != Core.Trading.PaymentRunMethod.Eft ? []
            : pain001 ? await Pain001FileAsync(request, [.. lines.Where(l => l.Amount > 0m)], suppliers, ct).ConfigureAwait(false)
            : layout is null ? BuildEftFile(request, [.. lines.Where(l => l.Amount > 0m)], suppliers) : LayoutEftFile(request, layout, [.. lines.Where(l => l.Amount > 0m)], suppliers);
        return new PaymentRunResult(lines, totalBase, eft, chequeNumbers);
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
            lines.Add(new PaymentProposalLine(supplier.Id, supplier.Code, supplier.Name, group.Key.Currency, group.Count(), due, available, apply, toPay, held, withholding, bankChanging.Contains(supplier.Id))
            {
                Priority = supplier.PaymentPriority,
            });
        }

        // Short of funds: pay by priority, then supplier code, until the funds run out; the rest waits for a later run.
        if (request.FundsAvailable is decimal funds)
        {
            if (funds < 0m || string.IsNullOrWhiteSpace(request.Currency))
            {
                return Error.Validation("Ap.FundsAvailable", "Funds available are a non-negative amount in the run's currency (give the currency).");
            }

            decimal left = funds;
            List<PaymentProposalLine> capped = [];
            foreach (PaymentProposalLine line in lines.OrderBy(l => l.Priority).ThenBy(l => l.SupplierCode, StringComparer.Ordinal))
            {
                decimal pay = Math.Min(line.ToPay, left);
                left -= pay;
                capped.Add(line with { ToPay = pay, Deferred = line.ToPay - pay });
            }

            lines = capped;
        }

        return new PaymentRunProposal(request.PaymentDate, [.. lines.OrderBy(l => l.SupplierCode, StringComparer.Ordinal).ThenBy(l => l.Currency, StringComparer.Ordinal)]);
    }

    /// <inheritdoc />
    public Task<Result<SavedPaymentRun>> SaveProposalAsync(PaymentRunRequest request, long proposedByUserId, CancellationToken ct = default)
        => _db.AtomicAsync(() => SaveProposalCoreAsync(request, proposedByUserId, ct), ct);

    private async Task<Result<SavedPaymentRun>> SaveProposalCoreAsync(PaymentRunRequest request, long proposedByUserId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.BankAccountCode))
        {
            return Error.Validation("Ap.RunBank", "A payment run needs the bank account it pays from.");
        }

        Result<PaymentRunProposal> proposal = await ProposeAsync(request, ct).ConfigureAwait(false);
        if (proposal.IsFailure)
        {
            return proposal.Error;
        }

        string prefix = $"PRUN-{request.PaymentDate:yyyyMMdd}-";
        int count = await _db.Set<PaymentRunBatch>().CountAsync(b => b.CompanyId == request.CompanyId && b.Number.StartsWith(prefix), ct).ConfigureAwait(false);
        PaymentRunBatch batch = new(request.CompanyId, $"{prefix}{count + 1:D3}", request.PaymentDate, request.DueOnOrBefore, request.BankAccountCode.Trim(), proposedByUserId)
        {
            WithholdingTaxCode = string.IsNullOrWhiteSpace(request.WithholdingTaxCode) ? null : request.WithholdingTaxCode.Trim(),
            EftLayoutCode = string.IsNullOrWhiteSpace(request.EftLayoutCode) ? null : request.EftLayoutCode.Trim(),
            Currency = string.IsNullOrWhiteSpace(request.Currency) ? null : request.Currency.Trim().ToUpperInvariant(),
            Method = request.Method,
            FirstChequeNumber = string.IsNullOrWhiteSpace(request.FirstChequeNumber) ? null : request.FirstChequeNumber.Trim(),
        };
        foreach (PaymentProposalLine line in proposal.Value.Lines.Where(l => l.ToPay > 0m || l.OnHold || l.BankChangePending).OrderBy(l => l.SupplierCode, StringComparer.Ordinal))
        {
            string? held = line.OnHold ? "Supplier on hold" : line.BankChangePending ? "Bank details changing" : null;
            batch.AddLine(new PaymentRunBatchLine(line.SupplierId, line.SupplierCode, line.Currency, line.ToPay, held is null && line.ToPay > 0m, held));
        }

        if (batch.Lines.Count == 0)
        {
            return Error.Rule("Ap.RunEmpty", $"Nothing is due on or before {request.DueOnOrBefore:yyyy-MM-dd}.");
        }

        _db.Set<PaymentRunBatch>().Add(batch);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        // The company's payment-run workflow, when defined, governs the approval from here.
        Result<bool> governed = await Workflow.WorkflowGate.StartIfDefinedAsync(_db, _workflow, request.CompanyId, Application.Abstractions.Workflow.WorkflowDocumentTypes.PaymentRun, batch.Number,
            batch.Lines.Where(l => l.Included).Sum(l => l.Amount), proposedByUserId,
            [("Currency", string.Join(",", batch.Lines.Where(l => l.Included).Select(l => l.Currency).Distinct().Order(StringComparer.Ordinal)))], ct).ConfigureAwait(false);
        return governed.IsFailure ? governed.Error : View(batch);
    }

    /// <inheritdoc />
    public async Task<Result<SavedPaymentRun>> SetLineAsync(long companyId, string runNumber, string supplierCode, string currency, bool included, decimal? amount, CancellationToken ct = default)
    {
        Result<PaymentRunBatch> found = await ProposedAsync(companyId, runNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        string code = (supplierCode ?? string.Empty).Trim().ToUpperInvariant(), cur = (currency ?? string.Empty).Trim().ToUpperInvariant();
        PaymentRunBatchLine? line = found.Value.Lines.FirstOrDefault(l => l.SupplierCode.ToUpperInvariant() == code && l.Currency == cur);
        if (line is null)
        {
            return Error.NotFound("Ap.RunLineNotFound", $"{code} ({cur}) is not in run {runNumber}.");
        }

        if (included && line.Note is not null)
        {
            return Error.Rule("Ap.RunLineHeld", $"{code} is held ({line.Note}); release it and propose again.");
        }

        Result set = line.Set(included, amount);
        if (set.IsFailure)
        {
            return set.Error;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return View(found.Value);
    }

    /// <inheritdoc />
    public async Task<Result<PaymentRunResult>> ApproveAsync(long companyId, string runNumber, long approvedByUserId, CancellationToken ct = default)
    {
        string number = (runNumber ?? string.Empty).Trim().ToUpperInvariant();
        return await Workflow.WorkflowGate.InProgressAsync(_db, companyId, Application.Abstractions.Workflow.WorkflowDocumentTypes.PaymentRun, number, ct).ConfigureAwait(false)
            ? Workflow.WorkflowGate.Governed("Payment run", number)
            : await ApproveFromWorkflowAsync(companyId, number, approvedByUserId, ct).ConfigureAwait(false);
    }

    /// <summary>Approves and pays a run (also the end of its workflow).</summary>
    internal Task<Result<PaymentRunResult>> ApproveFromWorkflowAsync(long companyId, string runNumber, long approvedByUserId, CancellationToken ct)
        => _db.AtomicAsync(async () =>
        {
            Result<PaymentRunBatch> found = await ProposedAsync(companyId, runNumber, ct).ConfigureAwait(false);
            if (found.IsFailure)
            {
                return found.Error;
            }

            PaymentRunBatch batch = found.Value;
            if (batch.ProposedByUserId == approvedByUserId)
            {
                return Error.Rule("Ap.RunSelfApproval", $"Run {batch.Number} must be approved by someone other than who proposed it.");
            }

            Dictionary<(long SupplierId, string Currency), decimal> agreed = batch.Lines.Where(l => l.Included && l.Amount > 0m).ToDictionary(l => (l.SupplierId, l.Currency), l => l.Amount);
            if (agreed.Count == 0)
            {
                return Error.Rule("Ap.RunEmpty", $"Run {batch.Number} pays nobody.");
            }

            Result<PaymentRunResult> paid = await PayAsync(new PaymentRunRequest
            {
                CompanyId = companyId, PaymentDate = batch.PaymentDate, DueOnOrBefore = batch.DueOnOrBefore, BankAccountCode = batch.BankAccountCode, SupplierIds = [.. agreed.Keys.Select(k => k.SupplierId).Distinct()],
                WithholdingTaxCode = batch.WithholdingTaxCode, EftLayoutCode = batch.EftLayoutCode, Currency = batch.Currency, Method = batch.Method, FirstChequeNumber = batch.FirstChequeNumber,
            }, agreed, ct).ConfigureAwait(false);
            if (paid.IsFailure)
            {
                return paid.Error;
            }

            foreach (PaymentRunLine line in paid.Value.Lines.Where(l => l.PaymentNumber.Length > 0))
            {
                if (batch.Lines.FirstOrDefault(b => b.SupplierId == line.SupplierId && b.Currency == line.Currency) is PaymentRunBatchLine saved)
                {
                    saved.PaymentNumber = line.PaymentNumber;
                }
            }

            batch.MarkPaid(approvedByUserId);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return paid;
        }, ct);

    /// <inheritdoc />
    public async Task<Result> CancelAsync(long companyId, string runNumber, CancellationToken ct = default)
    {
        string governedNumber = (runNumber ?? string.Empty).Trim().ToUpperInvariant();
        if (await Workflow.WorkflowGate.InProgressAsync(_db, companyId, Application.Abstractions.Workflow.WorkflowDocumentTypes.PaymentRun, governedNumber, ct).ConfigureAwait(false))
        {
            return Result.Failure(Workflow.WorkflowGate.Governed("Payment run", governedNumber));
        }

        return await CancelFromWorkflowAsync(companyId, runNumber, ct).ConfigureAwait(false);
    }

    /// <summary>Withdraws a run (also a rejected workflow's end).</summary>
    internal async Task<Result> CancelFromWorkflowAsync(long companyId, string runNumber, CancellationToken ct)
    {
        Result<PaymentRunBatch> found = await ProposedAsync(companyId, runNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        found.Value.Cancel();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<SavedPaymentRun>> GetAsync(long companyId, string runNumber, CancellationToken ct = default)
    {
        string number = (runNumber ?? string.Empty).Trim().ToUpperInvariant();
        PaymentRunBatch? batch = await _db.Set<PaymentRunBatch>().AsNoTracking().Include(b => b.Lines).FirstOrDefaultAsync(b => b.CompanyId == companyId && b.Number == number, ct).ConfigureAwait(false);
        return batch is null ? Error.NotFound("Ap.RunNotFound", $"Payment run {number} not found.") : View(batch);
    }

    private async Task<Result<PaymentRunBatch>> ProposedAsync(long companyId, string runNumber, CancellationToken ct)
    {
        string number = (runNumber ?? string.Empty).Trim().ToUpperInvariant();
        PaymentRunBatch? batch = await _db.Set<PaymentRunBatch>().Include(b => b.Lines).FirstOrDefaultAsync(b => b.CompanyId == companyId && b.Number == number, ct).ConfigureAwait(false);
        return batch is null ? Error.NotFound("Ap.RunNotFound", $"Payment run {number} not found.")
            : batch.Status != PaymentRunStatus.Proposed ? Error.Rule("Ap.RunNotProposed", $"Payment run {number} is {batch.Status}.")
            : batch;
    }

    private static SavedPaymentRun View(PaymentRunBatch batch)
        => new(batch.Number, batch.Status, batch.PaymentDate, [.. batch.Lines.OrderBy(l => l.SupplierCode, StringComparer.Ordinal)
            .Select(l => new SavedPaymentLine(l.SupplierCode, l.Currency, l.Due, l.Amount, l.Included, l.Note, l.PaymentNumber))]);

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

        if (!string.IsNullOrWhiteSpace(request.Currency))
        {
            string currency = request.Currency.Trim().ToUpperInvariant();
            dueInvoices = [.. dueInvoices.Where(i => i.Currency.Value == currency)];
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

    // An ISO 20022 pain.001 credit transfer file: the company pays from the bank account kept on the run's ledger account.
    private async Task<byte[]> Pain001FileAsync(PaymentRunRequest request, List<PaymentRunLine> lines, IReadOnlyDictionary<long, Supplier> suppliers, CancellationToken ct)
    {
        string company = await _db.Companies.Where(c => c.Id == request.CompanyId).Select(c => c.Name).FirstAsync(ct).ConfigureAwait(false);
        string debtorAccount = await _db.BankAccounts.AsNoTracking().Where(b => b.CompanyId == request.CompanyId && b.GlAccountCode == request.BankAccountCode && b.AccountNumber != null)
            .Select(b => b.AccountNumber!).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? request.BankAccountCode;
        List<Application.Banking.Pain001Payment> payments = [.. lines.Select(l => new Application.Banking.Pain001Payment(l.PaymentNumber,
            new Application.Banking.Pain001Party(suppliers[l.SupplierId].BankAccountName ?? suppliers[l.SupplierId].Name, suppliers[l.SupplierId].BankAccountNumber ?? string.Empty),
            l.Currency.ToString(), l.NetPaid, $"Payment {l.PaymentNumber} to {suppliers[l.SupplierId].Code}"))];
        string messageId = $"{request.BankAccountCode}-{request.PaymentDate:yyyyMMdd}-{lines.Select(l => l.PaymentNumber).FirstOrDefault()}";
        return Encoding.UTF8.GetBytes(Application.Banking.Pain001.Write(messageId, DateTimeOffset.UtcNow, request.PaymentDate, new Application.Banking.Pain001Party(company, debtorAccount), payments));
    }

    // The bulk mobile-money payout file: a header with the count and total, then one line per wallet.
    private static byte[] BuildMobileMoneyFile(PaymentRunRequest request, List<PaymentRunLine> lines, IReadOnlyDictionary<long, Supplier> suppliers)
    {
        StringBuilder sb = new();
        sb.AppendLine($"HDR-MM|{request.PaymentDate:yyyyMMdd}|{lines.Count}|{lines.Sum(l => l.NetPaid).ToString("0.00", CultureInfo.InvariantCulture)}");
        foreach (PaymentRunLine line in lines)
        {
            Supplier supplier = suppliers[line.SupplierId];
            sb.AppendLine(string.Join('|', "MM", line.PaymentNumber, supplier.Code, supplier.Name, supplier.MobileMoneyNumber ?? string.Empty, line.Currency,
                line.NetPaid.ToString("0.00", CultureInfo.InvariantCulture)));
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
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

/// <summary>Applies the end of a payment run's workflow: approved → paid as approved by the last approver; rejected or returned → withdrawn.</summary>
internal sealed class PaymentRunWorkflowHandler : Application.Abstractions.Workflow.IWorkflowDocumentHandler
{
    private readonly PaymentRunService _runs;

    public PaymentRunWorkflowHandler(PaymentRunService runs) => _runs = runs;

    public string DocumentType => Application.Abstractions.Workflow.WorkflowDocumentTypes.PaymentRun;

    public async Task<Result> ConcludeAsync(long companyId, string documentReference, Core.Workflow.WorkflowStatus outcome, long decidedByUserId, string? comment, CancellationToken ct = default)
    {
        if (outcome != Core.Workflow.WorkflowStatus.Approved)
        {
            Result cancelled = await _runs.CancelFromWorkflowAsync(companyId, documentReference, ct).ConfigureAwait(false);
            return cancelled.IsFailure && cancelled.Error.Code == "Ap.RunNotFound" ? Result.Success() : cancelled;
        }

        Result<PaymentRunResult> paid = await _runs.ApproveFromWorkflowAsync(companyId, documentReference, decidedByUserId, ct).ConfigureAwait(false);
        return paid.IsFailure && paid.Error.Code != "Ap.RunNotFound" ? Result.Failure(paid.Error) : Result.Success();
    }
}
