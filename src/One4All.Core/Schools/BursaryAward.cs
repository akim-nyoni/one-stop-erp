using One4All.Core.Common;

namespace One4All.Core.Schools;

/// <summary>Where a bursary request stands.</summary>
public enum BursaryAwardStatus
{
    /// <summary>Awaiting approval; fees are not yet reduced.</summary>
    Pending = 0,

    /// <summary>Approved and applied to the student.</summary>
    Approved = 1,

    /// <summary>Turned down.</summary>
    Rejected = 2,
}

/// <summary>
/// A bursary or scholarship request (<c>sch.bursary_award</c>, brief §4.11): it reduces the student's fees only once someone other than the requester
/// with the approval right (or the approval workflow) approves it.
/// </summary>
public sealed class BursaryAward : AuditableEntity, ICompanyScoped
{
    private BursaryAward()
    {
        Reference = string.Empty;
        Reason = string.Empty;
    }

    /// <summary>Requests a bursary.</summary>
    public BursaryAward(long companyId, long studentId, string reference, BursaryType type, decimal value, string reason, long requestedByUserId, DateOnly requestedOn)
    {
        CompanyId = companyId;
        StudentId = studentId;
        Reference = reference;
        Type = type;
        Value = value;
        Reason = reason;
        RequestedByUserId = requestedByUserId;
        RequestedOn = requestedOn;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The student.</summary>
    public long StudentId { get; private set; }

    /// <summary>The request reference.</summary>
    public string Reference { get; private set; }

    /// <summary>Percentage or fixed value.</summary>
    public BursaryType Type { get; private set; }

    /// <summary>The percentage points or value.</summary>
    public decimal Value { get; private set; }

    /// <summary>Why it is asked for.</summary>
    public string Reason { get; private set; }

    /// <summary>Who asked.</summary>
    public long RequestedByUserId { get; private set; }

    /// <summary>When.</summary>
    public DateOnly RequestedOn { get; private set; }

    /// <summary>Where it stands.</summary>
    public BursaryAwardStatus Status { get; private set; }

    /// <summary>Who decided.</summary>
    public long? DecidedByUserId { get; private set; }

    /// <summary>When it was decided.</summary>
    public DateOnly? DecidedOn { get; private set; }

    /// <summary>The decision note.</summary>
    public string? DecisionNote { get; private set; }

    /// <summary>Records the decision.</summary>
    /// <param name="approved">Approved or rejected.</param>
    /// <param name="userId">Who decided.</param>
    /// <param name="on">When.</param>
    /// <param name="note">Why.</param>
    public void Decide(bool approved, long userId, DateOnly on, string? note)
    {
        Status = approved ? BursaryAwardStatus.Approved : BursaryAwardStatus.Rejected;
        DecidedByUserId = userId;
        DecidedOn = on;
        DecisionNote = note?.Trim();
    }
}
