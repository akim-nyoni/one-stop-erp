using One4All.Core.Common;

namespace One4All.Core.Receipting;

/// <summary>
/// An account a cashier may not receipt against (receipting §15): their own, their spouse's or a relative's, or a business they have an interest in.
/// Another cashier takes those payments; an attempt is refused and logged.
/// </summary>
public sealed class CashierRelatedAccount : AuditableEntity, ICompanyScoped
{
    private CashierRelatedAccount() => Relationship = string.Empty;

    /// <summary>Creates the link.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="userId">The cashier (user).</param>
    /// <param name="customerId">The account.</param>
    /// <param name="relationship">How it is related (Own, Spouse, Relative, Business…).</param>
    public CashierRelatedAccount(long companyId, long userId, long customerId, string relationship)
    {
        CompanyId = companyId;
        UserId = userId;
        CustomerId = customerId;
        Relationship = relationship;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>Gets the cashier.</summary>
    public long UserId { get; private set; }

    /// <summary>Gets the account.</summary>
    public long CustomerId { get; private set; }

    /// <summary>Gets how the account is related to the cashier.</summary>
    public string Relationship { get; private set; }
}
