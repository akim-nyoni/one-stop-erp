using One4All.Core.Common;
using One4All.Core.Pos;

namespace One4All.Application.Pos;

/// <summary>What each till event must carry before it is recorded.</summary>
public static class PosEventRules
{
    /// <summary>Events that need a reason.</summary>
    public static readonly IReadOnlySet<PosEventKind> NeedReason = new HashSet<PosEventKind>
    {
        PosEventKind.LineVoid, PosEventKind.SaleVoid, PosEventKind.Refund, PosEventKind.PriceOverride, PosEventKind.NoSale, PosEventKind.LineDiscount,
    };

    /// <summary>Events that need a supervisor's authorisation.</summary>
    public static readonly IReadOnlySet<PosEventKind> NeedAuthoriser = new HashSet<PosEventKind> { PosEventKind.Refund, PosEventKind.PriceOverride, PosEventKind.NoSale };

    /// <summary>Checks an event.</summary>
    /// <param name="kind">What happened.</param>
    /// <param name="amount">The value involved.</param>
    /// <param name="originalAmount">The value before (list price).</param>
    /// <param name="reason">The reason.</param>
    /// <param name="cashierUserId">The cashier.</param>
    /// <param name="authorisedByUserId">The authorising supervisor.</param>
    /// <param name="customerReference">The loyalty customer.</param>
    /// <returns>Null when valid; otherwise the error.</returns>
    public static Error? Validate(PosEventKind kind, decimal amount, decimal? originalAmount, string? reason, long cashierUserId, long? authorisedByUserId, string? customerReference)
    {
        if (!Enum.IsDefined(kind))
        {
            return Error.Validation("Pos.EventKind", "Unknown till event.");
        }

        if (amount < 0m)
        {
            return Error.Validation("Pos.EventAmount", "The amount cannot be negative.");
        }

        if (NeedReason.Contains(kind) && string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("Pos.ReasonRequired", $"A {kind} needs a reason.");
        }

        if (NeedAuthoriser.Contains(kind))
        {
            if (authorisedByUserId is null)
            {
                return Error.Validation("Pos.AuthorisationRequired", $"A {kind} needs a supervisor's authorisation.");
            }

            if (authorisedByUserId == cashierUserId)
            {
                return Error.Rule("Pos.SelfAuthorised", "A cashier cannot authorise their own override, refund or no-sale.");
            }
        }

        if (kind is PosEventKind.LineDiscount or PosEventKind.PriceOverride && originalAmount is null)
        {
            return Error.Validation("Pos.OriginalPriceRequired", "A discount or override records the list amount it changed.");
        }

        if (kind == PosEventKind.LineDiscount && originalAmount is { } list && amount > list)
        {
            return Error.Validation("Pos.DiscountTooLarge", "A discount cannot exceed the line's list amount.");
        }

        if (kind is PosEventKind.LoyaltyEarned or PosEventKind.LoyaltyRedeemed && string.IsNullOrWhiteSpace(customerReference))
        {
            return Error.Validation("Pos.LoyaltyCustomer", "Loyalty points need the customer's card or reference.");
        }

        return null;
    }

    /// <summary>A customer's loyalty balance: earned less redeemed.</summary>
    /// <param name="events">The customer's loyalty events.</param>
    /// <returns>Earned, redeemed and the balance.</returns>
    public static (decimal Earned, decimal Redeemed, decimal Balance) Loyalty(IEnumerable<(PosEventKind Kind, decimal Points)> events)
    {
        decimal earned = 0m, redeemed = 0m;
        foreach ((PosEventKind kind, decimal points) in events)
        {
            if (kind == PosEventKind.LoyaltyEarned)
            {
                earned += points;
            }
            else if (kind == PosEventKind.LoyaltyRedeemed)
            {
                redeemed += points;
            }
        }

        return (earned, redeemed, earned - redeemed);
    }
}
