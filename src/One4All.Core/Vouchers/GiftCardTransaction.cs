using One4All.Core.Common;

namespace One4All.Core.Vouchers;

/// <summary>What happened to a gift card.</summary>
public enum GiftCardEvent
{
    /// <summary>Issued with its starting value.</summary>
    Issued = 1,

    /// <summary>Part or all of the balance spent.</summary>
    Redeemed = 2,

    /// <summary>Value added.</summary>
    ToppedUp = 3,

    /// <summary>Blocked as lost or stolen.</summary>
    Blocked = 4,

    /// <summary>Its balance moved to a replacement card.</summary>
    ReplacedOut = 5,

    /// <summary>Issued as a replacement, taking over a blocked card's balance.</summary>
    ReplacedIn = 6,

    /// <summary>Expired, its balance recognised as breakage.</summary>
    Breakage = 7,
}

/// <summary>
/// A line in a gift card's history (<c>vou.gift_card_transaction</c>, brief §4.8.1 full transaction history): every
/// issue, spend, top-up, block, replacement and breakage, with the balance after it.
/// </summary>
public sealed class GiftCardTransaction : Entity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private GiftCardTransaction()
    {
    }

    /// <summary>Records an event.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="giftCardId">The card.</param>
    /// <param name="occurredOn">When.</param>
    /// <param name="event">What happened.</param>
    /// <param name="amount">The change in balance (negative for spends and transfers out).</param>
    /// <param name="balanceAfter">The balance afterwards.</param>
    /// <param name="note">A reference or reason.</param>
    public GiftCardTransaction(long companyId, long giftCardId, DateOnly occurredOn, GiftCardEvent @event, decimal amount, decimal balanceAfter, string? note)
    {
        CompanyId = companyId;
        GiftCardId = giftCardId;
        OccurredOn = occurredOn;
        Event = @event;
        Amount = amount;
        BalanceAfter = balanceAfter;
        Note = note;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The card.</summary>
    public long GiftCardId { get; private set; }

    /// <summary>When.</summary>
    public DateOnly OccurredOn { get; private set; }

    /// <summary>What happened.</summary>
    public GiftCardEvent Event { get; private set; }

    /// <summary>The change in balance.</summary>
    public decimal Amount { get; private set; }

    /// <summary>The balance afterwards.</summary>
    public decimal BalanceAfter { get; private set; }

    /// <summary>A reference or reason.</summary>
    public string? Note { get; private set; }
}
