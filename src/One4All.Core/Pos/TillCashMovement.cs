using One4All.Core.Common;

namespace One4All.Core.Pos;

/// <summary>What a till cash movement is (brief §4.1).</summary>
public enum TillCashKind
{
    /// <summary>Cash taken out of the drawer to the safe or bank (a pickup / safe drop).</summary>
    Pickup = 1,

    /// <summary>Cash paid out of the drawer for an expense.</summary>
    PaidOut = 2,

    /// <summary>Cash added to the drawer's float.</summary>
    FloatTopUp = 3,

    /// <summary>Cash paid back to a customer from the drawer for a refund (posted by the refund itself).</summary>
    Refund = 4,
}

/// <summary>
/// Cash moved in or out of a till drawer during a session other than by sales (<c>pos.till_cash_movement</c>): pickups to the safe,
/// paid-outs for expenses and float top-ups — each posted, and each counted in the cash expected at cash-up.
/// </summary>
public sealed class TillCashMovement : Entity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private TillCashMovement()
    {
        Reason = string.Empty;
        AccountCode = string.Empty;
        JournalNumber = string.Empty;
    }

    /// <summary>Creates the movement.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="tillSessionId">The till session.</param>
    /// <param name="kind">What it is.</param>
    /// <param name="amount">The amount (positive).</param>
    /// <param name="accountCode">The other side: the safe / bank, the expense, or where the float came from.</param>
    /// <param name="reason">Why.</param>
    /// <param name="userId">Who did it.</param>
    /// <param name="authorisedByUserId">Who authorised it.</param>
    /// <param name="date">The date.</param>
    /// <param name="journalNumber">The posting.</param>
    public TillCashMovement(long companyId, long tillSessionId, TillCashKind kind, decimal amount, string accountCode, string reason, long userId, long? authorisedByUserId, DateOnly date,
        string journalNumber)
    {
        CompanyId = companyId;
        TillSessionId = tillSessionId;
        Kind = kind;
        Amount = amount;
        AccountCode = accountCode;
        Reason = reason;
        UserId = userId;
        AuthorisedByUserId = authorisedByUserId;
        Date = date;
        JournalNumber = journalNumber;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The till session.</summary>
    public long TillSessionId { get; private set; }

    /// <summary>What it is.</summary>
    public TillCashKind Kind { get; private set; }

    /// <summary>The amount (positive).</summary>
    public decimal Amount { get; private set; }

    /// <summary>The other side of the posting.</summary>
    public string AccountCode { get; private set; }

    /// <summary>Why.</summary>
    public string Reason { get; private set; }

    /// <summary>Who did it.</summary>
    public long UserId { get; private set; }

    /// <summary>Who authorised it.</summary>
    public long? AuthorisedByUserId { get; private set; }

    /// <summary>The date.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>The posting.</summary>
    public string JournalNumber { get; private set; }

    /// <summary>Gets the effect on the cash in the drawer.</summary>
    public decimal DrawerEffect => Kind == TillCashKind.FloatTopUp ? Amount : -Amount;
}
