using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Receipting;
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
/// posting patterns — a debtor payment credits the AR control (never income) and allocates oldest-first;
/// sundry credits the income vote; deposits credit the deposit liability; unidentified money credits
/// suspense — and runs the cashier session with a blind cash-up that posts the variance to shortages.
/// </summary>
public sealed class ReceiptingService : IReceiptingService
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _posting;
    private readonly ISalesLedgerService _salesLedger;
    private readonly IAllocationService _allocation;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="posting">The central posting engine.</param>
    /// <param name="salesLedger">The AR ledger (debtor receipts).</param>
    /// <param name="allocation">The allocation service (match receipt to invoices).</param>
    public ReceiptingService(One4AllDbContext db, IPostingEngine posting, ISalesLedgerService salesLedger, IAllocationService allocation)
    {
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
        if (request.Lines.Count == 0)
        {
            return Error.Validation("Rcp.NoLines", "A receipt must have at least one line.");
        }

        if (request.Lines.Any(l => l.Amount <= 0m))
        {
            return Error.Rule("Rcp.NonPositiveLine", "Receipt lines must be positive — no zero or negative receipts.");
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
        string receiptNumber = await NextReceiptNumberAsync(request.CompanyId, station, ct).ConfigureAwait(false);

        Receipt receipt = new(request.CompanyId, session.Id, session.StationId, session.CashierUserId, receiptNumber, request.Date, request.PayerKind, request.PayerAccountId, company.BaseCurrency);
        foreach (ReceiptLineRequest line in request.Lines)
        {
            receipt.AddLine(line.Type, line.Amount, line.AccountCode, line.Narrative);
        }

        foreach (ReceiptTenderRequest tender in request.Tenders)
        {
            receipt.AddTender(tender.Type, company.BaseCurrency, tender.Amount, tender.Reference);
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
                Currency = company.BaseCurrency,
                Amount = debtorTotal,
                BankAccountCode = request.CashAccountCode,
                Narrative = $"Receipt {receiptNumber}",
            }, ct).ConfigureAwait(false);

            if (receiptPosting.IsFailure)
            {
                return receiptPosting.Error;
            }

            (allocatedToDebt, creditOnAccount) = await AllocateOldestFirstAsync(request.CompanyId, request.PayerAccountId!.Value, receiptPosting.Value.OpenItemId, debtorTotal, request.Date, ct).ConfigureAwait(false);
        }

        // Sundry / deposit / suspense → Dr cash / Cr the coded account (never a guessed income account).
        List<ReceiptLineRequest> codedLines = request.Lines.Where(l => l.Type != ReceiptLineType.DebtorPayment).ToList();
        if (codedLines.Count > 0)
        {
            decimal codedTotal = codedLines.Sum(l => l.Amount);
            List<JournalLineRequest> journalLines =
            [
                new JournalLineRequest { AccountCode = request.CashAccountCode, Side = JournalSide.Debit, TransactionAmount = codedTotal },
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
                TransactionCurrency = company.BaseCurrency,
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
        return new ReceiptResult(receipt.Id, receiptNumber, total, allocatedToDebt, creditOnAccount);
    }

    /// <inheritdoc />
    public async Task<Result<CashUpResult>> CloseSessionAsync(long companyId, long sessionId, DateOnly date, IReadOnlyList<FloatDeclaration> countedCash, string cashAccountCode, string shortagesAccountCode, CancellationToken ct = default)
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
            return Result.Failure(Error.Rule("Rcp.SelfVerify", "A cashier cannot verify their own cash-up (segregation of duties)."));
        }

        session.Verify(supervisorUserId);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    private async Task<(decimal Allocated, decimal Remainder)> AllocateOldestFirstAsync(long companyId, long customerId, long creditItemId, decimal amount, DateOnly date, CancellationToken ct)
    {
        List<OpenItem> debits = await _db.Set<OpenItem>()
            .Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable && i.TraderId == customerId && i.Sign == LedgerSign.Debit && i.OutstandingTransaction > 0m)
            .OrderBy(i => i.DocumentDate).ThenBy(i => i.Id)
            .ToListAsync(ct).ConfigureAwait(false);

        decimal remaining = amount;
        decimal allocated = 0m;
        foreach (OpenItem debit in debits)
        {
            if (remaining <= 0m)
            {
                break;
            }

            decimal take = Math.Min(remaining, debit.OutstandingTransaction);
            Result<AllocationResult> result = await _allocation.AllocateAsync(companyId, TradingLedger.AccountsReceivable, debit.Id, creditItemId, take, date, ct).ConfigureAwait(false);
            if (result.IsFailure)
            {
                break;
            }

            allocated += take;
            remaining -= take;
        }

        return (allocated, remaining);
    }

    private async Task<string> NextReceiptNumberAsync(long companyId, CashierStation station, CancellationToken ct)
    {
        int count = await _db.Set<Receipt>().CountAsync(r => r.CompanyId == companyId && r.StationId == station.Id, ct).ConfigureAwait(false);
        return $"{station.ReceiptPrefix}-{count + 1:D6}";
    }
}
