using One4All.Core.Common;

namespace One4All.Core.Housing;

/// <summary>A fee an applicant pays to the waiting list (brief §14 H27).</summary>
public enum ApplicantFeeType
{
    /// <summary>Paid on registration.</summary>
    Registration = 1,

    /// <summary>Paid on each renewal / confirmation.</summary>
    Renewal = 2,
}

/// <summary>
/// A waiting-list fee received (<c>hse.applicant_fee</c>, brief §14 H27): who paid, what for, the receipt and the journal
/// that took it to income.
/// </summary>
public sealed class ApplicantFee : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private ApplicantFee()
    {
        ReceiptReference = string.Empty;
        JournalNumber = string.Empty;
    }

    /// <summary>Records a fee.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="applicantId">The applicant.</param>
    /// <param name="feeType">What it is for.</param>
    /// <param name="amount">The amount.</param>
    /// <param name="paidOn">When.</param>
    /// <param name="receiptReference">The receipt.</param>
    /// <param name="journalNumber">The journal.</param>
    public ApplicantFee(long companyId, long applicantId, ApplicantFeeType feeType, decimal amount, DateOnly paidOn, string receiptReference, string journalNumber)
    {
        CompanyId = companyId;
        ApplicantId = applicantId;
        FeeType = feeType;
        Amount = amount;
        PaidOn = paidOn;
        ReceiptReference = receiptReference;
        JournalNumber = journalNumber;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The applicant.</summary>
    public long ApplicantId { get; private set; }

    /// <summary>What it is for.</summary>
    public ApplicantFeeType FeeType { get; private set; }

    /// <summary>The amount.</summary>
    public decimal Amount { get; private set; }

    /// <summary>When it was paid.</summary>
    public DateOnly PaidOn { get; private set; }

    /// <summary>The receipt.</summary>
    public string ReceiptReference { get; private set; }

    /// <summary>The journal.</summary>
    public string JournalNumber { get; private set; }
}

/// <summary>
/// Transfer duty (or capital-gains withholding) the council collects as agent on an ownership transfer and remits to the
/// revenue authority (<c>hse.transfer_duty</c>, brief §14 H63): assessed on the consideration, held as a liability until
/// remitted, both legs journaled.
/// </summary>
public sealed class TransferDuty : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private TransferDuty()
    {
        ReceiptReference = string.Empty;
        LiabilityAccountCode = string.Empty;
        JournalNumber = string.Empty;
    }

    /// <summary>Records duty withheld.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="transferApplicationId">The transfer.</param>
    /// <param name="consideration">The consideration it was assessed on.</param>
    /// <param name="ratePercent">The rate.</param>
    /// <param name="amount">The duty.</param>
    /// <param name="withheldOn">When it was received.</param>
    /// <param name="receiptReference">The receipt.</param>
    /// <param name="liabilityAccountCode">The account it is held on.</param>
    /// <param name="journalNumber">The journal.</param>
    public TransferDuty(long companyId, long transferApplicationId, decimal consideration, decimal ratePercent, decimal amount, DateOnly withheldOn, string receiptReference,
        string liabilityAccountCode, string journalNumber)
    {
        CompanyId = companyId;
        TransferApplicationId = transferApplicationId;
        Consideration = consideration;
        RatePercent = ratePercent;
        Amount = amount;
        WithheldOn = withheldOn;
        ReceiptReference = receiptReference;
        LiabilityAccountCode = liabilityAccountCode;
        JournalNumber = journalNumber;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The transfer.</summary>
    public long TransferApplicationId { get; private set; }

    /// <summary>The consideration it was assessed on.</summary>
    public decimal Consideration { get; private set; }

    /// <summary>The rate, in percent.</summary>
    public decimal RatePercent { get; private set; }

    /// <summary>The duty.</summary>
    public decimal Amount { get; private set; }

    /// <summary>When it was received.</summary>
    public DateOnly WithheldOn { get; private set; }

    /// <summary>The receipt.</summary>
    public string ReceiptReference { get; private set; }

    /// <summary>The liability account it is held on until remitted.</summary>
    public string LiabilityAccountCode { get; private set; }

    /// <summary>The journal that received it.</summary>
    public string JournalNumber { get; private set; }

    /// <summary>When it was remitted; held while null.</summary>
    public DateOnly? RemittedOn { get; private set; }

    /// <summary>The revenue authority's acknowledgement.</summary>
    public string? RemittanceReference { get; private set; }

    /// <summary>The journal that paid it over.</summary>
    public string? RemittanceJournalNumber { get; private set; }

    /// <summary>Marks it remitted.</summary>
    /// <param name="date">When.</param>
    /// <param name="reference">The acknowledgement.</param>
    /// <param name="journalNumber">The journal.</param>
    public void Remit(DateOnly date, string reference, string journalNumber)
    {
        RemittedOn = date;
        RemittanceReference = reference;
        RemittanceJournalNumber = journalNumber;
    }
}
