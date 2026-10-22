using One4All.Core.Common;

namespace One4All.Core.Housing;

/// <summary>
/// A purchase deposit paid against an accepted stand offer before the agreement is signed (<c>hse.stand_deposit</c>,
/// brief §14 H51): held on a liability account until applied to the buyer's account or refunded.
/// </summary>
public sealed class StandDeposit : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private StandDeposit()
    {
        ReceiptReference = string.Empty;
        LiabilityAccountCode = string.Empty;
        JournalNumber = string.Empty;
    }

    /// <summary>Records a deposit received.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="allocationId">The accepted allocation it secures.</param>
    /// <param name="standId">The stand.</param>
    /// <param name="customerId">The payer's account.</param>
    /// <param name="amount">The amount.</param>
    /// <param name="receivedOn">When.</param>
    /// <param name="receiptReference">The receipt.</param>
    /// <param name="liabilityAccountCode">The account it is held on.</param>
    /// <param name="journalNumber">The journal.</param>
    public StandDeposit(long companyId, long allocationId, long standId, long customerId, decimal amount, DateOnly receivedOn, string receiptReference, string liabilityAccountCode, string journalNumber)
    {
        CompanyId = companyId;
        AllocationId = allocationId;
        StandId = standId;
        CustomerId = customerId;
        Amount = amount;
        ReceivedOn = receivedOn;
        ReceiptReference = receiptReference;
        LiabilityAccountCode = liabilityAccountCode;
        JournalNumber = journalNumber;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The accepted allocation it secures.</summary>
    public long AllocationId { get; private set; }

    /// <summary>The stand.</summary>
    public long StandId { get; private set; }

    /// <summary>The payer's account.</summary>
    public long CustomerId { get; private set; }

    /// <summary>The amount.</summary>
    public decimal Amount { get; private set; }

    /// <summary>When it was received.</summary>
    public DateOnly ReceivedOn { get; private set; }

    /// <summary>The receipt.</summary>
    public string ReceiptReference { get; private set; }

    /// <summary>The liability account it is held on.</summary>
    public string LiabilityAccountCode { get; private set; }

    /// <summary>The journal that received it.</summary>
    public string JournalNumber { get; private set; }

    /// <summary>When it was applied to the buyer's account or refunded; held while null.</summary>
    public DateOnly? ClearedOn { get; private set; }

    /// <summary>True when refunded rather than applied.</summary>
    public bool Refunded { get; private set; }

    /// <summary>The journal that cleared it.</summary>
    public string? ClearingJournalNumber { get; private set; }

    /// <summary>Clears the deposit.</summary>
    /// <param name="date">When.</param>
    /// <param name="refunded">True for a refund, false when applied.</param>
    /// <param name="journalNumber">The journal.</param>
    public void Clear(DateOnly date, bool refunded, string journalNumber)
    {
        ClearedOn = date;
        Refunded = refunded;
        ClearingJournalNumber = journalNumber;
    }
}

/// <summary>
/// An instalment agreement cancelled at the buyer's request (<c>hse.agreement_cancellation</c>, brief §14 H53): the stand
/// comes back (through repossession) and the buyer is refunded what was paid less occupation rental for the months
/// occupied, the admin charge and any arrears.
/// </summary>
public sealed class AgreementCancellation : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private AgreementCancellation()
    {
        AgreementNumber = string.Empty;
        Reason = string.Empty;
        RepossessionReference = string.Empty;
    }

    /// <summary>Records a cancellation.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="agreementNumber">The agreement.</param>
    /// <param name="date">When.</param>
    /// <param name="reason">Why.</param>
    /// <param name="paid">What the buyer paid.</param>
    /// <param name="monthsOccupied">Months the buyer occupied the stand.</param>
    /// <param name="occupationRental">Rental charged for those months.</param>
    /// <param name="adminCharge">The admin charge.</param>
    /// <param name="arrears">Instalments billed and unpaid.</param>
    /// <param name="refund">The refund.</param>
    /// <param name="repossessionReference">The repossession that returned the stand.</param>
    /// <param name="refundJournalNumber">The refund journal, when there is a refund.</param>
    public AgreementCancellation(long companyId, string agreementNumber, DateOnly date, string reason, decimal paid, int monthsOccupied, decimal occupationRental, decimal adminCharge,
        decimal arrears, decimal refund, string repossessionReference, string? refundJournalNumber)
    {
        CompanyId = companyId;
        AgreementNumber = agreementNumber;
        Date = date;
        Reason = reason;
        Paid = paid;
        MonthsOccupied = monthsOccupied;
        OccupationRental = occupationRental;
        AdminCharge = adminCharge;
        Arrears = arrears;
        Refund = refund;
        RepossessionReference = repossessionReference;
        RefundJournalNumber = refundJournalNumber;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The agreement.</summary>
    public string AgreementNumber { get; private set; }

    /// <summary>When.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>Why.</summary>
    public string Reason { get; private set; }

    /// <summary>What the buyer paid.</summary>
    public decimal Paid { get; private set; }

    /// <summary>Months occupied.</summary>
    public int MonthsOccupied { get; private set; }

    /// <summary>Occupation rental for those months.</summary>
    public decimal OccupationRental { get; private set; }

    /// <summary>The admin charge.</summary>
    public decimal AdminCharge { get; private set; }

    /// <summary>Instalments billed and unpaid.</summary>
    public decimal Arrears { get; private set; }

    /// <summary>The refund.</summary>
    public decimal Refund { get; private set; }

    /// <summary>The repossession that returned the stand.</summary>
    public string RepossessionReference { get; private set; }

    /// <summary>The refund journal, when there is a refund.</summary>
    public string? RefundJournalNumber { get; private set; }
}

/// <summary>Where a purchase deposit refund request stands.</summary>
public enum DepositRefundStatus
{
    /// <summary>Asked for; the deposit is still held.</summary>
    Pending = 0,

    /// <summary>Approved by someone other than who asked; the deposit is refunded.</summary>
    Approved = 1,

    /// <summary>Turned down, with the reason.</summary>
    Rejected = 2,
}

/// <summary>
/// A request to refund a held purchase deposit (<c>hse.stand_deposit_refund_request</c>): keyed by one user with the reason and the bank account,
/// once the allocation it secured is no longer going ahead, and paid out only when another user holding the deposit refund right approves it.
/// </summary>
public sealed class DepositRefundRequest : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private DepositRefundRequest()
    {
        CertificateNumber = string.Empty;
        BankAccountCode = string.Empty;
        Reason = string.Empty;
    }

    /// <summary>Creates the request.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="depositId">The held deposit.</param>
    /// <param name="certificateNumber">The allocation certificate it secured.</param>
    /// <param name="date">The refund date.</param>
    /// <param name="bankAccountCode">The bank account it is paid from.</param>
    /// <param name="reason">Why it is refunded.</param>
    /// <param name="requestedByUserId">Who asks.</param>
    public DepositRefundRequest(long companyId, long depositId, string certificateNumber, DateOnly date, string bankAccountCode, string reason, long requestedByUserId)
    {
        CompanyId = companyId;
        DepositId = depositId;
        CertificateNumber = certificateNumber;
        Date = date;
        BankAccountCode = bankAccountCode;
        Reason = reason;
        RequestedByUserId = requestedByUserId;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>Gets the held deposit.</summary>
    public long DepositId { get; private set; }

    /// <summary>Gets the allocation certificate it secured.</summary>
    public string CertificateNumber { get; private set; }

    /// <summary>Gets the refund date.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>Gets the bank account it is paid from.</summary>
    public string BankAccountCode { get; private set; }

    /// <summary>Gets why it is refunded.</summary>
    public string Reason { get; private set; }

    /// <summary>Gets who asked.</summary>
    public long RequestedByUserId { get; private set; }

    /// <summary>Gets where it stands.</summary>
    public DepositRefundStatus Status { get; private set; }

    /// <summary>Gets who decided it.</summary>
    public long? DecidedByUserId { get; private set; }

    /// <summary>Gets the refund journal once approved.</summary>
    public string? JournalNumber { get; private set; }

    /// <summary>Gets why it was turned down.</summary>
    public string? RejectionReason { get; private set; }

    /// <summary>Records the approval (the caller has paid the refund).</summary>
    /// <param name="userId">Who approved it.</param>
    /// <param name="journalNumber">The refund journal.</param>
    public void Approve(long userId, string journalNumber)
    {
        Status = DepositRefundStatus.Approved;
        DecidedByUserId = userId;
        JournalNumber = journalNumber;
    }

    /// <summary>Turns it down.</summary>
    /// <param name="userId">Who.</param>
    /// <param name="reason">Why.</param>
    public void Reject(long userId, string reason)
    {
        Status = DepositRefundStatus.Rejected;
        DecidedByUserId = userId;
        RejectionReason = reason;
    }
}
