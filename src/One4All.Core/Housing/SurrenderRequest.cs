using One4All.Core.Common;

namespace One4All.Core.Housing;

/// <summary>Where a stand surrender request stands.</summary>
public enum SurrenderRequestStatus
{
    /// <summary>Asked for; the holder still holds the stand.</summary>
    Pending = 0,

    /// <summary>Approved by someone other than who asked; the stand is surrendered.</summary>
    Approved = 1,

    /// <summary>Turned down, with the reason.</summary>
    Rejected = 2,
}

/// <summary>
/// A request to take a stand back from its holder (<c>hse.stand_surrender_request</c>): keyed by one user with the accounts and amounts the surrender
/// needs, and carried out only when another user approves it, so the second signature is a real approval rather than a name typed on the form.
/// </summary>
public sealed class SurrenderRequest : AuditableEntity, ICompanyScoped
{
    private SurrenderRequest()
    {
        StandNumber = string.Empty;
        Reason = string.Empty;
        GainLossAccountCode = string.Empty;
        StandsAvailableAccountCode = string.Empty;
    }

    /// <summary>Creates the request.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="standNumber">The stand.</param>
    /// <param name="date">The surrender date.</param>
    /// <param name="reason">Why the holder gives it back.</param>
    /// <param name="gainLossAccountCode">The surrender gain / loss account.</param>
    /// <param name="standsAvailableAccountCode">The stands available (inventory) account, for a sold stand.</param>
    /// <param name="netRealisableValue">The stand's net realisable value, for a sold stand.</param>
    /// <param name="refund">The refund to the holder.</param>
    /// <param name="refundAccountCode">The account the refund is charged to.</param>
    /// <param name="requestedByUserId">Who asks.</param>
    public SurrenderRequest(long companyId, string standNumber, DateOnly date, string reason, string gainLossAccountCode, string standsAvailableAccountCode,
        decimal netRealisableValue, decimal refund, string? refundAccountCode, long requestedByUserId)
    {
        CompanyId = companyId;
        StandNumber = standNumber;
        Date = date;
        Reason = reason;
        GainLossAccountCode = gainLossAccountCode;
        StandsAvailableAccountCode = standsAvailableAccountCode;
        NetRealisableValue = netRealisableValue;
        Refund = refund;
        RefundAccountCode = refundAccountCode;
        RequestedByUserId = requestedByUserId;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>Gets the stand.</summary>
    public string StandNumber { get; private set; }

    /// <summary>Gets the surrender date.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>Gets why the holder gives it back.</summary>
    public string Reason { get; private set; }

    /// <summary>Gets the surrender gain / loss account.</summary>
    public string GainLossAccountCode { get; private set; }

    /// <summary>Gets the stands available (inventory) account, for a sold stand.</summary>
    public string StandsAvailableAccountCode { get; private set; }

    /// <summary>Gets the stand's net realisable value, for a sold stand.</summary>
    public decimal NetRealisableValue { get; private set; }

    /// <summary>Gets the refund to the holder.</summary>
    public decimal Refund { get; private set; }

    /// <summary>Gets the account the refund is charged to.</summary>
    public string? RefundAccountCode { get; private set; }

    /// <summary>Gets who asked.</summary>
    public long RequestedByUserId { get; private set; }

    /// <summary>Gets where it stands.</summary>
    public SurrenderRequestStatus Status { get; private set; }

    /// <summary>Gets who decided it.</summary>
    public long? DecidedByUserId { get; private set; }

    /// <summary>Gets the surrender's reference once approved.</summary>
    public string? SurrenderReference { get; private set; }

    /// <summary>Gets why it was turned down.</summary>
    public string? RejectionReason { get; private set; }

    /// <summary>Records the approval (the caller has carried out the surrender).</summary>
    /// <param name="userId">Who approved it.</param>
    /// <param name="surrenderReference">The surrender's reference.</param>
    public void Approve(long userId, string surrenderReference)
    {
        Status = SurrenderRequestStatus.Approved;
        DecidedByUserId = userId;
        SurrenderReference = surrenderReference;
    }

    /// <summary>Turns it down.</summary>
    /// <param name="userId">Who.</param>
    /// <param name="reason">Why.</param>
    public void Reject(long userId, string reason)
    {
        Status = SurrenderRequestStatus.Rejected;
        DecidedByUserId = userId;
        RejectionReason = reason;
    }
}
