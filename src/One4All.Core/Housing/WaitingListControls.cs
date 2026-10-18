using One4All.Core.Common;

namespace One4All.Core.Housing;

/// <summary>Where a dual-authorised change request stands.</summary>
public enum ChangeRequestStatus
{
    /// <summary>Requested, awaiting a second officer.</summary>
    Pending = 0,

    /// <summary>Approved and applied.</summary>
    Approved = 1,

    /// <summary>Rejected.</summary>
    Rejected = 2,
}

/// <summary>
/// A request to change an applicant's registration date (<c>hse.registration_date_change</c>, brief §4.10.3: the date
/// that determines seniority - any change is a dual-authorisation, fully audited event). Requested by one officer,
/// approved or rejected by another; the old and new dates and the reason are kept.
/// </summary>
public sealed class RegistrationDateChange : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private RegistrationDateChange()
    {
        Reason = string.Empty;
    }

    /// <summary>Requests a change.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="applicantId">The applicant.</param>
    /// <param name="oldDate">The current registration date.</param>
    /// <param name="newDate">The date asked for.</param>
    /// <param name="reason">Why.</param>
    /// <param name="requestedByUserId">Who asked.</param>
    public RegistrationDateChange(long companyId, long applicantId, DateOnly oldDate, DateOnly newDate, string reason, long requestedByUserId)
    {
        CompanyId = companyId;
        ApplicantId = applicantId;
        OldDate = oldDate;
        NewDate = newDate;
        Reason = reason;
        RequestedByUserId = requestedByUserId;
        Status = ChangeRequestStatus.Pending;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The applicant.</summary>
    public long ApplicantId { get; private set; }

    /// <summary>The registration date before the change.</summary>
    public DateOnly OldDate { get; private set; }

    /// <summary>The date asked for.</summary>
    public DateOnly NewDate { get; private set; }

    /// <summary>Why.</summary>
    public string Reason { get; private set; }

    /// <summary>Where it stands.</summary>
    public ChangeRequestStatus Status { get; private set; }

    /// <summary>Who asked.</summary>
    public long RequestedByUserId { get; private set; }

    /// <summary>Who decided.</summary>
    public long? DecidedByUserId { get; private set; }

    /// <summary>Records the second officer's decision.</summary>
    /// <param name="approved">Approved or rejected.</param>
    /// <param name="userId">The deciding officer.</param>
    public void Decide(bool approved, long userId)
    {
        Status = approved ? ChangeRequestStatus.Approved : ChangeRequestStatus.Rejected;
        DecidedByUserId = userId;
    }
}

/// <summary>The kind of notice sent to a waiting-list applicant.</summary>
public enum ApplicantNoticeType
{
    /// <summary>The applicant must confirm to stay on the list.</summary>
    RenewalDue = 1,

    /// <summary>Suspended for not confirming.</summary>
    Suspended = 2,

    /// <summary>Removed after the grace period.</summary>
    Removed = 3,
}

/// <summary>
/// A notice to a waiting-list applicant (<c>hse.applicant_notice</c>, brief §4.10.3: non-responders flagged, then
/// suspended, then removed - with notification history proving they were contacted).
/// </summary>
public sealed class ApplicantNotice : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private ApplicantNotice()
    {
    }

    /// <summary>Records a notice.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="applicantId">The applicant.</param>
    /// <param name="noticeType">The kind of notice.</param>
    /// <param name="sentOn">When it was sent.</param>
    public ApplicantNotice(long companyId, long applicantId, ApplicantNoticeType noticeType, DateOnly sentOn)
    {
        CompanyId = companyId;
        ApplicantId = applicantId;
        NoticeType = noticeType;
        SentOn = sentOn;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The applicant.</summary>
    public long ApplicantId { get; private set; }

    /// <summary>The kind of notice.</summary>
    public ApplicantNoticeType NoticeType { get; private set; }

    /// <summary>When it was sent.</summary>
    public DateOnly SentOn { get; private set; }

    /// <summary>Gets how it went out: SMS, or Letter (printed for the address on file) when there is no phone number.</summary>
    public string? Channel { get; init; }

    /// <summary>Gets where it went: the phone number or the address.</summary>
    public string? Recipient { get; init; }
}
