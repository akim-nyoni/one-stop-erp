using One4All.Core.Common;

namespace One4All.Core.Trading;

/// <summary>Where a debt collection case stands.</summary>
public enum DebtCaseStage
{
    /// <summary>Handed over to the collector or attorney.</summary>
    HandedOver = 0,

    /// <summary>A summons has been issued.</summary>
    Summonsed = 1,

    /// <summary>Settled by the debtor.</summary>
    Settled = 2,

    /// <summary>Withdrawn without settlement.</summary>
    Withdrawn = 3,

    /// <summary>Written off as irrecoverable.</summary>
    WrittenOff = 4,
}

/// <summary>
/// A debt handed over for legal collection (<c>ar.debt_case</c>, brief §4.9 legal handover, summons register and write-off
/// approval): the debtor, collector, amount handed over, the summons once issued, and a write-off that one user requests
/// and a different user approves before anything is written off.
/// </summary>
public sealed class DebtCase : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private DebtCase()
    {
        CaseNumber = string.Empty;
        HandedOverTo = string.Empty;
    }

    /// <summary>Opens a case.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="caseNumber">The case number.</param>
    /// <param name="customerId">The debtor.</param>
    /// <param name="handedOverTo">The collector or attorney.</param>
    /// <param name="handedOverOn">When.</param>
    /// <param name="amount">The overdue balance handed over.</param>
    public DebtCase(long companyId, string caseNumber, long customerId, string handedOverTo, DateOnly handedOverOn, decimal amount)
    {
        CompanyId = companyId;
        CaseNumber = caseNumber;
        CustomerId = customerId;
        HandedOverTo = handedOverTo;
        HandedOverOn = handedOverOn;
        AmountHandedOver = amount;
        Stage = DebtCaseStage.HandedOver;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The case number.</summary>
    public string CaseNumber { get; private set; }

    /// <summary>The debtor.</summary>
    public long CustomerId { get; private set; }

    /// <summary>The collector or attorney.</summary>
    public string HandedOverTo { get; private set; }

    /// <summary>When it was handed over.</summary>
    public DateOnly HandedOverOn { get; private set; }

    /// <summary>The balance handed over.</summary>
    public decimal AmountHandedOver { get; private set; }

    /// <summary>Where it stands.</summary>
    public DebtCaseStage Stage { get; private set; }

    /// <summary>The summons number, once issued.</summary>
    public string? SummonsNumber { get; private set; }

    /// <summary>The court.</summary>
    public string? Court { get; private set; }

    /// <summary>When the summons was issued.</summary>
    public DateOnly? SummonsIssuedOn { get; private set; }

    /// <summary>The hearing date.</summary>
    public DateOnly? HearingDate { get; private set; }

    /// <summary>Who asked for the debt to be written off, while a write-off waits for approval.</summary>
    public long? WriteOffRequestedByUserId { get; private set; }

    /// <summary>Why it should be written off.</summary>
    public string? WriteOffReason { get; private set; }

    /// <summary>Who approved the write-off.</summary>
    public long? WriteOffApprovedByUserId { get; private set; }

    /// <summary>When the case was closed.</summary>
    public DateOnly? ClosedOn { get; private set; }

    /// <summary>Whether the case is still being pursued.</summary>
    public bool IsOpen => Stage is DebtCaseStage.HandedOver or DebtCaseStage.Summonsed;

    /// <summary>Records the summons.</summary>
    /// <param name="summonsNumber">The summons number.</param>
    /// <param name="court">The court.</param>
    /// <param name="issuedOn">When it was issued.</param>
    /// <param name="hearingDate">The hearing date.</param>
    public void RecordSummons(string summonsNumber, string court, DateOnly issuedOn, DateOnly? hearingDate)
    {
        SummonsNumber = summonsNumber;
        Court = court;
        SummonsIssuedOn = issuedOn;
        HearingDate = hearingDate;
        Stage = DebtCaseStage.Summonsed;
    }

    /// <summary>Requests a write-off.</summary>
    /// <param name="userId">Who asks.</param>
    /// <param name="reason">Why.</param>
    public void RequestWriteOff(long userId, string reason)
    {
        WriteOffRequestedByUserId = userId;
        WriteOffReason = reason;
    }

    /// <summary>The bad-debt account a requested write-off goes to (so its workflow can apply it).</summary>
    public string? WriteOffAccountCode { get; set; }

    /// <summary>Withdraws a write-off request (rejected in its workflow).</summary>
    public void ClearWriteOffRequest()
    {
        WriteOffRequestedByUserId = null;
        WriteOffReason = null;
        WriteOffAccountCode = null;
    }

    /// <summary>Records the approved write-off and closes the case.</summary>
    /// <param name="userId">The approver.</param>
    /// <param name="date">When.</param>
    public void ApproveWriteOff(long userId, DateOnly date)
    {
        WriteOffApprovedByUserId = userId;
        Stage = DebtCaseStage.WrittenOff;
        ClosedOn = date;
    }

    /// <summary>Closes the case as settled or withdrawn.</summary>
    /// <param name="stage">Settled or withdrawn.</param>
    /// <param name="date">When.</param>
    public void Close(DebtCaseStage stage, DateOnly date)
    {
        Stage = stage;
        ClosedOn = date;
    }
}
