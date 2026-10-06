using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Vouchers;
using One4All.Core.Common;
using One4All.Core.Vouchers;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Vouchers;

/// <summary>Gift card top-ups, blocks, replacements and history.</summary>
public sealed class GiftCardServicing : IGiftCardServicing
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _engine;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="engine">The posting engine.</param>
    public GiftCardServicing(One4AllDbContext db, IPostingEngine engine)
    {
        _db = db;
        _engine = engine;
    }

    /// <inheritdoc />
    public Task<Result<GiftCardResult>> TopUpAsync(long companyId, string cardNumber, decimal amount, DateOnly date, string cashAccountCode, string liabilityAccountCode, CancellationToken ct = default)
        => _db.AtomicAsync(() => TopUpCoreAsync(companyId, cardNumber, amount, date, cashAccountCode, liabilityAccountCode, ct), ct);

    // One unit: the posting, the new balance and its history line.
    private async Task<Result<GiftCardResult>> TopUpCoreAsync(long companyId, string cardNumber, decimal amount, DateOnly date, string cashAccountCode, string liabilityAccountCode, CancellationToken ct)
    {
        Result<GiftCard> found = await LoadAsync(companyId, cardNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        GiftCard card = found.Value;
        if (amount <= 0m || card.Status is not (GiftCardStatus.Active or GiftCardStatus.Redeemed))
        {
            return Error.Rule("Voucher.CannotTopUp", $"Card {card.CardNumber} is {card.Status}; only a live card can be topped up by a positive amount.");
        }

        Result<PostingReceipt> posted = await _engine.PostAsync(new JournalRequest
        {
            CompanyId = companyId,
            SourceModule = "VOU",
            PostingDate = date,
            TransactionCurrency = card.Currency,
            DocumentType = "GiftCardTopUp",
            DocumentNumber = card.CardNumber,
            Narrative = $"Gift card top-up {card.CardNumber}",
            Lines =
            [
                new JournalLineRequest { AccountCode = cashAccountCode, Side = JournalSide.Debit, TransactionAmount = amount },
                new JournalLineRequest { AccountCode = liabilityAccountCode, Side = JournalSide.Credit, TransactionAmount = amount },
            ],
        }, ct).ConfigureAwait(false);
        if (posted.IsFailure)
        {
            return posted.Error;
        }

        card.TopUp(amount);
        _db.Set<GiftCardTransaction>().Add(new GiftCardTransaction(companyId, card.Id, date, GiftCardEvent.ToppedUp, amount, card.Balance, posted.Value.JournalNumber));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new GiftCardResult(card.Id, card.Balance, card.Status);
    }

    /// <inheritdoc />
    public async Task<Result<GiftCardResult>> BlockAsync(long companyId, string cardNumber, DateOnly date, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("Voucher.ReasonRequired", "Blocking a card requires a reason.");
        }

        Result<GiftCard> found = await LoadAsync(companyId, cardNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        GiftCard card = found.Value;
        if (card.Status != GiftCardStatus.Active)
        {
            return Error.Rule("Voucher.NotActive", $"Card {card.CardNumber} is {card.Status} and cannot be blocked.");
        }

        card.Block();
        _db.Set<GiftCardTransaction>().Add(new GiftCardTransaction(companyId, card.Id, date, GiftCardEvent.Blocked, 0m, card.Balance, reason.Trim()));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new GiftCardResult(card.Id, card.Balance, card.Status);
    }

    /// <inheritdoc />
    public Task<Result<GiftCardResult>> ReplaceAsync(long companyId, string blockedCardNumber, string newCardNumber, DateOnly date, CancellationToken ct = default)
        => _db.AtomicAsync(() => ReplaceCoreAsync(companyId, blockedCardNumber, newCardNumber, date, ct), ct);

    // One unit: the new card and the emptied old one are saved together.
    private async Task<Result<GiftCardResult>> ReplaceCoreAsync(long companyId, string blockedCardNumber, string newCardNumber, DateOnly date, CancellationToken ct)
    {
        Result<GiftCard> found = await LoadAsync(companyId, blockedCardNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        GiftCard old = found.Value;
        if (old.Status != GiftCardStatus.Blocked || old.ReplacedByCardId is not null)
        {
            return Error.Rule("Voucher.NotReplaceable", $"Card {old.CardNumber} must be blocked, and not already replaced, to be replaced.");
        }

        string number = newCardNumber.Trim().ToUpperInvariant();
        if (await _db.Set<GiftCard>().AnyAsync(g => g.CompanyId == companyId && g.CardNumber == number, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Voucher.Duplicate", $"Gift card {number} already exists.");
        }

        GiftCard replacement = new(companyId, number, old.Currency, old.Balance, date, old.ExpiryDate);
        _db.Set<GiftCard>().Add(replacement);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        decimal moved = old.TransferToReplacement(replacement.Id);
        _db.Set<GiftCardTransaction>().Add(new GiftCardTransaction(companyId, old.Id, date, GiftCardEvent.ReplacedOut, -moved, 0m, number));
        _db.Set<GiftCardTransaction>().Add(new GiftCardTransaction(companyId, replacement.Id, date, GiftCardEvent.ReplacedIn, moved, replacement.Balance, old.CardNumber));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new GiftCardResult(replacement.Id, replacement.Balance, replacement.Status);
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<GiftCardHistoryLine>>> HistoryAsync(long companyId, string cardNumber, CancellationToken ct = default)
    {
        Result<GiftCard> found = await LoadAsync(companyId, cardNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        List<GiftCardHistoryLine> lines = await _db.Set<GiftCardTransaction>().AsNoTracking()
            .Where(t => t.GiftCardId == found.Value.Id)
            .OrderBy(t => t.Id)
            .Select(t => new GiftCardHistoryLine(t.OccurredOn, t.Event, t.Amount, t.BalanceAfter, t.Note))
            .ToListAsync(ct).ConfigureAwait(false);
        return Result<IReadOnlyList<GiftCardHistoryLine>>.Success(lines);
    }

    /// <inheritdoc />
    public async Task<Result<int>> CreateRangeAsync(long companyId, string prefix, long first, long last, int digits, Core.Accounting.CurrencyCode currency, DateOnly date, CancellationToken ct = default)
    {
        string head = (prefix ?? string.Empty).Trim().ToUpperInvariant();
        if (first < 0 || last < first || last - first >= 10_000 || digits is < 1 or > 18 || last.ToString(System.Globalization.CultureInfo.InvariantCulture).Length > digits)
        {
            return Error.Validation("Voucher.RangeInvalid", "A range runs from a first to a last serial (at most 10,000 cards) that fit the number of digits.");
        }

        List<string> numbers = [.. Enumerable.Range(0, (int)(last - first + 1)).Select(n => head + (first + n).ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(digits, '0'))];
        if (await _db.Set<GiftCard>().AnyAsync(g => g.CompanyId == companyId && numbers.Contains(g.CardNumber), ct).ConfigureAwait(false))
        {
            return Error.Conflict("Voucher.RangeOverlaps", $"Some of {numbers[0]}–{numbers[^1]} already exist.");
        }

        _db.Set<GiftCard>().AddRange(numbers.Select(n => GiftCard.Blank(companyId, n, currency, date)));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return numbers.Count;
    }

    /// <inheritdoc />
    public Task<Result<GiftCardResult>> ActivateAsync(long companyId, string cardNumber, decimal value, DateOnly date, DateOnly? expiryDate, string cashAccountCode, string liabilityAccountCode,
        CancellationToken ct = default)
        => _db.AtomicAsync(async () =>
        {
            Result<GiftCard> found = await LoadAsync(companyId, cardNumber, ct).ConfigureAwait(false);
            if (found.IsFailure)
            {
                return found.Error;
            }

            GiftCard card = found.Value;
            if (card.Status != GiftCardStatus.Inactive || value <= 0m)
            {
                return Error.Rule("Voucher.CannotActivate", $"Card {card.CardNumber} is {card.Status}; only inactive stock is activated, with a positive value.");
            }

            Result<PostingReceipt> posted = await _engine.PostAsync(new JournalRequest
            {
                CompanyId = companyId, SourceModule = "VOU", PostingDate = date, TransactionCurrency = card.Currency, EnforceManualJournalRules = false,
                DocumentType = "GiftCardIssue", DocumentNumber = card.CardNumber, Narrative = $"Gift card activation {card.CardNumber}",
                Lines =
                [
                    new JournalLineRequest { AccountCode = cashAccountCode, Side = JournalSide.Debit, TransactionAmount = value },
                    new JournalLineRequest { AccountCode = liabilityAccountCode, Side = JournalSide.Credit, TransactionAmount = value },
                ],
            }, ct).ConfigureAwait(false);
            if (posted.IsFailure)
            {
                return posted.Error;
            }

            card.Activate(value, date, expiryDate);
            _db.Set<GiftCardTransaction>().Add(new GiftCardTransaction(companyId, card.Id, date, GiftCardEvent.Activated, value, card.Balance, posted.Value.JournalNumber));
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result<GiftCardResult>.Success(new GiftCardResult(card.Id, card.Balance, card.Status));
        }, ct);

    private async Task<Result<GiftCard>> LoadAsync(long companyId, string cardNumber, CancellationToken ct)
    {
        string number = cardNumber.Trim().ToUpperInvariant();
        GiftCard? card = await _db.Set<GiftCard>().FirstOrDefaultAsync(g => g.CompanyId == companyId && g.CardNumber == number, ct).ConfigureAwait(false);
        if (card is null)
        {
            return Error.NotFound("Voucher.NotFound", $"Gift card {cardNumber} not found.");
        }

        return card;
    }
}
