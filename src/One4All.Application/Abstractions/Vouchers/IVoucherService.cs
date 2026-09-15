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
}
