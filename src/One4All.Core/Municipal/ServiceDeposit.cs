using One4All.Core.Common;

namespace One4All.Core.Municipal;

/// <summary>Where a service deposit stands.</summary>
public enum ServiceDepositStatus
{
    /// <summary>Held against the service (a liability of the council).</summary>
    Held = 0,

    /// <summary>Refunded to the consumer.</summary>
    Refunded = 1,

    /// <summary>Applied to the account's arrears on a final bill.</summary>
    Forfeited = 2,
}

/// <summary>
/// A deposit held against a property's service (<c>mun.service_deposit</c>, brief §4.9): taken when the service is
/// connected and held as a liability, not income, then refunded to the consumer or applied to the account's arrears when
/// the service ends. A property's service holds one deposit at a time.
/// </summary>
public sealed class ServiceDeposit : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private ServiceDeposit()
    {
        Reference = string.Empty;
        LiabilityAccountCode = string.Empty;
    }

    /// <summary>Takes a deposit.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="reference">The deposit reference.</param>
    /// <param name="propertyId">The property.</param>
    /// <param name="ratepayerCustomerId">The account the deposit belongs to.</param>
    /// <param name="serviceType">The service.</param>
    /// <param name="amount">The amount held.</param>
    /// <param name="liabilityAccountCode">The liability account it sits on.</param>
    /// <param name="takenOn">When it was taken.</param>
    public ServiceDeposit(long companyId, string reference, long propertyId, long ratepayerCustomerId, ServiceType serviceType, decimal amount, string liabilityAccountCode, DateOnly takenOn)
    {
        CompanyId = companyId;
        Reference = reference;
        PropertyId = propertyId;
        RatepayerCustomerId = ratepayerCustomerId;
        ServiceType = serviceType;
        Amount = amount;
        LiabilityAccountCode = liabilityAccountCode;
        TakenOn = takenOn;
        Status = ServiceDepositStatus.Held;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The deposit reference.</summary>
    public string Reference { get; private set; }

    /// <summary>The property.</summary>
    public long PropertyId { get; private set; }

    /// <summary>The account the deposit belongs to.</summary>
    public long RatepayerCustomerId { get; private set; }

    /// <summary>The service.</summary>
    public ServiceType ServiceType { get; private set; }

    /// <summary>The amount held.</summary>
    public decimal Amount { get; private set; }

    /// <summary>The liability account it sits on.</summary>
    public string LiabilityAccountCode { get; private set; }

    /// <summary>When it was taken.</summary>
    public DateOnly TakenOn { get; private set; }

    /// <summary>Where it stands.</summary>
    public ServiceDepositStatus Status { get; private set; }

    /// <summary>When it was released (refunded or forfeited).</summary>
    public DateOnly? ReleasedOn { get; private set; }

    /// <summary>Releases it.</summary>
    /// <param name="status">Refunded or forfeited.</param>
    /// <param name="date">When.</param>
    public void Release(ServiceDepositStatus status, DateOnly date)
    {
        Status = status;
        ReleasedOn = date;
    }
}
