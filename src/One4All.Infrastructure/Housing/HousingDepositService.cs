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

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="posting">The posting engine.</param>
    /// <param name="salesLedger">The sales ledger (to apply a deposit as a receipt).</param>
    /// <param name="repossessions">Repossession (to return a cancelled agreement's stand).</param>
    public HousingDepositService(One4AllDbContext db, IPostingEngine posting, ISalesLedgerService salesLedger, IRepossessionService repossessions)
    {
        _db = db;
        _posting = posting;
        _salesLedger = salesLedger;
        _repossessions = repossessions;
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

        if (allocation.Status != AllocationStatus.Accepted)
        {
            return Error.Rule("Hse.OfferNotAccepted", $"The offer under {receipt.CertificateNumber} is {allocation.Status}; a deposit secures an accepted offer.");
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
    public Task<Result<string>> RefundDepositAsync(long companyId, string certificateNumber, DateOnly date, string bankAccountCode, CancellationToken ct = default)
        => _db.AtomicAsync(() => ClearCoreAsync(companyId, certificateNumber, date, bankAccountCode, ct), ct);

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
