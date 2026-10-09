using One4All.Core.Common;

namespace One4All.Core.Municipal;

/// <summary>Where a reviewed billing run stands.</summary>
public enum BillingRunStatus
{
    /// <summary>Proposed: bills worked out, nothing posted, open for review.</summary>
    Proposed = 0,

    /// <summary>Approved and posted to the receivables ledger.</summary>
    Posted = 1,

    /// <summary>Cancelled before posting.</summary>
    Cancelled = 2,

    /// <summary>Approved and posting in chunks; resumable until every included bill is posted or failed.</summary>
    Posting = 3,
}

/// <summary>What the review of a proposed bill found.</summary>
[Flags]
public enum BillingExceptions
{
    /// <summary>Nothing unusual.</summary>
    None = 0,

    /// <summary>The property's bill for the month is already on the ledger (left out).</summary>
    AlreadyBilled = 1,

    /// <summary>The charge works out at nothing (left out).</summary>
    NothingToBill = 2,

    /// <summary>The bill could not be worked out, for example no tariff (left out).</summary>
    Failed = 4,

    /// <summary>The charge differs from the last bill by more than the run's tolerance (kept, for review).</summary>
    LargeChange = 8,
}

/// <summary>
/// A reviewed billing run (<c>mun.billing_run</c>, brief §4.9): bills for a service, period and optionally a ward are
/// worked out in a proposed state with an exception report, reviewed (lines can be left out), approved by someone other
/// than the proposer and only then posted to the receivables ledger; a proposed run can be cancelled and re-run.
/// </summary>
public sealed class BillingRun : AuditableEntity, ICompanyScoped
{
    private readonly List<BillingRunLine> _lines = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private BillingRun()
    {
        RunNumber = string.Empty;
        InvoicePrefix = string.Empty;
    }

    /// <summary>Proposes a run.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="runNumber">The run number.</param>
    /// <param name="serviceType">The service billed.</param>
    /// <param name="billDate">The bill date.</param>
    /// <param name="ward">The ward billed, or null for all.</param>
    /// <param name="invoicePrefix">The bill-number prefix.</param>
    /// <param name="proposedByUserId">Who proposed it.</param>
    public BillingRun(long companyId, string runNumber, ServiceType serviceType, DateOnly billDate, string? ward, string invoicePrefix, long proposedByUserId)
    {
        CompanyId = companyId;
        RunNumber = runNumber;
        ServiceType = serviceType;
        BillDate = billDate;
        Period = new DateOnly(billDate.Year, billDate.Month, 1);
        Ward = ward;
        InvoicePrefix = invoicePrefix;
        ProposedByUserId = proposedByUserId;
        Status = BillingRunStatus.Proposed;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The run number.</summary>
    public string RunNumber { get; private set; }

    /// <summary>The service billed.</summary>
    public ServiceType ServiceType { get; private set; }

    /// <summary>The bill date.</summary>
    public DateOnly BillDate { get; private set; }

    /// <summary>The first day of the month billed.</summary>
    public DateOnly Period { get; private set; }

    /// <summary>The ward billed, or null for all.</summary>
    public string? Ward { get; private set; }

    /// <summary>The bill-number prefix.</summary>
    public string InvoicePrefix { get; private set; }

    /// <summary>Gets or sets the council service the run bills (null: the built-in category's own connections).</summary>
    public string? ServiceCode { get; set; }

    /// <summary>Gets or sets the meter-reading route the run was limited to (null: every route).</summary>
    public string? RouteCode { get; set; }

    /// <summary>Gets or sets the billing cycle the run was limited to (null: every cycle).</summary>
    public BillingCycle? Cycle { get; set; }

    /// <summary>Where it stands.</summary>
    public BillingRunStatus Status { get; private set; }

    /// <summary>Who proposed it.</summary>
    public long ProposedByUserId { get; private set; }

    /// <summary>Who approved it.</summary>
    public long? ApprovedByUserId { get; private set; }

    /// <summary>The proposed bills.</summary>
    public IReadOnlyList<BillingRunLine> Lines => _lines.AsReadOnly();

    /// <summary>Adds a proposed bill.</summary>
    /// <param name="line">The bill.</param>
    public void AddLine(BillingRunLine line) => _lines.Add(line);

    /// <summary>Marks it approved and posted.</summary>
    /// <param name="userId">Who approved it.</param>
    public void MarkPosted(long userId)
    {
        Status = BillingRunStatus.Posted;
        ApprovedByUserId = userId;
    }

    /// <summary>Approves it: its bills now post in chunks.</summary>
    /// <param name="userId">Who approved it.</param>
    public void StartPosting(long userId)
    {
        Status = BillingRunStatus.Posting;
        ApprovedByUserId = userId;
    }

    /// <summary>Every included bill is posted or failed.</summary>
    public void FinishPosting() => Status = BillingRunStatus.Posted;

    /// <summary>Cancels it before posting.</summary>
    public void Cancel() => Status = BillingRunStatus.Cancelled;
}

/// <summary>A proposed bill in a billing run (<c>mun.billing_run_line</c>).</summary>
public sealed class BillingRunLine : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private BillingRunLine()
    {
        StandNumber = string.Empty;
        InvoiceNumber = string.Empty;
        RevenueAccountCode = string.Empty;
        Narrative = string.Empty;
    }

    /// <summary>Proposes a bill.</summary>
    /// <param name="propertyId">The property.</param>
    /// <param name="standNumber">The stand.</param>
    /// <param name="ratepayerCustomerId">The ratepayer billed.</param>
    /// <param name="invoiceNumber">The bill number.</param>
    /// <param name="charge">The charge.</param>
    /// <param name="previousCharge">The last bill for the service, if any.</param>
    /// <param name="revenueAccountCode">The revenue account.</param>
    /// <param name="narrative">The bill narrative.</param>
    /// <param name="exceptions">What the review found.</param>
    /// <param name="message">Why it was left out, when it was.</param>
    public BillingRunLine(long propertyId, string standNumber, long ratepayerCustomerId, string invoiceNumber, decimal charge, decimal? previousCharge,
        string revenueAccountCode, string narrative, BillingExceptions exceptions, string? message)
    {
        PropertyId = propertyId;
        StandNumber = standNumber;
        RatepayerCustomerId = ratepayerCustomerId;
        InvoiceNumber = invoiceNumber;
        Charge = charge;
        PreviousCharge = previousCharge;
        RevenueAccountCode = revenueAccountCode;
        Narrative = narrative;
        Exceptions = exceptions;
        Message = message;
        Included = !IsBlocked;
    }

    /// <summary>The run.</summary>
    public long BillingRunId { get; private set; }

    /// <summary>The property.</summary>
    public long PropertyId { get; private set; }

    /// <summary>The stand.</summary>
    public string StandNumber { get; private set; }

    /// <summary>The ratepayer billed.</summary>
    public long RatepayerCustomerId { get; private set; }

    /// <summary>The bill number.</summary>
    public string InvoiceNumber { get; private set; }

    /// <summary>The charge.</summary>
    public decimal Charge { get; private set; }

    /// <summary>The last bill for the service, if any.</summary>
    public decimal? PreviousCharge { get; private set; }

    /// <summary>The revenue account.</summary>
    public string RevenueAccountCode { get; private set; }

    /// <summary>The bill narrative.</summary>
    public string Narrative { get; private set; }

    /// <summary>What the review found.</summary>
    public BillingExceptions Exceptions { get; private set; }

    /// <summary>Why it was left out, when it was.</summary>
    public string? Message { get; private set; }

    /// <summary>Whether it will be posted on approval.</summary>
    public bool Included { get; private set; }

    /// <summary>Whether an exception keeps it out of the run for good.</summary>
    public bool IsBlocked => (Exceptions & (BillingExceptions.AlreadyBilled | BillingExceptions.NothingToBill | BillingExceptions.Failed)) != 0;

    /// <summary>Puts it in or leaves it out of the run.</summary>
    /// <param name="included">Whether to post it.</param>
    public void SetIncluded(bool included) => Included = included && !IsBlocked;

    /// <summary>The invoice posted for it, once posted.</summary>
    public long? PostedOpenItemId { get; private set; }

    /// <summary>Whether it is posted (its bill is on the ratepayer's account).</summary>
    public bool IsPosted { get; private set; }

    /// <summary>Records the bill as posted.</summary>
    /// <param name="openItemId">Its invoice, when known.</param>
    public void MarkPosted(long? openItemId)
    {
        IsPosted = true;
        PostedOpenItemId = openItemId;
    }

    /// <summary>Records why the bill could not be posted; the rest of the run carries on.</summary>
    /// <param name="message">Why.</param>
    public void MarkFailed(string message)
    {
        Exceptions |= BillingExceptions.Failed;
        Message = message.Length > 500 ? message[..500] : message;
    }
}
