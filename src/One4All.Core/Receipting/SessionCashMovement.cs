using One4All.Core.Accounting;
using One4All.Core.Common;

namespace One4All.Core.Receipting;

/// <summary>A mid-shift cash movement out of the drawer (brief §RCP-3).</summary>
public enum CashMovementType
{
    /// <summary>A pickup/drop of cash to the safe, reducing drawer exposure.</summary>
    Pickup = 1,

    /// <summary>A petty-cash pay-out at the counter, against an expense.</summary>
    PettyCashPayout = 2,
}

/// <summary>
/// A mid-shift cash movement recorded against a cashier session (brief §RCP-3): a pickup to the safe or a
/// petty-cash pay-out. Both remove cash from the drawer, so the blind cash-up expects less at close. Each
/// carries a reason and posts through the central engine.
/// </summary>
public sealed class SessionCashMovement : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private SessionCashMovement()
    {
        Reason = string.Empty;
    }

    /// <summary>Creates a cash movement.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="cashierSessionId">The session.</param>
    /// <param name="type">The movement type.</param>
    /// <param name="currency">The currency.</param>
    /// <param name="amount">The amount.</param>
    /// <param name="date">The date.</param>
    /// <param name="reason">The mandatory reason.</param>
    public SessionCashMovement(long companyId, long cashierSessionId, CashMovementType type, CurrencyCode currency, decimal amount, DateOnly date, string reason)
    {
        CompanyId = companyId;
        CashierSessionId = cashierSessionId;
        Type = type;
        Currency = currency;
        Amount = amount;
        Date = date;
        Reason = reason;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The session.</summary>
    public long CashierSessionId { get; private set; }

    /// <summary>The movement type.</summary>
    public CashMovementType Type { get; private set; }

    /// <summary>The currency.</summary>
    public CurrencyCode Currency { get; private set; }

    /// <summary>The amount.</summary>
    public decimal Amount { get; private set; }

    /// <summary>The date.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>The reason.</summary>
    public string Reason { get; private set; }
}
