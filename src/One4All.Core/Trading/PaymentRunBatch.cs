using One4All.Core.Common;

namespace One4All.Core.Trading;

/// <summary>Where a saved payment run stands.</summary>
public enum PaymentRunStatus
{
    /// <summary>Proposed: lines can be left out or part-paid.</summary>
    Proposed = 1,

    /// <summary>Approved by a second person and paid.</summary>
    Paid = 2,

    /// <summary>Withdrawn before approval.</summary>
    Cancelled = 3,
}

/// <summary>
/// A supplier payment run saved for review (brief §4.6): the proposed payments, editable (left out, part-paid) until someone other than
/// the proposer approves it, which pays exactly what is approved.
/// </summary>
public sealed class PaymentRunBatch : AuditableEntity, ICompanyScoped
{
    private readonly List<PaymentRunBatchLine> _lines = [];

    private PaymentRunBatch()
    {
        Number = string.Empty;
        BankAccountCode = string.Empty;
    }

    /// <summary>Saves a proposed run.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="number">Its number.</param>
    /// <param name="paymentDate">The payment date.</param>
    /// <param name="dueOnOrBefore">The due date it pays up to.</param>
    /// <param name="bankAccountCode">The bank it pays from.</param>
    /// <param name="proposedByUserId">Who proposed it.</param>
    public PaymentRunBatch(long companyId, string number, DateOnly paymentDate, DateOnly dueOnOrBefore, string bankAccountCode, long proposedByUserId)
    {
        CompanyId = companyId;
        Number = number;
        PaymentDate = paymentDate;
        DueOnOrBefore = dueOnOrBefore;
        BankAccountCode = bankAccountCode;
        ProposedByUserId = proposedByUserId;
        Status = PaymentRunStatus.Proposed;
    }

    /// <summary>The company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Its number.</summary>
    public string Number { get; private set; }

    /// <summary>The payment date.</summary>
    public DateOnly PaymentDate { get; private set; }

    /// <summary>The due date it pays up to.</summary>
    public DateOnly DueOnOrBefore { get; private set; }

    /// <summary>The bank it pays from.</summary>
    public string BankAccountCode { get; private set; }

    /// <summary>The withholding tax code asked for (else the company default for uncleared suppliers).</summary>
    public string? WithholdingTaxCode { get; set; }

    /// <summary>The bank file layout.</summary>
    public string? EftLayoutCode { get; set; }

    /// <summary>The currency it was limited to, if any.</summary>
    public string? Currency { get; set; }

    /// <summary>Where it stands.</summary>
    public PaymentRunStatus Status { get; private set; }

    /// <summary>Who proposed it.</summary>
    public long ProposedByUserId { get; private set; }

    /// <summary>Who approved it.</summary>
    public long? ApprovedByUserId { get; private set; }

    /// <summary>The proposed payments.</summary>
    public IReadOnlyList<PaymentRunBatchLine> Lines => _lines.AsReadOnly();

    /// <summary>Adds a proposed payment.</summary>
    /// <param name="line">The payment.</param>
    public void AddLine(PaymentRunBatchLine line) => _lines.Add(line);

    /// <summary>Marks it approved and paid.</summary>
    /// <param name="userId">Who approved it.</param>
    public void MarkPaid(long userId)
    {
        Status = PaymentRunStatus.Paid;
        ApprovedByUserId = userId;
    }

    /// <summary>Withdraws it.</summary>
    public void Cancel() => Status = PaymentRunStatus.Cancelled;
}

/// <summary>One supplier's proposed payment in a saved run.</summary>
public sealed class PaymentRunBatchLine : Entity
{
    private PaymentRunBatchLine()
    {
        SupplierCode = string.Empty;
        Currency = string.Empty;
    }

    /// <summary>Creates the line.</summary>
    /// <param name="supplierId">The supplier.</param>
    /// <param name="supplierCode">Its code.</param>
    /// <param name="currency">The currency paid in.</param>
    /// <param name="due">What is due after credits.</param>
    /// <param name="included">Whether it is paid.</param>
    /// <param name="note">Why it is left out (held, bank details changing).</param>
    public PaymentRunBatchLine(long supplierId, string supplierCode, string currency, decimal due, bool included, string? note)
    {
        SupplierId = supplierId;
        SupplierCode = supplierCode;
        Currency = currency;
        Due = due;
        Amount = due;
        Included = included;
        Note = note;
    }

    /// <summary>The run.</summary>
    public long PaymentRunBatchId { get; private set; }

    /// <summary>The supplier.</summary>
    public long SupplierId { get; private set; }

    /// <summary>Its code.</summary>
    public string SupplierCode { get; private set; }

    /// <summary>The currency.</summary>
    public string Currency { get; private set; }

    /// <summary>What is due after credits.</summary>
    public decimal Due { get; private set; }

    /// <summary>What will be paid (the due amount, or less for a part-payment).</summary>
    public decimal Amount { get; private set; }

    /// <summary>Whether it is paid.</summary>
    public bool Included { get; private set; }

    /// <summary>Why it is left out, if it is.</summary>
    public string? Note { get; private set; }

    /// <summary>The payment number once paid.</summary>
    public string? PaymentNumber { get; set; }

    /// <summary>Sets what is paid: left out, in full, or part.</summary>
    /// <param name="included">Whether it is paid.</param>
    /// <param name="amount">The amount (nil keeps the full due amount).</param>
    /// <returns>Success, or why not.</returns>
    public Result Set(bool included, decimal? amount)
    {
        if (amount is decimal part && (part <= 0m || part > Due))
        {
            return Result.Failure(Error.Validation("Ap.RunAmount", $"{SupplierCode} can be paid between 0 and {Due:N2} {Currency}."));
        }

        Included = included;
        Amount = amount ?? Due;
        return Result.Success();
    }
}
