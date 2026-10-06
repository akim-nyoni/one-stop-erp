using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Vouchers;

namespace One4All.Application.Abstractions.Vouchers;

/// <summary>A request to issue a gift card.</summary>
public sealed class IssueGiftCardRequest
{
    /// <summary>The company.</summary>
    public long CompanyId { get; init; }

    /// <summary>The card number/serial.</summary>
    public string CardNumber { get; init; } = string.Empty;

    /// <summary>The card currency.</summary>
    public CurrencyCode Currency { get; init; }

    /// <summary>The value loaded.</summary>
    public decimal Value { get; init; }

    /// <summary>The issue date.</summary>
    public DateOnly IssueDate { get; init; }

    /// <summary>The expiry date, if any.</summary>
    public DateOnly? ExpiryDate { get; init; }

    /// <summary>The cash/bank account receiving the money (debited).</summary>
    public string CashAccountCode { get; init; } = string.Empty;

    /// <summary>The gift-card liability account (credited).</summary>
    public string LiabilityAccountCode { get; init; } = string.Empty;
}

/// <summary>The result of a gift-card operation.</summary>
/// <param name="CardId">The card id.</param>
/// <param name="Balance">The card balance after the operation.</param>
/// <param name="Status">The card status.</param>
public readonly record struct GiftCardResult(long CardId, decimal Balance, GiftCardStatus Status);

/// <summary>
/// Voucher management (brief §4.8) — gift cards as a liability. Issuing loads value against a liability
/// account; redemption draws the balance down (the value is a tender in the sale that debits the liability);
/// unredeemed expired balances are recognised as breakage income.
/// </summary>
public interface IVoucherService
{
    /// <summary>Issues a gift card: Dr cash, Cr gift-card liability; creates the card.</summary>
    /// <param name="request">The request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result, or an error.</returns>
    Task<Result<GiftCardResult>> IssueGiftCardAsync(IssueGiftCardRequest request, CancellationToken ct = default);

    /// <summary>Redeems an amount from a card (reduces the balance; the GL debit to the liability is posted by the sale's tender).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="cardNumber">The card number.</param>
    /// <param name="amount">The amount to redeem.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result, or an error.</returns>
    Task<Result<GiftCardResult>> RedeemAsync(long companyId, string cardNumber, decimal amount, CancellationToken ct = default);

    /// <summary>Gets a card's balance and status.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="cardNumber">The card number.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result, or an error.</returns>
    Task<Result<GiftCardResult>> GetBalanceAsync(long companyId, string cardNumber, CancellationToken ct = default);

    /// <summary>Recognises an expired card's residual balance as breakage income: Dr liability, Cr breakage.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="cardNumber">The card number.</param>
    /// <param name="date">The recognition date.</param>
    /// <param name="liabilityAccountCode">The gift-card liability account (debited).</param>
    /// <param name="breakageAccountCode">The breakage income account (credited).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result, or an error.</returns>
    Task<Result<GiftCardResult>> RecogniseBreakageAsync(long companyId, string cardNumber, DateOnly date, string liabilityAccountCode, string breakageAccountCode, CancellationToken ct = default);

    /// <summary>
    /// Recognises breakage for every active card whose expiry date has passed and that still has a residual
    /// balance — the periodic run that releases stale gift-card liabilities to income.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="asAtDate">The recognition date; cards expiring on or before it are swept.</param>
    /// <param name="liabilityAccountCode">The gift-card liability account (debited).</param>
    /// <param name="breakageAccountCode">The breakage income account (credited).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of cards expired and the total breakage recognised, or an error.</returns>
    Task<Result<BreakageRunResult>> RecogniseExpiredBreakageAsync(long companyId, DateOnly asAtDate, string liabilityAccountCode, string breakageAccountCode, CancellationToken ct = default);
}

/// <summary>The result of a bulk breakage run.</summary>
/// <param name="CardsExpired">The number of cards whose balance was recognised as breakage.</param>
/// <param name="TotalBreakage">The total breakage income recognised.</param>
public readonly record struct BreakageRunResult(int CardsExpired, decimal TotalBreakage);

/// <summary>A line of a gift card's history.</summary>
/// <param name="OccurredOn">When.</param>
/// <param name="Event">What happened.</param>
/// <param name="Amount">The change in balance.</param>
/// <param name="BalanceAfter">The balance afterwards.</param>
/// <param name="Note">A reference or reason.</param>
public sealed record GiftCardHistoryLine(DateOnly OccurredOn, One4All.Core.Vouchers.GiftCardEvent Event, decimal Amount, decimal BalanceAfter, string? Note);

/// <summary>Gift card servicing beyond issue and redemption (brief §4.8.1).</summary>
public interface IGiftCardServicing
{
    /// <summary>Adds value to an active (or spent) card: Dr cash, Cr gift card liability.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="cardNumber">The card.</param>
    /// <param name="amount">The amount.</param>
    /// <param name="date">The date.</param>
    /// <param name="cashAccountCode">Where the money was received.</param>
    /// <param name="liabilityAccountCode">The gift card liability.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The card's new balance, or why it was refused.</returns>
    Task<Result<GiftCardResult>> TopUpAsync(long companyId, string cardNumber, decimal amount, DateOnly date, string cashAccountCode, string liabilityAccountCode, CancellationToken ct = default);

    /// <summary>Blocks a card reported lost or stolen; it can no longer be spent.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="cardNumber">The card.</param>
    /// <param name="date">The date.</param>
    /// <param name="reason">Why; required.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The blocked card, or why it was refused.</returns>
    Task<Result<GiftCardResult>> BlockAsync(long companyId, string cardNumber, DateOnly date, string reason, CancellationToken ct = default);

    /// <summary>
    /// Replaces a blocked card: a new card takes over its balance and expiry. The liability is unchanged, so nothing posts.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="blockedCardNumber">The blocked card.</param>
    /// <param name="newCardNumber">The replacement's number.</param>
    /// <param name="date">The date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The replacement card, or why it was refused.</returns>
    Task<Result<GiftCardResult>> ReplaceAsync(long companyId, string blockedCardNumber, string newCardNumber, DateOnly date, CancellationToken ct = default);

    /// <summary>A card's full history, oldest first.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="cardNumber">The card.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The history, or why there is none.</returns>
    Task<Result<IReadOnlyList<GiftCardHistoryLine>>> HistoryAsync(long companyId, string cardNumber, CancellationToken ct = default);

    /// <summary>
    /// Creates a range of printed card stock (prefix plus zero-padded serials, e.g. GC000100–GC000199), inactive with no value and no
    /// liability until activated. At most 10,000 cards per range; refused if any number in it exists (<c>Voucher.RangeOverlaps</c>).
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="prefix">The number prefix.</param>
    /// <param name="first">The first serial.</param>
    /// <param name="last">The last serial.</param>
    /// <param name="digits">The serial's width (zero-padded).</param>
    /// <param name="currency">The currency the cards hold.</param>
    /// <param name="date">When the stock is created.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>How many cards were created.</returns>
    Task<Result<int>> CreateRangeAsync(long companyId, string prefix, long first, long last, int digits, CurrencyCode currency, DateOnly date, CancellationToken ct = default);

    /// <summary>Activates a card of printed stock at sale: loads its value (Dr cash, Cr gift card liability) and makes it spendable.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="cardNumber">The card.</param>
    /// <param name="value">The value loaded.</param>
    /// <param name="date">The sale date.</param>
    /// <param name="expiryDate">When it expires.</param>
    /// <param name="cashAccountCode">Where the money went.</param>
    /// <param name="liabilityAccountCode">The gift card liability.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The card.</returns>
    Task<Result<GiftCardResult>> ActivateAsync(long companyId, string cardNumber, decimal value, DateOnly date, DateOnly? expiryDate, string cashAccountCode, string liabilityAccountCode,
        CancellationToken ct = default);
}
