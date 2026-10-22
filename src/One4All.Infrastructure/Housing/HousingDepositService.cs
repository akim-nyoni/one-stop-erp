using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Housing;
using One4All.Core.Common;
using One4All.Core.Housing;
using One4All.Core.System;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Trading;

namespace One4All.Infrastructure.Housing;

/// <summary>Purchase deposits and agreement cancellations (brief §14 H51, H53).</summary>
public sealed class HousingDepositService : IHousingDepositService
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _posting;
    private readonly ISalesLedgerService _salesLedger;
    private readonly IRepossessionService _repossessions;
    private readonly Application.Abstractions.Security.IPermissionService _permissions;

    // An allocation whose deposit may be refunded: the sale is not going ahead.
    private static readonly AllocationStatus[] RefundableStatuses = [AllocationStatus.Declined, AllocationStatus.Lapsed, AllocationStatus.Cancelled, AllocationStatus.Ended];

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="posting">The posting engine.</param>
    /// <param name="salesLedger">The sales ledger (to apply a deposit as a receipt).</param>
    /// <param name="repossessions">Repossession (to return a cancelled agreement's stand).</param>
    /// <param name="permissions">Checks the authority of whoever approves a deposit refund.</param>
    public HousingDepositService(One4AllDbContext db, IPostingEngine posting, ISalesLedgerService salesLedger, IRepossessionService repossessions,
        Application.Abstractions.Security.IPermissionService permissions)
    {
        _db = db;
        _posting = posting;
        _salesLedger = salesLedger;
        _repossessions = repossessions;
        _permissions = permissions;
    }

    /// <inheritdoc />
    public Task<Result<string>> TakeDepositAsync(StandDepositReceipt receipt, CancellationToken ct = default)
        => _db.AtomicAsync(() => TakeCoreAsync(receipt, ct), ct);

    private async Task<Result<string>> TakeCoreAsync(StandDepositReceipt receipt, CancellationToken ct)
    {
        if (receipt.Amount <= 0m || string.IsNullOrWhiteSpace(receipt.ReceiptReference) || string.IsNullOrWhiteSpace(receipt.LiabilityAccountCode))
        {
            return Error.Validation("Hse.DepositDetails", "A deposit needs a positive amount, the receipt and the liability account.");
        }

        StandAllocation? allocation = await AllocationAsync(receipt.CompanyId, receipt.CertificateNumber, ct).ConfigureAwait(false);
        if (allocation is null)
        {
            return Error.NotFound("Hse.CertificateNotFound", $"Allocation certificate {receipt.CertificateNumber} not found.");
        }

        if (allocation.Status is not (AllocationStatus.Offered or AllocationStatus.Accepted))
        {
            return Error.Rule("Hse.OfferNotAccepted", $"The offer under {receipt.CertificateNumber} is {allocation.Status}; a deposit secures an open or accepted offer.");
        }

        if (!await _db.Customers.AnyAsync(c => c.Id == receipt.CustomerId && c.CompanyId == receipt.CompanyId, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Ar.CustomerNotFound", $"Customer {receipt.CustomerId} not found.");
        }

        if (await _db.Set<StandDeposit>().AnyAsync(d => d.AllocationId == allocation.Id && d.ClearedOn == null, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Hse.DepositHeld", $"A deposit is already held against {receipt.CertificateNumber}.");
        }

        Result<string> journal = await PostAsync(receipt.CompanyId, receipt.Date, "HSE-DEP", receipt.ReceiptReference.Trim(), $"Purchase deposit {receipt.CertificateNumber}",
            receipt.BankAccountCode, receipt.LiabilityAccountCode, receipt.Amount, ct).ConfigureAwait(false);
        if (journal.IsFailure)
        {
            return journal.Error;
        }

        _db.Set<StandDeposit>().Add(new StandDeposit(receipt.CompanyId, allocation.Id, allocation.StandId, receipt.CustomerId, receipt.Amount, receipt.Date, receipt.ReceiptReference.Trim(),
            receipt.LiabilityAccountCode, journal.Value));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return journal.Value;
    }

    /// <inheritdoc />
    public Task<Result<string>> ApplyDepositAsync(long companyId, string certificateNumber, DateOnly date, CancellationToken ct = default)
        => _db.AtomicAsync(() => ClearCoreAsync(companyId, certificateNumber, date, null, ct), ct);

    /// <inheritdoc />
    public async Task<Result<long>> RequestRefundAsync(DepositRefundInput request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Reason) || string.IsNullOrWhiteSpace(request.BankAccountCode))
        {
            return Error.Validation("Hse.RefundDetails", "A deposit refund needs the reason and the bank account it is paid from.");
        }

        StandAllocation? allocation = await AllocationAsync(request.CompanyId, request.CertificateNumber, ct).ConfigureAwait(false);
        StandDeposit? deposit = allocation is null ? null
            : await _db.Set<StandDeposit>().AsNoTracking().FirstOrDefaultAsync(d => d.AllocationId == allocation.Id && d.ClearedOn == null, ct).ConfigureAwait(false);
        if (allocation is null || deposit is null)
        {
            return Error.NotFound("Hse.NoDepositHeld", $"No deposit is held against {request.CertificateNumber}.");
        }

        Result refundable = Refundable(allocation, deposit, request.Date);
        if (refundable.IsFailure)
        {
            return refundable.Error;
        }

        if (await _db.Set<DepositRefundRequest>().AnyAsync(r => r.DepositId == deposit.Id && r.Status == DepositRefundStatus.Pending, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Hse.RefundPending", $"A refund of the deposit against {allocation.CertificateNumber} is already awaiting approval.");
        }

        DepositRefundRequest refund = new(request.CompanyId, deposit.Id, allocation.CertificateNumber ?? request.CertificateNumber.Trim(), request.Date, request.BankAccountCode.Trim(), request.Reason.Trim(), request.RequestedByUserId);
        _db.Set<DepositRefundRequest>().Add(refund);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return refund.Id;
    }

    /// <inheritdoc />
    public Task<Result<string>> ApproveRefundAsync(long companyId, long requestId, long approverUserId, CancellationToken ct = default)
        => _db.AtomicAsync(() => ApproveRefundCoreAsync(companyId, requestId, approverUserId, ct), ct);

    // One unit: the refund journal, the deposit cleared and the request approved.
    private async Task<Result<string>> ApproveRefundCoreAsync(long companyId, long requestId, long approverUserId, CancellationToken ct)
    {
        Result<DepositRefundRequest> pending = await DecidableRefundAsync(companyId, requestId, approverUserId, ct).ConfigureAwait(false);
        if (pending.IsFailure)
        {
            return pending.Error;
        }

        DepositRefundRequest request = pending.Value;
        StandAllocation? allocation = await AllocationAsync(companyId, request.CertificateNumber, ct).ConfigureAwait(false);
        StandDeposit? deposit = await _db.Set<StandDeposit>().AsNoTracking().FirstOrDefaultAsync(d => d.Id == request.DepositId && d.ClearedOn == null, ct).ConfigureAwait(false);
        if (allocation is null || deposit is null)
        {
            return Error.NotFound("Hse.NoDepositHeld", $"No deposit is held against {request.CertificateNumber}.");
        }

        // Checked again on approval: the allocation may have been reinstated since the request.
        Result refundable = Refundable(allocation, deposit, request.Date);
        if (refundable.IsFailure)
        {
            return refundable.Error;
        }

        Result<string> journal = await ClearCoreAsync(companyId, request.CertificateNumber, request.Date, request.BankAccountCode, ct).ConfigureAwait(false);
        if (journal.IsFailure)
        {
            return journal.Error;
        }

        request.Approve(approverUserId, journal.Value);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return journal.Value;
    }

    /// <inheritdoc />
    public async Task<Result> RejectRefundAsync(long companyId, long requestId, long userId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(Error.Validation("Hse.RefundReason", "Give the reason for turning the refund down."));
        }

        Result<DepositRefundRequest> pending = await DecidableRefundAsync(companyId, requestId, userId, ct).ConfigureAwait(false);
        if (pending.IsFailure)
        {
            return Result.Failure(pending.Error);
        }

        pending.Value.Reject(userId, reason.Trim());
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PendingDepositRefund>> PendingRefundsAsync(long companyId, CancellationToken ct = default)
        => await (
            from r in _db.Set<DepositRefundRequest>().AsNoTracking()
            join d in _db.Set<StandDeposit>().AsNoTracking() on r.DepositId equals d.Id
            where r.CompanyId == companyId && r.Status == DepositRefundStatus.Pending
            orderby r.Id
            select new PendingDepositRefund(r.Id, r.CertificateNumber, d.Amount, r.Date, r.Reason, r.RequestedByUserId))
            .ToListAsync(ct).ConfigureAwait(false);

    // A deposit is refunded only once the sale it secured is not going ahead, and not before it was received.
    private static Result Refundable(StandAllocation allocation, StandDeposit deposit, DateOnly date)
    {
        if (!RefundableStatuses.Contains(allocation.Status))
        {
            return Result.Failure(Error.Rule("Hse.RefundAllocationOpen",
                $"The offer under {allocation.CertificateNumber} is {allocation.Status}; a deposit is refunded only once the offer is declined, lapsed or cancelled, or the allocation has ended. "
                + "While the sale goes ahead the deposit is applied to the buyer's account."));
        }

        return date < deposit.ReceivedOn
            ? Result.Failure(Error.Validation("Hse.DepositDate", "A deposit cannot be cleared before it was received."))
            : Result.Success();
    }

    // A pending refund, decided by someone other than who asked, holding the deposit refund approve right.
    private async Task<Result<DepositRefundRequest>> DecidableRefundAsync(long companyId, long requestId, long userId, CancellationToken ct)
    {
        DepositRefundRequest? request = await _db.Set<DepositRefundRequest>()
            .FirstOrDefaultAsync(r => r.Id == requestId && r.CompanyId == companyId && r.Status == DepositRefundStatus.Pending, ct).ConfigureAwait(false);
        if (request is null)
        {
            return Error.NotFound("Hse.RefundNotPending", $"Deposit refund request {requestId} is not awaiting a decision.");
        }

        if (request.RequestedByUserId == userId)
        {
            return Error.Forbidden("Hse.SelfApproval", "You asked for this refund; another user approves or rejects it.");
        }

        if (!(await _permissions.ResolveAsync(userId, ct).ConfigureAwait(false)).Has("Housing.DepositRefund", Core.Security.OperationFlags.Approve))
        {
            return Error.Forbidden("Hse.RefundNotAuthorised", $"User {userId} may not approve deposit refunds.");
        }

        return request;
    }

    // Applied: a receipt on the payer's account funded from the liability. Refunded: the liability paid out of the bank.
    private async Task<Result<string>> ClearCoreAsync(long companyId, string certificateNumber, DateOnly date, string? refundBankAccountCode, CancellationToken ct)
    {
        StandAllocation? allocation = await AllocationAsync(companyId, certificateNumber, ct).ConfigureAwait(false);
        StandDeposit? deposit = allocation is null ? null
            : await _db.Set<StandDeposit>().FirstOrDefaultAsync(d => d.AllocationId == allocation.Id && d.ClearedOn == null, ct).ConfigureAwait(false);
        if (deposit is null)
        {
            return Error.NotFound("Hse.NoDepositHeld", $"No deposit is held against {certificateNumber}.");
        }

        if (date < deposit.ReceivedOn)
        {
            return Error.Validation("Hse.DepositDate", "A deposit cannot be cleared before it was received.");
        }

        string journal;
        if (refundBankAccountCode is null)
        {
            Company? company = await _db.Set<Company>().FirstOrDefaultAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
            if (company is null)
            {
                return Error.NotFound("Housing.CompanyNotFound", $"Company {companyId} not found.");
            }

            Result<TradingPostingResult> applied = await _salesLedger.PostReceiptAsync(new ReceiptRequest
            {
                CompanyId = companyId, CustomerId = deposit.CustomerId, DocumentNumber = $"{deposit.ReceiptReference}-APP", ReceiptDate = date, Currency = company.BaseCurrency,
                Amount = deposit.Amount, BankAccountCode = deposit.LiabilityAccountCode, Narrative = $"Purchase deposit {certificateNumber} applied",
            }, ct).ConfigureAwait(false);
            if (applied.IsFailure)
            {
                return applied.Error;
            }

            journal = applied.Value.JournalNumber;
        }
        else
        {
            Result<string> refunded = await PostAsync(companyId, date, "HSE-DEPREF", $"{deposit.ReceiptReference}-REF", $"Purchase deposit {certificateNumber} refunded",
                deposit.LiabilityAccountCode, refundBankAccountCode, deposit.Amount, ct).ConfigureAwait(false);
            if (refunded.IsFailure)
            {
                return refunded.Error;
            }

            journal = refunded.Value;
        }

        deposit.Clear(date, refundBankAccountCode is not null, journal);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return journal;
    }

    /// <inheritdoc />
    public Task<Result<AgreementCancellationResult>> CancelAgreementAsync(AgreementCancellationRequest request, CancellationToken ct = default)
        => _db.AtomicAsync(() => CancelCoreAsync(request, ct), ct);

    // The refund is computed before the repossession clears the buyer's account; the repossession forfeits the balance and the refund comes back out of it.
    private async Task<Result<AgreementCancellationResult>> CancelCoreAsync(AgreementCancellationRequest request, CancellationToken ct)
    {
        if (request.MonthlyOccupationRental < 0m || request.AdminChargePercent is < 0m or > 100m || string.IsNullOrWhiteSpace(request.BankAccountCode))
        {
            return Error.Validation("Hse.CancellationTerms", "A cancellation needs a rental of zero or more, an admin charge of 0–100% and the refund bank account.");
        }

        InstalmentAgreement? agreement = await _db.Set<InstalmentAgreement>().AsNoTracking().Include(a => a.Lines)
            .FirstOrDefaultAsync(a => a.CompanyId == request.CompanyId && a.AgreementNumber == request.AgreementNumber.Trim(), ct).ConfigureAwait(false);
        if (agreement is null)
        {
            return Error.NotFound("Hse.AgreementNotFound", $"Agreement {request.AgreementNumber} not found.");
        }

        List<OpenItemAsAt> items = [.. (await OpenItemsAsAt.LoadAsync(_db, request.CompanyId, TradingLedger.AccountsReceivable, request.Date, ct).ConfigureAwait(false))
            .Where(i => i.Item.TraderId == agreement.BuyerCustomerId)];
        decimal paid = items.Where(i => i.Item.ItemType == OpenItemType.Receipt && i.Item.DocumentDate >= agreement.SignedOn).Sum(i => i.Item.OriginalBase);
        decimal owing = items.Sum(i => i.Signed(LedgerSign.Debit));
        decimal notYetBilled = agreement.Lines.Where(l => l.Raised && l.DueDate <= request.Date).OrderBy(l => l.Number).Select(l => (decimal?)l.BalanceAfter).LastOrDefault() ?? agreement.Financed;
        decimal arrears = Math.Max(0m, owing - notYetBilled);
        RefundComputation refund = CancellationRefund.Compute(paid, agreement.SignedOn, request.Date, request.MonthlyOccupationRental, agreement.CashPrice, request.AdminChargePercent, arrears);

        Result<RepossessionEntry> repossessed = await _repossessions.RepossessAsync(new RepossessionRequest
        {
            CompanyId = request.CompanyId, AgreementNumber = agreement.AgreementNumber, Date = request.Date, NetRealisableValue = request.NetRealisableValue,
            Reason = $"Cancelled at the buyer's request: {request.Reason}", RequestedByUserId = request.RequestedByUserId, ApprovedByUserId = request.ApprovedByUserId,
            StandsAvailableAccountCode = request.StandsAvailableAccountCode, GainLossAccountCode = request.GainLossAccountCode,
        }, ct).ConfigureAwait(false);
        if (repossessed.IsFailure)
        {
            return repossessed.Error;
        }

        string? refundJournal = null;
        if (refund.Refund > 0m)
        {
            Result<string> posted = await PostAsync(request.CompanyId, request.Date, "HSE-CANREF", $"{agreement.AgreementNumber}-REF", $"Refund on cancelling {agreement.AgreementNumber}",
                request.GainLossAccountCode, request.BankAccountCode, refund.Refund, ct).ConfigureAwait(false);
            if (posted.IsFailure)
            {
                return posted.Error;
            }

            refundJournal = posted.Value;
        }

        _db.Set<AgreementCancellation>().Add(new AgreementCancellation(request.CompanyId, agreement.AgreementNumber, request.Date, request.Reason.Trim(), paid, refund.MonthsOccupied,
            refund.OccupationRental, refund.AdminCharge, arrears, refund.Refund, repossessed.Value.Reference, refundJournal));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new AgreementCancellationResult(paid, refund.MonthsOccupied, refund.OccupationRental, refund.AdminCharge, arrears, refund.Refund, repossessed.Value.Reference);
    }

    private Task<StandAllocation?> AllocationAsync(long companyId, string certificateNumber, CancellationToken ct)
    {
        string number = certificateNumber.Trim();
        return _db.Set<StandAllocation>().AsNoTracking().FirstOrDefaultAsync(a => a.CompanyId == companyId && a.CertificateNumber == number, ct);
    }

    private async Task<Result<string>> PostAsync(long companyId, DateOnly date, string documentType, string documentNumber, string narrative, string debitAccount, string creditAccount, decimal amount, CancellationToken ct)
    {
        Company? company = await _db.Set<Company>().FirstOrDefaultAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        if (company is null)
        {
            return Error.NotFound("Housing.CompanyNotFound", $"Company {companyId} not found.");
        }

        Result<PostingReceipt> posted = await _posting.PostAsync(new JournalRequest
        {
            CompanyId = companyId, SourceModule = "GL", PostingDate = date, TransactionCurrency = company.BaseCurrency, DocumentType = documentType, DocumentNumber = documentNumber, Narrative = narrative,
            Lines =
            [
                new JournalLineRequest { AccountCode = debitAccount, Side = JournalSide.Debit, TransactionAmount = amount },
                new JournalLineRequest { AccountCode = creditAccount, Side = JournalSide.Credit, TransactionAmount = amount },
            ],
        }, ct).ConfigureAwait(false);
        return posted.IsFailure ? posted.Error : posted.Value.JournalNumber;
    }
}
