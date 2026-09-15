using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Vouchers;
using One4All.Core.Common;
using One4All.Core.Vouchers;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Vouchers;

/// <summary>Voucher management (gift cards) with liability accounting (brief §4.8).</summary>
public sealed class VoucherService : IVoucherService
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _postingEngine;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="postingEngine">The posting engine.</param>
    public VoucherService(One4AllDbContext db, IPostingEngine postingEngine)
    {
        _db = db;
        _postingEngine = postingEngine;
    }

    /// <inheritdoc />
    public async Task<Result<GiftCardResult>> IssueGiftCardAsync(IssueGiftCardRequest request, CancellationToken ct = default)
    {
        bool exists = await _db.Set<GiftCard>().AnyAsync(g => g.CompanyId == request.CompanyId && g.CardNumber == request.CardNumber.Trim().ToUpperInvariant(), ct).ConfigureAwait(false);
        if (exists)
        {
            return Error.Conflict("Voucher.Duplicate", $"Gift card {request.CardNumber} already exists.");
        }

        Result<PostingReceipt> posting = await _postingEngine.PostAsync(new JournalRequest
        {
            CompanyId = request.CompanyId,
            SourceModule = "VOU",
            PostingDate = request.IssueDate,
            TransactionCurrency = request.Currency,
            EnforceManualJournalRules = false,
            DocumentType = "GiftCardIssue",
            DocumentNumber = request.CardNumber,
            Narrative = $"Gift card issue {request.CardNumber}",
            Lines =
            [
                new JournalLineRequest { AccountCode = request.CashAccountCode, Side = JournalSide.Debit, TransactionAmount = request.Value },
                new JournalLineRequest { AccountCode = request.LiabilityAccountCode, Side = JournalSide.Credit, TransactionAmount = request.Value },
            ],
        }, ct).ConfigureAwait(false);

        if (posting.IsFailure)
        {
            return posting.Error;
        }

        GiftCard card = new(request.CompanyId, request.CardNumber, request.Currency, request.Value, request.IssueDate, request.ExpiryDate);
        _db.Set<GiftCard>().Add(card);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new GiftCardResult(card.Id, card.Balance, card.Status);
    }

    /// <inheritdoc />
    public async Task<Result<GiftCardResult>> RedeemAsync(long companyId, string cardNumber, decimal amount, CancellationToken ct = default)
    {
        Result<GiftCard> cardResult = await LoadAsync(companyId, cardNumber, ct).ConfigureAwait(false);
        if (cardResult.IsFailure)
        {
            return cardResult.Error;
        }

        GiftCard card = cardResult.Value;
        Result redeem = card.Redeem(amount);
        if (redeem.IsFailure)
        {
            return redeem.Error;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new GiftCardResult(card.Id, card.Balance, card.Status);
    }

    /// <inheritdoc />
    public async Task<Result<GiftCardResult>> GetBalanceAsync(long companyId, string cardNumber, CancellationToken ct = default)
    {
        Result<GiftCard> cardResult = await LoadAsync(companyId, cardNumber, ct).ConfigureAwait(false);
        return cardResult.IsFailure ? cardResult.Error : new GiftCardResult(cardResult.Value.Id, cardResult.Value.Balance, cardResult.Value.Status);
    }

    /// <inheritdoc />
    public async Task<Result<GiftCardResult>> RecogniseBreakageAsync(long companyId, string cardNumber, DateOnly date, string liabilityAccountCode, string breakageAccountCode, CancellationToken ct = default)
    {
        Result<GiftCard> cardResult = await LoadAsync(companyId, cardNumber, ct).ConfigureAwait(false);
        if (cardResult.IsFailure)
        {
            return cardResult.Error;
        }

        GiftCard card = cardResult.Value;
        decimal breakage = card.RecogniseBreakage();
        if (breakage > 0m)
        {
            Result<PostingReceipt> posting = await _postingEngine.PostAsync(new JournalRequest
            {
                CompanyId = companyId,
                SourceModule = "VOU",
                PostingDate = date,
                TransactionCurrency = card.Currency,
                EnforceManualJournalRules = false,
                DocumentType = "GiftCardBreakage",
                DocumentNumber = cardNumber,
                Narrative = $"Gift card breakage {cardNumber}",
                Lines =
                [
                    new JournalLineRequest { AccountCode = liabilityAccountCode, Side = JournalSide.Debit, TransactionAmount = breakage },
                    new JournalLineRequest { AccountCode = breakageAccountCode, Side = JournalSide.Credit, TransactionAmount = breakage },
                ],
            }, ct).ConfigureAwait(false);

            if (posting.IsFailure)
            {
                return posting.Error;
            }
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new GiftCardResult(card.Id, card.Balance, card.Status);
    }

    private async Task<Result<GiftCard>> LoadAsync(long companyId, string cardNumber, CancellationToken ct)
    {
        string number = cardNumber.Trim().ToUpperInvariant();
        GiftCard? card = await _db.Set<GiftCard>().FirstOrDefaultAsync(g => g.CompanyId == companyId && g.CardNumber == number, ct).ConfigureAwait(false);
        return card is null ? Error.NotFound("Voucher.NotFound", $"Gift card {cardNumber} not found.") : card;
    }
}
