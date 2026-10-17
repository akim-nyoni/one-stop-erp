using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Receipting;
using One4All.Core.Accounting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Common;
using One4All.Core.Receipting;
using One4All.Core.System;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using RcpReceiptRequest = One4All.Application.Abstractions.Receipting.ReceiptRequest;
using TradingReceiptRequest = One4All.Application.Abstractions.Trading.ReceiptRequest;

namespace One4All.Infrastructure.Receipting;

/// <summary>
/// Cashiering &amp; Receipting (brief §RCP-1/RCP-2). Posts receipts through the central engine using the §14
/// posting patterns - a debtor payment credits the AR control (never income) and allocates oldest-first;
/// sundry credits the income vote; deposits credit the deposit liability; unidentified money credits
/// suspense - and runs the cashier session with a blind cash-up that posts the variance to shortages.
/// </summary>
public sealed class ReceiptingService : IReceiptingService
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _posting;
    private readonly ISalesLedgerService _salesLedger;
    private readonly IAllocationService _allocation;
    private readonly Application.Abstractions.Security.IAuditService? _audit;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="posting">The central posting engine.</param>
    /// <param name="salesLedger">The AR ledger (debtor receipts).</param>
    /// <param name="allocation">The allocation service (match receipt to invoices).</param>
    /// <param name="audit">Logs a refused receipt against a cashier's own or related account.</param>
    public ReceiptingService(One4AllDbContext db, IPostingEngine posting, ISalesLedgerService salesLedger, IAllocationService allocation,
        Application.Abstractions.Security.IAuditService? audit = null)
    {
        _audit = audit;
        _db = db;
        _posting = posting;
        _salesLedger = salesLedger;
        _allocation = allocation;
    }

    /// <inheritdoc />
    public async Task<Result<long>> OpenSessionAsync(long companyId, long stationId, long cashierUserId, DateOnly date, IReadOnlyList<FloatDeclaration> floats, CancellationToken ct = default)
    {
        CashierStation? station = await _db.Set<CashierStation>().FirstOrDefaultAsync(s => s.Id == stationId && s.CompanyId == companyId, ct).ConfigureAwait(false);
        if (station is null)
        {
            return Error.NotFound("Rcp.StationNotFound", $"Station {stationId} not found.");
        }

        bool alreadyOpen = await _db.Set<CashierSession>()
            .AnyAsync(s => s.CompanyId == companyId && s.CashierUserId == cashierUserId && s.Status == SessionStatus.Open, ct).ConfigureAwait(false);
        if (alreadyOpen)
        {
            return Error.Rule("Rcp.SessionAlreadyOpen", "The cashier already has an open session.");
        }

        CashierSession session = new(companyId, stationId, cashierUserId, date);
        foreach (FloatDeclaration f in floats)
        {
            session.DeclareFloat(f.Currency, f.Amount);
        }

        _db.Set<CashierSession>().Add(session);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return session.Id;
    }

    /// <inheritdoc />
    public async Task<Result<ReceiptResult>> PostReceiptAsync(RcpReceiptRequest request, CancellationToken ct = default)
    {
        // A cashier may not receipt against their own or a related account (receipting §15); the attempt is logged outside the
        // receipt's unit of work so that it survives the refusal.
        if (request.PayerAccountId is long payer)
        {
            long? cashier = await _db.Set<CashierSession>().AsNoTracking().Where(s => s.Id == request.SessionId && s.CompanyId == request.CompanyId).Select(s => (long?)s.CashierUserId)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
            string? relationship = cashier is long user ? await _db.Set<CashierRelatedAccount>().AsNoTracking()
                .Where(r => r.CompanyId == request.CompanyId && r.UserId == user && r.CustomerId == payer).Select(r => r.Relationship).FirstOrDefaultAsync(ct).ConfigureAwait(false) : null;
            if (relationship is not null)
            {
                if (_audit is not null)
                {
                    await _audit.RecordAsync(new Application.Abstractions.Security.AuditEvent(request.CompanyId, cashier, $"user {cashier}", "Receipting", "RelatedAccountReceipt",
                        payer.ToString(System.Globalization.CultureInfo.InvariantCulture), Core.Audit.AuditAction.Reject, Reason: relationship,
                        Details: request.Lines.Sum(l => l.Amount).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)), ct).ConfigureAwait(false);
                }

                return Error.Forbidden("Rcp.RelatedAccount", $"This account is the cashier's {relationship.ToLowerInvariant()} account: another cashier must take the payment.");
            }
        }

        return await _db.AtomicAsync(() => PostReceiptCoreAsync(request, ct), ct).ConfigureAwait(false);
    }

    // One unit of work: its postings and record changes commit together or not at all.
    private async Task<Result<ReceiptResult>> PostReceiptCoreAsync(RcpReceiptRequest request, CancellationToken ct)
    {
        if (request.Lines.Count == 0)
        {
            return Error.Validation("Rcp.NoLines", "A receipt must have at least one line.");
        }

        if (request.Lines.Any(l => l.Amount <= 0m))
        {
            return Error.Rule("Rcp.NonPositiveLine", "Receipt lines must be positive - no zero or negative receipts.");
        }

        decimal total = request.Lines.Sum(l => l.Amount);
        decimal tenderTotal = request.Tenders.Sum(t => t.Amount);
        if (tenderTotal != total)
        {
            return Error.Rule("Rcp.TenderMismatch", $"Tenders ({tenderTotal}) do not equal the receipt total ({total}).");
        }

        bool hasDebtor = request.Lines.Any(l => l.Type == ReceiptLineType.DebtorPayment);
        bool hasCoded = request.Lines.Any(l => l.Type != ReceiptLineType.DebtorPayment);
        if (hasDebtor && request.PayerAccountId is null)
        {
            return Error.Rule("Rcp.NoPayerAccount", "A debtor-payment receipt requires a payer account.");
        }

        if (!hasDebtor && !hasCoded)
        {
            return Error.Rule("Rcp.NothingToPost", "A receipt needs either a payer account or a sundry/deposit/suspense code.");
        }

        // Money directed against the default policy (oldest first) needs the cashier's reason; it is reported on RC19.
        bool overridden = hasDebtor && request.AllocationStrategy != ReceiptAllocationStrategy.OldestFirst;
        if (overridden && string.IsNullOrWhiteSpace(request.AllocationReason))
        {
            return Error.Validation("Rcp.OverrideReason", "Give the reason for allocating other than oldest first.");
        }

        CashierSession? session = await _db.Set<CashierSession>().FirstOrDefaultAsync(s => s.Id == request.SessionId && s.CompanyId == request.CompanyId, ct).ConfigureAwait(false);
        if (session is null)
        {
            return Error.NotFound("Rcp.SessionNotFound", $"Session {request.SessionId} not found.");
        }

        if (session.Status != SessionStatus.Open)
        {
            return Error.Rule("Rcp.SessionNotOpen", "The session is not open.");
        }

        Company? company = await _db.Set<Company>().FirstOrDefaultAsync(c => c.Id == request.CompanyId, ct).ConfigureAwait(false);
        if (company is null)
        {
            return Error.NotFound("Rcp.CompanyNotFound", $"Company {request.CompanyId} not found.");
        }

        CashierStation station = await _db.Set<CashierStation>().FirstAsync(s => s.Id == session.StationId, ct).ConfigureAwait(false);
        Result<string> number = await NextReceiptNumberAsync(request.CompanyId, station, ct).ConfigureAwait(false);
        if (number.IsFailure)
        {
            return number.Error;
        }

        string receiptNumber = number.Value;
        CurrencyCode currency = request.Currency ?? company.BaseCurrency;

        // A pending receipt (a claimed bank transfer/cheque) posts to the uncleared-effects control until the
        // bank statement confirms it, rather than straight to cash/bank.
        bool pending = request.Pending && !string.IsNullOrWhiteSpace(request.PendingAccountCode);
        string cashCode = pending ? request.PendingAccountCode! : request.CashAccountCode;

        Receipt receipt = new(request.CompanyId, session.Id, session.StationId, session.CashierUserId, receiptNumber, request.Date, request.PayerKind, request.PayerAccountId, currency)
        {
            StartedOnUtc = request.StartedAtUtc,
        };
        foreach (ReceiptLineRequest line in request.Lines)
        {
            receipt.AddLine(line.Type, line.Amount, line.AccountCode, line.Narrative, line.Service);
        }

        foreach (ReceiptTenderRequest tender in request.Tenders)
        {
            receipt.AddTender(tender.Type, currency, tender.Amount, tender.Reference);
        }

        if (overridden)
        {
            receipt.RecordAllocationOverride(request.AllocationStrategy.ToString(), request.AllocationReason!.Trim());
        }

        _db.Set<Receipt>().Add(receipt);

        decimal allocatedToDebt = 0m;
        decimal creditOnAccount = 0m;

        // Debtor payments → Dr cash / Cr AR control (via the AR ledger), then allocate oldest-first.
        decimal debtorTotal = request.Lines.Where(l => l.Type == ReceiptLineType.DebtorPayment).Sum(l => l.Amount);
        if (debtorTotal > 0m)
        {
            Result<TradingPostingResult> receiptPosting = await _salesLedger.PostReceiptAsync(new TradingReceiptRequest
            {
                CompanyId = request.CompanyId,
                CustomerId = request.PayerAccountId!.Value,
                DocumentNumber = receiptNumber,
                ReceiptDate = request.Date,
                Currency = currency,
                Amount = debtorTotal,
                BankAccountCode = cashCode,
                ExchangeRate = request.ExchangeRate,
                Narrative = $"Receipt {receiptNumber}",
            }, ct).ConfigureAwait(false);

            if (receiptPosting.IsFailure)
            {
                return receiptPosting.Error;
            }

            receipt.DebtorOpenItemId = receiptPosting.Value.OpenItemId;
            receipt.CashAccountCode = cashCode;

            // A payment in the base currency allocates here against the account's charges; a foreign-currency payment allocates here too, its base
            // value settling base-currency charges at the receipt's rate (charges in other currencies wait for the allocation workbench).
            (allocatedToDebt, creditOnAccount) = currency == company.BaseCurrency
                ? await AllocateAsync(request.CompanyId, request.PayerAccountId!.Value, receiptPosting.Value.OpenItemId, debtorTotal, request.Date, request.AllocationStrategy, request.DirectedAllocations, ct).ConfigureAwait(false)
                : await AllocateForeignAsync(request.CompanyId, request.PayerAccountId!.Value, receiptPosting.Value.OpenItemId, debtorTotal, company.BaseCurrency, request.Date, request.AllocationStrategy, request.DirectedAllocations, ct).ConfigureAwait(false);
        }

        // Sundry / deposit / suspense → Dr cash / Cr the coded account (never a guessed income account).
        List<ReceiptLineRequest> codedLines = request.Lines.Where(l => l.Type != ReceiptLineType.DebtorPayment).ToList();
        if (codedLines.Count > 0)
        {
            decimal codedTotal = codedLines.Sum(l => l.Amount);
            List<JournalLineRequest> journalLines =
            [
                new JournalLineRequest { AccountCode = cashCode, Side = JournalSide.Debit, TransactionAmount = codedTotal },
            ];
            foreach (ReceiptLineRequest line in codedLines)
            {
                if (string.IsNullOrWhiteSpace(line.AccountCode))
                {
                    return Error.Validation("Rcp.CodeRequired", $"A {line.Type} line requires an account code.");
                }

                journalLines.Add(new JournalLineRequest { AccountCode = line.AccountCode, Side = JournalSide.Credit, TransactionAmount = line.Amount, Narrative = line.Narrative });
            }

            Result<PostingReceipt> posted = await _posting.PostAsync(new JournalRequest
            {
                CompanyId = request.CompanyId,
                SourceModule = "GL",
                PostingDate = request.Date,
                TransactionCurrency = currency,
                DocumentType = "RECEIPT",
                DocumentNumber = receiptNumber,
                Narrative = $"Receipt {receiptNumber}",
                Lines = journalLines,
            }, ct).ConfigureAwait(false);

            if (posted.IsFailure)
            {
                return posted.Error;
            }

            receipt.JournalNumber = posted.Value.JournalNumber;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        // Log each tender as pending (unconfirmed) so it clears to the bank on statement confirmation.
        if (pending)
        {
            foreach (ReceiptTenderRequest tender in request.Tenders)
            {
                _db.Set<PendingTender>().Add(new PendingTender(request.CompanyId, receipt.Id, receiptNumber, tender.Type, tender.Reference, tender.Amount, request.Date, cashCode));
            }

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return new ReceiptResult(receipt.Id, receiptNumber, total, allocatedToDebt, creditOnAccount);
    }

    /// <inheritdoc />
    public Task<Result<ReceiptReversalResult>> ReverseReceiptAsync(long companyId, long receiptId, DateOnly date, long reverserUserId, string reason, CancellationToken ct = default)
        => _db.AtomicAsync(() => ReverseReceiptCoreAsync(companyId, receiptId, date, reverserUserId, reason, ct), ct);

    // One unit of work: its postings and record changes commit together or not at all.
    private async Task<Result<ReceiptReversalResult>> ReverseReceiptCoreAsync(long companyId, long receiptId, DateOnly date, long reverserUserId, string reason, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("Rcp.ReasonRequired", "Reversing a receipt requires a reason.");
        }

        Receipt? receipt = await _db.Set<Receipt>().Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == receiptId && r.CompanyId == companyId, ct).ConfigureAwait(false);
        if (receipt is null)
        {
            return Error.NotFound("Rcp.ReceiptNotFound", $"Receipt {receiptId} not found.");
        }

        if (receipt.Status != ReceiptStatus.Committed)
        {
            return Error.Rule("Rcp.NotReversible", "Only a committed receipt can be reversed.");
        }

        if (receipt.CashierUserId == reverserUserId)
        {
            await LogViolationAsync(companyId, reverserUserId, $"Attempted to reverse own receipt {receipt.ReceiptNumber}", date, ct).ConfigureAwait(false);
            return Error.Rule("Rcp.SelfReverse", "A cashier cannot reverse their own receipt (segregation of duties).");
        }

        if (string.IsNullOrWhiteSpace(receipt.CashAccountCode))
        {
            return Error.Rule("Rcp.NoCashAccount", "The receipt has no recorded cash account to reverse against.");
        }

        string cashAccount = receipt.CashAccountCode;

        // Debtor portion: un-allocate the receipt's matches, then post the exact opposite (Dr AR / Cr cash)
        // and net the reversal debit against the restored receipt credit so both settle to nil.
        string? debtorJournal = null;
        decimal debtorTotal = receipt.Lines.Where(l => l.Type == ReceiptLineType.DebtorPayment).Sum(l => l.Amount);
        if (receipt.DebtorOpenItemId is { } creditItemId && debtorTotal > 0m)
        {
            List<long> allocationIds = await _db.Set<Allocation>()
                .Where(a => a.CompanyId == companyId && a.CreditItemId == creditItemId && !a.IsReversed)
                .Select(a => a.Id)
                .ToListAsync(ct).ConfigureAwait(false);

            foreach (long allocationId in allocationIds)
            {
                Result<UnallocationResult> un = await _allocation.UnallocateAsync(companyId, allocationId, date, ct).ConfigureAwait(false);
                if (un.IsFailure)
                {
                    return un.Error;
                }
            }

            Result<TradingPostingResult> reversal = await _salesLedger.PostReceiptReversalAsync(companyId, receipt.PayerAccountId!.Value, receipt.ReceiptNumber + "-REV", date, receipt.Currency, debtorTotal, cashAccount, ct: ct).ConfigureAwait(false);
            if (reversal.IsFailure)
            {
                return reversal.Error;
            }

            debtorJournal = reversal.Value.JournalNumber;

            Result<AllocationResult> match = await _allocation.AllocateAsync(companyId, TradingLedger.AccountsReceivable, reversal.Value.OpenItemId, creditItemId, debtorTotal, date, ct).ConfigureAwait(false);
            if (match.IsFailure)
            {
                return match.Error;
            }
        }

        // Coded portion: mirror the original journal - Cr cash, Dr each income/deposit/suspense account.
        string? codedJournal = null;
        List<ReceiptLine> codedLines = receipt.Lines.Where(l => l.Type != ReceiptLineType.DebtorPayment).ToList();
        if (codedLines.Count > 0)
        {
            decimal codedTotal = codedLines.Sum(l => l.Amount);
            List<JournalLineRequest> journalLines =
            [
                new JournalLineRequest { AccountCode = cashAccount, Side = JournalSide.Credit, TransactionAmount = codedTotal },
            ];
            journalLines.AddRange(codedLines.Select(l => new JournalLineRequest { AccountCode = l.AccountCode!, Side = JournalSide.Debit, TransactionAmount = l.Amount, Narrative = l.Narrative }));

            Result<PostingReceipt> posted = await _posting.PostAsync(new JournalRequest
            {
                CompanyId = companyId,
                SourceModule = "GL",
                PostingDate = date,
                TransactionCurrency = receipt.Currency,
                DocumentType = "RECEIPT-REV",
                DocumentNumber = receipt.ReceiptNumber + "-REV",
                Narrative = $"Receipt reversal: {reason}",
                Lines = journalLines,
            }, ct).ConfigureAwait(false);

            if (posted.IsFailure)
            {
                return posted.Error;
            }

            codedJournal = posted.Value.JournalNumber;
        }

        receipt.Cancel(reason, reverserUserId);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new ReceiptReversalResult(receipt.Id, receipt.LineTotal, debtorJournal, codedJournal);
    }

    /// <inheritdoc />
    public Task<Result<RefundResult>> RefundCreditAsync(long companyId, long customerId, decimal amount, DateOnly date, string bankAccountCode, long approverUserId, string reason, CancellationToken ct = default)
        => _db.AtomicAsync(() => RefundCreditCoreAsync(companyId, customerId, amount, date, bankAccountCode, approverUserId, reason, ct), ct);

    // One unit of work: its postings and record changes commit together or not at all.
    private async Task<Result<RefundResult>> RefundCreditCoreAsync(long companyId, long customerId, decimal amount, DateOnly date, string bankAccountCode, long approverUserId, string reason, CancellationToken ct)
    {
        if (amount <= 0m)
        {
            return Error.Rule("Rcp.NonPositiveAmount", "The refund amount must be positive.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("Rcp.ReasonRequired", "A refund requires a reason.");
        }

        Company? company = await _db.Set<Company>().FirstOrDefaultAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        if (company is null)
        {
            return Error.NotFound("Rcp.CompanyNotFound", $"Company {companyId} not found.");
        }

        List<OpenItem> credits = await _db.Set<OpenItem>()
            .Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable && i.TraderId == customerId && i.Sign == LedgerSign.Credit && i.OutstandingTransaction > 0m)
            .OrderBy(i => i.DocumentDate).ThenBy(i => i.Id)
            .ToListAsync(ct).ConfigureAwait(false);

        decimal available = credits.Sum(i => i.OutstandingTransaction);
        if (amount > available + 0.0001m)
        {
            return Error.Rule("Rcp.InsufficientCredit", $"Refund {amount} exceeds the available credit {available} on the account.");
        }

        // Dr AR control / Cr bank (paid from the bank/safe), creating a debit adjustment.
        Result<TradingPostingResult> reversal = await _salesLedger.PostReceiptReversalAsync(companyId, customerId, $"REFUND-{customerId}-{date:yyyyMMdd}", date, company.BaseCurrency, amount, bankAccountCode, ct: ct).ConfigureAwait(false);
        if (reversal.IsFailure)
        {
            return reversal.Error;
        }

        // Clear the payer's credit items against the refund debit (oldest first).
        decimal remaining = amount;
        foreach (OpenItem credit in credits)
        {
            if (remaining <= 0m)
            {
                break;
            }

            decimal take = Math.Min(remaining, credit.OutstandingTransaction);
            Result<AllocationResult> alloc = await _allocation.AllocateAsync(companyId, TradingLedger.AccountsReceivable, reversal.Value.OpenItemId, credit.Id, take, date, ct).ConfigureAwait(false);
            if (alloc.IsFailure)
            {
                return alloc.Error;
            }

            remaining -= take;
        }

        string payerName = await _db.Set<Customer>().Where(c => c.Id == customerId).Select(c => c.Name).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? $"#{customerId}";
        _db.Set<RefundRecord>().Add(new RefundRecord(companyId, RefundType.CreditBalance, payerName, amount, date, reason, approverUserId, reversal.Value.JournalNumber));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new RefundResult(amount, reversal.Value.JournalNumber);
    }

    /// <inheritdoc />
    public Task<Result<RefundResult>> RefundDepositAsync(long companyId, decimal amount, decimal forfeitAmount, DateOnly date, string depositLiabilityAccountCode, string bankAccountCode, string? forfeitAccountCode, long approverUserId, string reason, CancellationToken ct = default)
        => _db.AtomicAsync(() => RefundDepositCoreAsync(companyId, amount, forfeitAmount, date, depositLiabilityAccountCode, bankAccountCode, forfeitAccountCode, approverUserId, reason, ct), ct);

    // One unit of work: its postings and record changes commit together or not at all.
    private async Task<Result<RefundResult>> RefundDepositCoreAsync(long companyId, decimal amount, decimal forfeitAmount, DateOnly date, string depositLiabilityAccountCode, string bankAccountCode, string? forfeitAccountCode, long approverUserId, string reason, CancellationToken ct)
    {
        if (amount <= 0m)
        {
            return Error.Rule("Rcp.NonPositiveAmount", "The deposit amount must be positive.");
        }

        if (forfeitAmount < 0m || forfeitAmount > amount)
        {
            return Error.Rule("Rcp.InvalidForfeit", "The forfeit must be between zero and the deposit amount.");
        }

        if (forfeitAmount > 0m && string.IsNullOrWhiteSpace(forfeitAccountCode))
        {
            return Error.Validation("Rcp.ForfeitAccountRequired", "A forfeit requires an income account.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("Rcp.ReasonRequired", "A deposit refund requires a reason.");
        }

        Company? company = await _db.Set<Company>().FirstOrDefaultAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        if (company is null)
        {
            return Error.NotFound("Rcp.CompanyNotFound", $"Company {companyId} not found.");
        }

        decimal net = amount - forfeitAmount;
        List<JournalLineRequest> lines =
        [
            new JournalLineRequest { AccountCode = depositLiabilityAccountCode, Side = JournalSide.Debit, TransactionAmount = amount },
        ];
        if (net > 0m)
        {
            lines.Add(new JournalLineRequest { AccountCode = bankAccountCode, Side = JournalSide.Credit, TransactionAmount = net });
        }

        if (forfeitAmount > 0m)
        {
            lines.Add(new JournalLineRequest { AccountCode = forfeitAccountCode!, Side = JournalSide.Credit, TransactionAmount = forfeitAmount });
        }

        Result<PostingReceipt> posted = await _posting.PostAsync(new JournalRequest
        {
            CompanyId = companyId,
            SourceModule = "GL",
            PostingDate = date,
            TransactionCurrency = company.BaseCurrency,
            DocumentType = "DEP-REFUND",
            Narrative = $"Deposit refund: {reason}",
            Lines = lines,
        }, ct).ConfigureAwait(false);

        if (posted.IsFailure)
        {
            return posted.Error;
        }

        _db.Set<RefundRecord>().Add(new RefundRecord(companyId, RefundType.Deposit, "Deposit refund", net, date, reason, approverUserId, posted.Value.JournalNumber));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new RefundResult(net, posted.Value.JournalNumber);
    }

    /// <inheritdoc />
    public Task<Result<ReceiptResult>> AllocateFromSuspenseAsync(long companyId, long customerId, decimal amount, DateOnly date, string suspenseAccountCode, ReceiptAllocationStrategy strategy, string reason, CancellationToken ct = default)
        => _db.AtomicAsync(() => AllocateFromSuspenseCoreAsync(companyId, customerId, amount, date, suspenseAccountCode, strategy, reason, ct), ct);

    // One unit of work: its postings and record changes commit together or not at all.
    private async Task<Result<ReceiptResult>> AllocateFromSuspenseCoreAsync(long companyId, long customerId, decimal amount, DateOnly date, string suspenseAccountCode, ReceiptAllocationStrategy strategy, string reason, CancellationToken ct)
    {
        if (amount <= 0m)
        {
            return Error.Rule("Rcp.NonPositiveAmount", "The amount to move out of suspense must be positive.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("Rcp.ReasonRequired", "Allocating from suspense requires a reason.");
        }

        Company? company = await _db.Set<Company>().FirstOrDefaultAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        if (company is null)
        {
            return Error.NotFound("Rcp.CompanyNotFound", $"Company {companyId} not found.");
        }

        // Dr suspense control / Cr AR control (via the AR ledger), then apply to the account's charges.
        Result<TradingPostingResult> posting = await _salesLedger.PostReceiptAsync(new TradingReceiptRequest
        {
            CompanyId = companyId,
            CustomerId = customerId,
            DocumentNumber = $"SUS-{customerId}-{date:yyyyMMdd}",
            ReceiptDate = date,
            Currency = company.BaseCurrency,
            Amount = amount,
            BankAccountCode = suspenseAccountCode,
            Narrative = $"Suspense allocation: {reason}",
        }, ct).ConfigureAwait(false);

        if (posting.IsFailure)
        {
            return posting.Error;
        }

        (decimal allocated, decimal remainder) = await AllocateAsync(companyId, customerId, posting.Value.OpenItemId, amount, date, strategy, [], ct).ConfigureAwait(false);
        _db.Set<SuspenseAllocation>().Add(new SuspenseAllocation(companyId, customerId, amount, date, reason, posting.Value.JournalNumber));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new ReceiptResult(0, posting.Value.JournalNumber, amount, allocated, remainder);
    }

    /// <inheritdoc />
    public Task<Result<string>> PickupAsync(long companyId, long sessionId, decimal amount, DateOnly date, string cashAccountCode, string safeAccountCode, string reason, CancellationToken ct = default) =>
        RecordMovementAsync(companyId, sessionId, CashMovementType.Pickup, amount, date, debitAccountCode: safeAccountCode, creditAccountCode: cashAccountCode, "PICKUP", reason, ct);

    /// <inheritdoc />
    public Task<Result<string>> PettyCashPayoutAsync(long companyId, long sessionId, decimal amount, DateOnly date, string cashAccountCode, string expenseAccountCode, string reason, CancellationToken ct = default) =>
        RecordMovementAsync(companyId, sessionId, CashMovementType.PettyCashPayout, amount, date, debitAccountCode: expenseAccountCode, creditAccountCode: cashAccountCode, "PETTYCASH", reason, ct);

    private async Task<Result<string>> RecordMovementAsync(long companyId, long sessionId, CashMovementType type, decimal amount, DateOnly date, string debitAccountCode, string creditAccountCode, string documentType, string reason, CancellationToken ct)
    {
        if (amount <= 0m)
        {
            return Error.Rule("Rcp.NonPositiveAmount", "The amount must be positive.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("Rcp.ReasonRequired", "A cash movement requires a reason.");
        }

        CashierSession? session = await _db.Set<CashierSession>().FirstOrDefaultAsync(s => s.Id == sessionId && s.CompanyId == companyId, ct).ConfigureAwait(false);
        if (session is null)
        {
            return Error.NotFound("Rcp.SessionNotFound", $"Session {sessionId} not found.");
        }

        if (session.Status != SessionStatus.Open)
        {
            return Error.Rule("Rcp.SessionNotOpen", "Cash movements can only be recorded on an open session.");
        }

        Company? company = await _db.Set<Company>().FirstOrDefaultAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        if (company is null)
        {
            return Error.NotFound("Rcp.CompanyNotFound", $"Company {companyId} not found.");
        }

        Result<PostingReceipt> posted = await _posting.PostAsync(new JournalRequest
        {
            CompanyId = companyId,
            SourceModule = "GL",
            PostingDate = date,
            TransactionCurrency = company.BaseCurrency,
            DocumentType = documentType,
            DocumentNumber = $"{documentType}-{sessionId}",
            Narrative = $"{type}: {reason}",
            Lines =
            [
                new JournalLineRequest { AccountCode = debitAccountCode, Side = JournalSide.Debit, TransactionAmount = amount },
                new JournalLineRequest { AccountCode = creditAccountCode, Side = JournalSide.Credit, TransactionAmount = amount },
            ],
        }, ct).ConfigureAwait(false);

        if (posted.IsFailure)
        {
            return posted.Error;
        }

        _db.Set<SessionCashMovement>().Add(new SessionCashMovement(companyId, sessionId, type, company.BaseCurrency, amount, date, reason));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return posted.Value.JournalNumber;
    }

    /// <inheritdoc />
    public Task<Result<CashUpResult>> CloseSessionAsync(long companyId, long sessionId, DateOnly date, IReadOnlyList<FloatDeclaration> countedCash, string cashAccountCode, string shortagesAccountCode, CancellationToken ct = default)
        => _db.AtomicAsync(() => CloseSessionCoreAsync(companyId, sessionId, date, countedCash, cashAccountCode, shortagesAccountCode, ct), ct);

    // One unit of work: its postings and record changes commit together or not at all.
    private async Task<Result<CashUpResult>> CloseSessionCoreAsync(long companyId, long sessionId, DateOnly date, IReadOnlyList<FloatDeclaration> countedCash, string cashAccountCode, string shortagesAccountCode, CancellationToken ct)
    {
        CashierSession? session = await _db.Set<CashierSession>().Include(s => s.Floats)
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.CompanyId == companyId, ct).ConfigureAwait(false);
        if (session is null)
        {
            return Error.NotFound("Rcp.SessionNotFound", $"Session {sessionId} not found.");
        }

        if (session.Status != SessionStatus.Open)
        {
            return Error.Rule("Rcp.SessionNotOpen", "Only an open session can be closed.");
        }

        Company? company = await _db.Set<Company>().FirstOrDefaultAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        if (company is null)
        {
            return Error.NotFound("Rcp.CompanyNotFound", $"Company {companyId} not found.");
        }

        // Expected base-currency cash = opening float (base) + cash tenders (base) taken this session.
        decimal expectedCash = session.FloatFor(company.BaseCurrency);
        decimal cashTenders = await _db.Set<Receipt>()
            .Where(r => r.CashierSessionId == sessionId && r.Status == ReceiptStatus.Committed)
            .SelectMany(r => r.Tenders)
            .Where(t => t.Type == TenderType.Cash && t.Currency == company.BaseCurrency)
            .SumAsync(t => t.Amount, ct).ConfigureAwait(false);
        expectedCash += cashTenders;

        // Pickups to the safe and petty-cash pay-outs remove cash from the drawer during the shift.
        decimal movements = await _db.Set<SessionCashMovement>()
            .Where(m => m.CashierSessionId == sessionId && m.Currency == company.BaseCurrency)
            .SumAsync(m => m.Amount, ct).ConfigureAwait(false);
        expectedCash -= movements;

        decimal countedBase = countedCash.Where(c => c.Currency == company.BaseCurrency).Sum(c => c.Amount);
        decimal variance = countedBase - expectedCash;

        string? varianceJournal = null;
        if (variance != 0m)
        {
            // Shortage (variance < 0): Dr shortages / Cr cash. Overage (variance > 0): Dr cash / Cr shortages.
            decimal magnitude = Math.Abs(variance);
            JournalSide shortagesSide = variance < 0m ? JournalSide.Debit : JournalSide.Credit;
            JournalSide cashSide = variance < 0m ? JournalSide.Credit : JournalSide.Debit;

            Result<PostingReceipt> posted = await _posting.PostAsync(new JournalRequest
            {
                CompanyId = companyId,
                SourceModule = "GL",
                PostingDate = date,
                TransactionCurrency = company.BaseCurrency,
                DocumentType = "CASHUP",
                DocumentNumber = $"CU-{sessionId}",
                Narrative = $"Cash-up variance session {sessionId}",
                Lines =
                [
                    new JournalLineRequest { AccountCode = shortagesAccountCode, Side = shortagesSide, TransactionAmount = magnitude },
                    new JournalLineRequest { AccountCode = cashAccountCode, Side = cashSide, TransactionAmount = magnitude },
                ],
            }, ct).ConfigureAwait(false);

            if (posted.IsFailure)
            {
                return posted.Error;
            }

            varianceJournal = posted.Value.JournalNumber;
        }

        session.Close(date, variance);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new CashUpResult(expectedCash, countedBase, variance, varianceJournal);
    }

    /// <inheritdoc />
    public Task<Result> RecordDishonourAsync(long companyId, long receiptId, DateOnly date, decimal penaltyAmount, string? penaltyAccountCode, string? penaltyInvoiceNumber, long reverserUserId, string reason, CancellationToken ct = default)
        => _db.AtomicAsync(() => RecordDishonourCoreAsync(companyId, receiptId, date, penaltyAmount, penaltyAccountCode, penaltyInvoiceNumber, reverserUserId, reason, ct), ct);

    // One unit of work: its postings and record changes commit together or not at all.
    private async Task<Result> RecordDishonourCoreAsync(long companyId, long receiptId, DateOnly date, decimal penaltyAmount, string? penaltyAccountCode, string? penaltyInvoiceNumber, long reverserUserId, string reason, CancellationToken ct)
    {
        Receipt? receipt = await _db.Set<Receipt>().Include(r => r.Tenders).Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == receiptId && r.CompanyId == companyId, ct).ConfigureAwait(false);
        if (receipt is null)
        {
            return Result.Failure(Error.NotFound("Rcp.ReceiptNotFound", $"Receipt {receiptId} not found."));
        }

        string reference = receipt.Tenders.FirstOrDefault(t => t.Type == TenderType.Cheque)?.Reference
            ?? receipt.Tenders.FirstOrDefault()?.Reference
            ?? receipt.ReceiptNumber;
        string drawer = receipt.PayerKind == PayerKind.Anonymous ? "(sundry)" : $"{receipt.PayerKind} #{receipt.PayerAccountId}";
        decimal amount = receipt.LineTotal;

        // Reverse the original receipt - restores the debt and undoes every line.
        Result<ReceiptReversalResult> reversal = await ReverseReceiptAsync(companyId, receiptId, date, reverserUserId, $"Dishonoured: {reason}", ct).ConfigureAwait(false);
        if (reversal.IsFailure)
        {
            return Result.Failure(reversal.Error);
        }

        // Raise the penalty on the payer's account, when charged.
        if (penaltyAmount > 0m)
        {
            if (receipt.PayerKind != PayerKind.ArCustomer || receipt.PayerAccountId is null || string.IsNullOrWhiteSpace(penaltyAccountCode))
            {
                return Result.Failure(Error.Rule("Rcp.PenaltyNotChargeable", "A penalty needs an AR payer account and a penalty-income account."));
            }

            Company company = await _db.Set<Company>().FirstAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
            Result<TradingPostingResult> penalty = await _salesLedger.PostInvoiceAsync(new SalesInvoiceRequest
            {
                CompanyId = companyId,
                CustomerId = receipt.PayerAccountId.Value,
                DocumentNumber = penaltyInvoiceNumber ?? $"{receipt.ReceiptNumber}-PEN",
                DocumentDate = date,
                Currency = company.BaseCurrency,
                NetAmount = penaltyAmount,
                TaxAmount = 0m,
                SalesAccountCode = penaltyAccountCode!,
                Narrative = $"Dishonour penalty - {receipt.ReceiptNumber}",
            }, ct).ConfigureAwait(false);

            if (penalty.IsFailure)
            {
                return Result.Failure(penalty.Error);
            }
        }

        _db.Set<DishonouredPayment>().Add(new DishonouredPayment(companyId, receiptId, receipt.ReceiptNumber, reference, drawer, amount, penaltyAmount, date));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public Task<Result<string>> ConfirmPendingTenderAsync(long companyId, long pendingTenderId, DateOnly date, string bankAccountCode, CancellationToken ct = default)
        => _db.AtomicAsync(() => ConfirmPendingTenderCoreAsync(companyId, pendingTenderId, date, bankAccountCode, ct), ct);

    // One unit of work: its postings and record changes commit together or not at all.
    private async Task<Result<string>> ConfirmPendingTenderCoreAsync(long companyId, long pendingTenderId, DateOnly date, string bankAccountCode, CancellationToken ct)
    {
        PendingTender? tender = await _db.Set<PendingTender>().FirstOrDefaultAsync(t => t.Id == pendingTenderId && t.CompanyId == companyId, ct).ConfigureAwait(false);
        if (tender is null)
        {
            return Error.NotFound("Rcp.PendingTenderNotFound", $"Pending tender {pendingTenderId} not found.");
        }

        if (tender.Status == PendingTenderStatus.Confirmed)
        {
            return Error.Rule("Rcp.AlreadyConfirmed", $"Pending tender {pendingTenderId} is already confirmed.");
        }

        Company company = await _db.Set<Company>().FirstAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        Result<PostingReceipt> posted = await _posting.PostAsync(new JournalRequest
        {
            CompanyId = companyId,
            SourceModule = "GL",
            PostingDate = date,
            TransactionCurrency = company.BaseCurrency,
            DocumentType = "TENDER-CLR",
            DocumentNumber = tender.ReceiptNumber,
            Narrative = $"Pending tender cleared - {tender.ReceiptNumber}",
            Lines =
            [
                new JournalLineRequest { AccountCode = bankAccountCode, Side = JournalSide.Debit, TransactionAmount = tender.Amount },
                new JournalLineRequest { AccountCode = tender.PendingAccountCode, Side = JournalSide.Credit, TransactionAmount = tender.Amount },
            ],
        }, ct).ConfigureAwait(false);

        if (posted.IsFailure)
        {
            return posted.Error;
        }

        tender.Confirm(date);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return posted.Value.JournalNumber;
    }

    /// <inheritdoc />
    public async Task<Result<long>> CaptureArrangementAsync(long companyId, long customerId, decimal totalAmount, decimal instalmentAmount, DateOnly startDate, CancellationToken ct = default)
    {
        if (totalAmount <= 0m || instalmentAmount <= 0m)
        {
            return Error.Rule("Rcp.InvalidArrangement", "The total and instalment amounts must be positive.");
        }

        if (instalmentAmount > totalAmount + 0.0001m)
        {
            return Error.Rule("Rcp.InstalmentTooLarge", "The instalment cannot exceed the total.");
        }

        bool customerExists = await _db.Set<Customer>().AnyAsync(c => c.Id == customerId && c.CompanyId == companyId, ct).ConfigureAwait(false);
        if (!customerExists)
        {
            return Error.NotFound("Rcp.CustomerNotFound", $"Customer {customerId} not found.");
        }

        PaymentArrangement arrangement = new(companyId, customerId, totalAmount, instalmentAmount, startDate);
        _db.Set<PaymentArrangement>().Add(arrangement);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return arrangement.Id;
    }

    /// <inheritdoc />
    public async Task<Result<int>> ReprintReceiptAsync(long companyId, long receiptId, DateOnly date, long byUserId, CancellationToken ct = default)
    {
        Receipt? receipt = await _db.Set<Receipt>().FirstOrDefaultAsync(r => r.Id == receiptId && r.CompanyId == companyId, ct).ConfigureAwait(false);
        if (receipt is null)
        {
            return Error.NotFound("Rcp.ReceiptNotFound", $"Receipt {receiptId} not found.");
        }

        int priorCopies = await _db.Set<ReceiptReprint>().CountAsync(x => x.ReceiptId == receiptId, ct).ConfigureAwait(false);
        int copyNumber = priorCopies + 1;
        _db.Set<ReceiptReprint>().Add(new ReceiptReprint(companyId, receiptId, receipt.ReceiptNumber, copyNumber, date, byUserId));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return copyNumber;
    }

    /// <inheritdoc />
    public async Task<Result> VerifySessionAsync(long companyId, long sessionId, long supervisorUserId, CancellationToken ct = default)
    {
        CashierSession? session = await _db.Set<CashierSession>().FirstOrDefaultAsync(s => s.Id == sessionId && s.CompanyId == companyId, ct).ConfigureAwait(false);
        if (session is null)
        {
            return Result.Failure(Error.NotFound("Rcp.SessionNotFound", $"Session {sessionId} not found."));
        }

        if (session.Status != SessionStatus.Closed)
        {
            return Result.Failure(Error.Rule("Rcp.SessionNotClosed", "Only a closed session can be verified."));
        }

        if (session.CashierUserId == supervisorUserId)
        {
            await LogViolationAsync(companyId, supervisorUserId, $"Attempted to verify own cash-up (session {sessionId})", session.ClosedOn ?? session.OpenedOn, ct).ConfigureAwait(false);
            return Result.Failure(Error.Rule("Rcp.SelfVerify", "A cashier cannot verify their own cash-up (segregation of duties)."));
        }

        session.Verify(supervisorUserId);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<AllocationPreview>> PreviewAllocationAsync(long companyId, long customerId, decimal amount, ReceiptAllocationStrategy strategy, IReadOnlyList<DirectedAllocation>? directed = null, CancellationToken ct = default)
    {
        if (amount <= 0m)
        {
            return Error.Rule("Rcp.NonPositiveAmount", "The amount to allocate must be positive.");
        }

        List<OpenItem> debits = await OutstandingDebitsAsync(companyId, customerId, ct).ConfigureAwait(false);
        Result<List<(OpenItem Item, decimal Applied)>> plan = BuildPlan(debits, amount, strategy, directed);
        if (plan.IsFailure)
        {
            return plan.Error;
        }

        List<AllocationPreviewLine> lines = plan.Value
            .Select(p => new AllocationPreviewLine(p.Item.Id, p.Item.DocumentNumber, p.Item.DocumentDate, p.Item.OutstandingTransaction, p.Applied))
            .ToList();
        decimal allocated = lines.Sum(l => l.Applied);
        return new AllocationPreview(lines, allocated, amount - allocated);
    }

    private async Task<(decimal Allocated, decimal Remainder)> AllocateAsync(long companyId, long customerId, long creditItemId, decimal amount, DateOnly date, ReceiptAllocationStrategy strategy, IReadOnlyList<DirectedAllocation> directed, CancellationToken ct)
    {
        List<OpenItem> debits = await OutstandingDebitsAsync(companyId, customerId, ct).ConfigureAwait(false);
        Result<List<(OpenItem Item, decimal Applied)>> plan = BuildPlan(debits, amount, strategy, directed);
        if (plan.IsFailure)
        {
            return (0m, amount);
        }

        decimal allocated = 0m;
        foreach ((OpenItem item, decimal applied) in plan.Value)
        {
            if (applied <= 0m)
            {
                continue;
            }

            Result<AllocationResult> result = await _allocation.AllocateAsync(companyId, TradingLedger.AccountsReceivable, item.Id, creditItemId, applied, date, ct).ConfigureAwait(false);
            if (result.IsFailure)
            {
                break;
            }

            allocated += applied;
        }

        return (allocated, amount - allocated);
    }

    // A foreign-currency payment: its base value is spread over the account's base-currency charges by the strategy (directed amounts are in the
    // base currency), each piece allocated across currencies; what is left stays as credit on account in the payment's currency.
    private async Task<(decimal Allocated, decimal Remainder)> AllocateForeignAsync(long companyId, long customerId, long creditItemId, decimal amount, CurrencyCode baseCurrency, DateOnly date,
        ReceiptAllocationStrategy strategy, IReadOnlyList<DirectedAllocation> directed, CancellationToken ct)
    {
        OpenItem credit = await _db.OpenItems.AsNoTracking().FirstAsync(i => i.Id == creditItemId, ct).ConfigureAwait(false);
        List<OpenItem> debits = [.. (await OutstandingDebitsAsync(companyId, customerId, ct).ConfigureAwait(false)).Where(d => d.Currency == baseCurrency)];
        Result<List<(OpenItem Item, decimal Applied)>> plan = BuildPlan(debits, credit.OutstandingBase, strategy, directed);
        if (plan.IsFailure)
        {
            return (0m, amount);
        }

        decimal allocated = 0m;
        foreach ((OpenItem item, decimal appliedBase) in plan.Value)
        {
            decimal creditAmount = Math.Min(decimal.Round(appliedBase / credit.ExchangeRate, 4, MidpointRounding.ToEven), amount - allocated);
            if (creditAmount <= 0m)
            {
                continue;
            }

            Result<AllocationResult> result = await _allocation.AllocateAcrossCurrenciesAsync(companyId, TradingLedger.AccountsReceivable, item.Id, creditItemId, creditAmount, date, ct).ConfigureAwait(false);
            if (result.IsFailure)
            {
                break;
            }

            allocated += result.Value.TransactionAmount;
        }

        return (allocated, Math.Max(0m, amount - allocated));
    }

    private async Task<List<OpenItem>> OutstandingDebitsAsync(long companyId, long customerId, CancellationToken ct) =>
        await _db.Set<OpenItem>()
            .Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable && i.TraderId == customerId && i.Sign == LedgerSign.Debit && i.OutstandingTransaction > 0m)
            .ToListAsync(ct).ConfigureAwait(false);

    private static Result<List<(OpenItem Item, decimal Applied)>> BuildPlan(List<OpenItem> debits, decimal amount, ReceiptAllocationStrategy strategy, IReadOnlyList<DirectedAllocation>? directed)
    {
        List<(OpenItem Item, decimal Applied)> plan = [];

        if (strategy == ReceiptAllocationStrategy.UserDirected)
        {
            if (directed is null || directed.Count == 0)
            {
                return Error.Validation("Rcp.NoDirectedAllocations", "A user-directed allocation requires explicit allocations.");
            }

            decimal directedTotal = directed.Sum(d => d.Amount);
            if (directedTotal > amount + 0.0001m)
            {
                return Error.Rule("Rcp.OverDirected", $"Directed allocations ({directedTotal}) exceed the payment ({amount}).");
            }

            foreach (DirectedAllocation instruction in directed)
            {
                OpenItem? item = debits.FirstOrDefault(d => d.Id == instruction.OpenItemId);
                if (item is null)
                {
                    return Error.NotFound("Rcp.DirectedItemNotFound", $"Open item {instruction.OpenItemId} is not an outstanding charge on this account.");
                }

                if (instruction.Amount > item.OutstandingTransaction + 0.0001m)
                {
                    return Error.Rule("Rcp.DirectedOverItem", $"Directed amount {instruction.Amount} exceeds the {item.OutstandingTransaction} outstanding on {item.DocumentNumber}.");
                }

                plan.Add((item, instruction.Amount));
            }

            return plan;
        }

        IEnumerable<OpenItem> ordered = strategy switch
        {
            ReceiptAllocationStrategy.NewestFirst => debits.OrderByDescending(i => i.DocumentDate).ThenByDescending(i => i.Id),
            ReceiptAllocationStrategy.LargestFirst => debits.OrderByDescending(i => i.OutstandingTransaction).ThenBy(i => i.DocumentDate),
            _ => debits.OrderBy(i => i.DocumentDate).ThenBy(i => i.Id),
        };

        decimal remaining = amount;
        foreach (OpenItem item in ordered)
        {
            if (remaining <= 0m)
            {
                break;
            }

            decimal take = Math.Min(remaining, item.OutstandingTransaction);
            plan.Add((item, take));
            remaining -= take;
        }

        return plan;
    }

    private async Task LogViolationAsync(long companyId, long userId, string conflict, DateOnly date, CancellationToken ct)
    {
        _db.Set<SegregationViolation>().Add(new SegregationViolation(companyId, userId, conflict, date));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    // Gapless per station; a field collector's handheld numbers from its issued range and stops at the range's end (brief §RCP-8).
    private async Task<Result<string>> NextReceiptNumberAsync(long companyId, CashierStation station, CancellationToken ct)
    {
        int count = await _db.Set<Receipt>().CountAsync(r => r.CompanyId == companyId && r.StationId == station.Id, ct).ConfigureAwait(false);
        return station.ReceiptNumber(count + 1) is string number ? number
            : Error.Rule("Rcp.RangeExhausted", $"Station {station.Code} has used its receipt range {station.ReceiptRangeFrom}–{station.ReceiptRangeTo}; issue the collector a new range.");
    }
}
