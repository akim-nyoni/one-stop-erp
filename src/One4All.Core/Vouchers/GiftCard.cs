using One4All.Core.Accounting;
using One4All.Core.Common;

namespace One4All.Core.Vouchers;

/// <summary>The status of a gift card / gift voucher (brief §4.8).</summary>
public enum GiftCardStatus
{
    /// <summary>Active with a balance.</summary>
    Active = 0,

    /// <summary>Fully redeemed.</summary>
    Redeemed = 1,

    /// <summary>Expired (residual balance recognised as breakage).</summary>
    Expired = 2,

    /// <summary>Blocked (lost/stolen).</summary>
    Blocked = 3,

    /// <summary>Printed card stock not yet sold: no value and no liability until activated.</summary>
    Inactive = 4,
}

/// <summary>
/// A gift card / gift voucher (brief §4.8.1, <c>vou.gift_card</c>). Sold at POS it is a <b>liability</b> (not
/// revenue) until redeemed; redemption draws the balance down as a tender; an expired residual is recognised
/// as breakage income. Serial/balance/expiry are tracked here.
/// </summary>
public sealed class GiftCard : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private GiftCard() => CardNumber = string.Empty;

    private GiftCard(long companyId, string cardNumber, CurrencyCode currency, DateOnly createdOn)
    {
        CompanyId = companyId;
        CardNumber = cardNumber.Trim().ToUpperInvariant();
        Currency = currency;
        IssuedDate = createdOn;
        Status = GiftCardStatus.Inactive;
    }

    /// <summary>Creates a card of printed stock, inactive until sold.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="cardNumber">The card number (serial/barcode).</param>
    /// <param name="currency">The currency it will hold.</param>
    /// <param name="createdOn">When the stock was created.</param>
    /// <returns>The inactive card.</returns>
    public static GiftCard Blank(long companyId, string cardNumber, CurrencyCode currency, DateOnly createdOn) => new(companyId, cardNumber, currency, createdOn);

    /// <summary>Activates printed stock at sale with its value.</summary>
    /// <param name="value">The value loaded.</param>
    /// <param name="date">The sale date.</param>
    /// <param name="expiryDate">When it expires.</param>
    /// <returns>Success, or why not.</returns>
    public Result Activate(decimal value, DateOnly date, DateOnly? expiryDate)
    {
        if (Status != GiftCardStatus.Inactive || value <= 0m)
        {
            return Result.Failure(Error.Rule("Voucher.CannotActivate", $"Card {CardNumber} is {Status}; only inactive stock is activated, with a positive value."));
        }

        OriginalValue = value;
        Balance = value;
        IssuedDate = date;
        ExpiryDate = expiryDate;
        Status = GiftCardStatus.Active;
        return Result.Success();
    }

    /// <summary>Issues a gift card.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="cardNumber">The card number/serial.</param>
    /// <param name="currency">The card currency.</param>
    /// <param name="value">The issued value.</param>
    /// <param name="issuedDate">The issue date.</param>
    /// <param name="expiryDate">The expiry date, if any.</param>
    public GiftCard(long companyId, string cardNumber, CurrencyCode currency, decimal value, DateOnly issuedDate, DateOnly? expiryDate)
    {
        CompanyId = companyId;
        CardNumber = cardNumber.Trim().ToUpperInvariant();
        Currency = currency;
        OriginalValue = value;
        Balance = value;
        IssuedDate = issuedDate;
        ExpiryDate = expiryDate;
        Status = GiftCardStatus.Active;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The card number/serial.</summary>
    public string CardNumber { get; private set; }

    /// <summary>The card currency.</summary>
    public CurrencyCode Currency { get; private set; }

    /// <summary>The originally issued value.</summary>
    public decimal OriginalValue { get; private set; }

    /// <summary>The current balance.</summary>
    public decimal Balance { get; private set; }

    /// <summary>The issue date.</summary>
    public DateOnly IssuedDate { get; private set; }

    /// <summary>The expiry date, if any.</summary>
    public DateOnly? ExpiryDate { get; private set; }

    /// <summary>The status.</summary>
    public GiftCardStatus Status { get; private set; }

    /// <summary>The card that replaced this one, when it was lost or stolen.</summary>
    public long? ReplacedByCardId { get; private set; }

    /// <summary>Moves a blocked card's balance to its replacement, leaving it empty.</summary>
    /// <param name="replacementId">The replacement card.</param>
    /// <returns>The balance moved.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the card is not blocked or was already replaced.</exception>
    public decimal TransferToReplacement(long replacementId)
    {
        if (Status != GiftCardStatus.Blocked || ReplacedByCardId is not null)
        {
            throw new InvalidOperationException("Only a blocked card that has not been replaced can be replaced.");
        }

        decimal moved = Balance;
        Balance = 0m;
        ReplacedByCardId = replacementId;
        return moved;
    }

    /// <summary>Redeems an amount from the card, reducing the balance.</summary>
    /// <param name="amount">The amount to redeem.</param>
    /// <returns>Success, or a rule error when blocked/expired/insufficient.</returns>
    public Result Redeem(decimal amount)
    {
        if (Status != GiftCardStatus.Active)
        {
            return Result.Failure(Error.Rule("Voucher.NotActive", $"Card {CardNumber} is {Status}."));
        }

        if (amount <= 0m || amount > Balance)
        {
            return Result.Failure(Error.Rule("Voucher.InsufficientBalance", $"Card {CardNumber} balance {Balance} cannot cover {amount}."));
        }

        Balance = decimal.Round(Balance - amount, 4, MidpointRounding.ToEven);
        if (Balance == 0m)
        {
            Status = GiftCardStatus.Redeemed;
        }

        return Result.Success();
    }

    /// <summary>Tops up the card balance.</summary>
    /// <param name="amount">The amount to add.</param>
    public void TopUp(decimal amount)
    {
        Balance = decimal.Round(Balance + amount, 4, MidpointRounding.ToEven);
        if (Status == GiftCardStatus.Redeemed && Balance > 0m)
        {
            Status = GiftCardStatus.Active;
        }
    }

    /// <summary>Blocks the card (lost/stolen).</summary>
    public void Block() => Status = GiftCardStatus.Blocked;

    /// <summary>Recognises the residual balance as breakage and marks the card expired.</summary>
    /// <returns>The breakage amount recognised.</returns>
    public decimal RecogniseBreakage()
    {
        decimal breakage = Balance;
        Balance = 0m;
        Status = GiftCardStatus.Expired;
        return breakage;
    }
}
