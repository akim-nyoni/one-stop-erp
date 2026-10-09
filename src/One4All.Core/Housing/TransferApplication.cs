using One4All.Core.Common;

namespace One4All.Core.Housing;

/// <summary>The kind of change of ownership (brief §4.10.6). Surrender and repossession go through the repossession process.</summary>
public enum TransferType
{
    /// <summary>Sale / assignment to a third party.</summary>
    Sale = 1,

    /// <summary>Cession of rights (pre-title, the common case).</summary>
    Cession = 2,

    /// <summary>Inheritance / deceased estate.</summary>
    Inheritance = 3,

    /// <summary>Donation or gift.</summary>
    Donation = 4,

    /// <summary>Divorce or court order.</summary>
    CourtOrder = 5,

    /// <summary>Matrimonial addition or removal of a spouse.</summary>
    Matrimonial = 6,

    /// <summary>Transfer to or from a company or trust.</summary>
    CompanyOrTrust = 7,

    /// <summary>Change of name (marriage, deed poll).</summary>
    ChangeOfName = 8,

    /// <summary>Correction of a captured error.</summary>
    Correction = 9,

    /// <summary>Substitution of a deceased joint holder.</summary>
    DeceasedJointHolder = 10,
}

/// <summary>A pre-condition of a transfer (brief §4.10.6).</summary>
public enum TransferRequirement
{
    /// <summary>Municipal clearance — checked live against the municipal register, never typed.</summary>
    MunicipalClearance = 1,

    /// <summary>Housing account clear — checked live against the sales ledger and instalment sales.</summary>
    HousingAccountClear = 2,

    /// <summary>Identity of transferor and transferee verified.</summary>
    Identity = 3,

    /// <summary>Spousal consent or a court order.</summary>
    SpousalConsent = 4,

    /// <summary>Original agreement or title produced, or a sworn loss affidavit with police report.</summary>
    OriginalDocuments = 5,

    /// <summary>Deceased-estate documents: death certificate, executor's appointment, distribution account.</summary>
    EstateDocuments = 6,

    /// <summary>Transfer and administration fees receipted — the receipt is checked on the sales ledger.</summary>
    FeesReceipted = 7,

    /// <summary>Capital gains / transfer-duty withholding computed and remitted.</summary>
    TaxWithholding = 8,
}

/// <summary>Where a transfer application stands.</summary>
public enum TransferStatus
{
    /// <summary>Lodged; the checklist is being verified.</summary>
    Lodged = 1,

    /// <summary>Checklist complete and submitted for approval.</summary>
    Submitted = 2,

    /// <summary>Approved by two officers.</summary>
    Approved = 3,

    /// <summary>Registers updated: the transfer is done.</summary>
    Completed = 4,

    /// <summary>Rejected.</summary>
    Rejected = 5,
}

/// <summary>
/// An application to change a stand's ownership (<c>hse.transfer_application</c>, brief §4.10.6): lodged with its type,
/// verified item by item against the type's checklist, submitted, approved by two officers other than those who verified
/// it, and completed by updating the registers in one unit of work. One open application per stand, in the database.
/// </summary>
public sealed class TransferApplication : AuditableEntity, ICompanyScoped
{
    private readonly List<TransferCheck> _checks = [];
    private readonly List<TransferApproval> _approvals = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private TransferApplication()
    {
        ApplicationNumber = string.Empty;
    }

    /// <summary>Lodges an application.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="applicationNumber">The application number.</param>
    /// <param name="standId">The stand.</param>
    /// <param name="transferType">The kind of transfer.</param>
    /// <param name="fromCustomerId">The current holder.</param>
    /// <param name="toCustomerId">The incoming holder.</param>
    /// <param name="consideration">The consideration.</param>
    /// <param name="lodgedOn">When.</param>
    public TransferApplication(long companyId, string applicationNumber, long standId, TransferType transferType, long fromCustomerId, long toCustomerId, decimal consideration, DateOnly lodgedOn)
    {
        CompanyId = companyId;
        ApplicationNumber = applicationNumber;
        StandId = standId;
        TransferType = transferType;
        FromCustomerId = fromCustomerId;
        ToCustomerId = toCustomerId;
        Consideration = consideration;
        LodgedOn = lodgedOn;
        Status = TransferStatus.Lodged;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The application number.</summary>
    public string ApplicationNumber { get; private set; }

    /// <summary>The stand.</summary>
    public long StandId { get; private set; }

    /// <summary>The kind of transfer.</summary>
    public TransferType TransferType { get; private set; }

    /// <summary>The current holder.</summary>
    public long FromCustomerId { get; private set; }

    /// <summary>The incoming holder.</summary>
    public long ToCustomerId { get; private set; }

    /// <summary>The consideration.</summary>
    public decimal Consideration { get; private set; }

    /// <summary>When it was lodged.</summary>
    public DateOnly LodgedOn { get; private set; }

    /// <summary>Where it stands.</summary>
    public TransferStatus Status { get; private set; }

    /// <summary>Who submitted it.</summary>
    public long? SubmittedByUserId { get; private set; }

    /// <summary>When it was decided (completed or rejected).</summary>
    public DateOnly? DecidedOn { get; private set; }

    /// <summary>Why it was rejected.</summary>
    public string? RejectionReason { get; private set; }

    /// <summary>The municipal clearance certificate relied on, if the stand is on the municipal register.</summary>
    public string? ClearanceNumber { get; private set; }

    /// <summary>The verified checklist items.</summary>
    public IReadOnlyList<TransferCheck> Checks => _checks.AsReadOnly();

    /// <summary>The approvals.</summary>
    public IReadOnlyList<TransferApproval> Approvals => _approvals.AsReadOnly();

    /// <summary>Records a verified item.</summary>
    /// <param name="check">The item.</param>
    public void AddCheck(TransferCheck check) => _checks.Add(check);

    /// <summary>Submits the verified application.</summary>
    /// <param name="userId">Who.</param>
    /// <param name="clearanceNumber">The clearance certificate relied on, if any.</param>
    public void Submit(long userId, string? clearanceNumber)
    {
        Status = TransferStatus.Submitted;
        SubmittedByUserId = userId;
        ClearanceNumber = clearanceNumber;
    }

    /// <summary>Records an approval; the second makes it approved.</summary>
    /// <param name="approval">The approval.</param>
    public void Approve(TransferApproval approval)
    {
        _approvals.Add(approval);
        if (_approvals.Count >= 2)
        {
            Status = TransferStatus.Approved;
        }
    }

    /// <summary>Approves it through the approval workflow, whose stages take the place of the two approvers.</summary>
    /// <param name="approval">The workflow's final approval.</param>
    public void ApproveThroughWorkflow(TransferApproval approval)
    {
        _approvals.Add(approval);
        Status = TransferStatus.Approved;
    }

    /// <summary>Records it completed.</summary>
    /// <param name="date">When.</param>
    public void Complete(DateOnly date)
    {
        Status = TransferStatus.Completed;
        DecidedOn = date;
    }

    /// <summary>Rejects it.</summary>
    /// <param name="date">When.</param>
    /// <param name="reason">Why.</param>
    public void Reject(DateOnly date, string reason)
    {
        Status = TransferStatus.Rejected;
        DecidedOn = date;
        RejectionReason = reason;
    }
}

/// <summary>A checklist item verified by an officer against a document (<c>hse.transfer_check</c>).</summary>
public sealed class TransferCheck : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private TransferCheck()
    {
        DocumentReference = string.Empty;
    }

    /// <summary>Records a verified item.</summary>
    /// <param name="requirement">The item.</param>
    /// <param name="documentReference">The supporting document.</param>
    /// <param name="verifiedByUserId">The verifying officer.</param>
    /// <param name="verifiedOn">When.</param>
    public TransferCheck(TransferRequirement requirement, string documentReference, long verifiedByUserId, DateOnly verifiedOn)
    {
        Requirement = requirement;
        DocumentReference = documentReference;
        VerifiedByUserId = verifiedByUserId;
        VerifiedOn = verifiedOn;
    }

    /// <summary>The application.</summary>
    public long TransferApplicationId { get; private set; }

    /// <summary>The item.</summary>
    public TransferRequirement Requirement { get; private set; }

    /// <summary>The supporting document.</summary>
    public string DocumentReference { get; private set; }

    /// <summary>The verifying officer.</summary>
    public long VerifiedByUserId { get; private set; }

    /// <summary>When.</summary>
    public DateOnly VerifiedOn { get; private set; }
}

/// <summary>An approval of a transfer (<c>hse.transfer_approval</c>).</summary>
public sealed class TransferApproval : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private TransferApproval()
    {
    }

    /// <summary>Records an approval.</summary>
    /// <param name="userId">The approver.</param>
    /// <param name="approvedOn">When.</param>
    public TransferApproval(long userId, DateOnly approvedOn)
    {
        UserId = userId;
        ApprovedOn = approvedOn;
    }

    /// <summary>The application.</summary>
    public long TransferApplicationId { get; private set; }

    /// <summary>The approver.</summary>
    public long UserId { get; private set; }

    /// <summary>When.</summary>
    public DateOnly ApprovedOn { get; private set; }
}
