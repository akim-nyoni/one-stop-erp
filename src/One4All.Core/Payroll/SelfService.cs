using One4All.Core.Common;

namespace One4All.Core.Payroll;

/// <summary>Where an employee's self-service request stands.</summary>
public enum SelfServiceStatus
{
    /// <summary>Waiting for HR.</summary>
    Pending = 0,

    /// <summary>Approved and applied.</summary>
    Approved = 1,

    /// <summary>Rejected.</summary>
    Rejected = 2,
}

/// <summary>A leave application made through employee self-service (<c>hr.leave_application</c>), taken only once HR approves it.</summary>
public sealed class LeaveApplication : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private LeaveApplication() => LeaveType = string.Empty;

    /// <summary>Creates the application.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="employeeId">The employee.</param>
    /// <param name="leaveType">The leave type.</param>
    /// <param name="fromDate">The first day.</param>
    /// <param name="toDate">The last day.</param>
    /// <param name="days">The working days.</param>
    public LeaveApplication(long companyId, long employeeId, string leaveType, DateOnly fromDate, DateOnly toDate, decimal days)
    {
        CompanyId = companyId;
        EmployeeId = employeeId;
        LeaveType = leaveType;
        FromDate = fromDate;
        ToDate = toDate;
        Days = days;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The employee.</summary>
    public long EmployeeId { get; private set; }

    /// <summary>The leave type.</summary>
    public string LeaveType { get; private set; }

    /// <summary>The first day.</summary>
    public DateOnly FromDate { get; private set; }

    /// <summary>The last day.</summary>
    public DateOnly ToDate { get; private set; }

    /// <summary>The working days.</summary>
    public decimal Days { get; private set; }

    /// <summary>The status.</summary>
    public SelfServiceStatus Status { get; private set; }

    /// <summary>Who decided.</summary>
    public long? DecidedByUserId { get; private set; }

    /// <summary>The decision note.</summary>
    public string? Note { get; private set; }

    /// <summary>The leave record created on approval.</summary>
    public long? LeaveRecordId { get; private set; }

    /// <summary>Records the decision.</summary>
    /// <param name="approved">Whether approved.</param>
    /// <param name="userId">Who decided.</param>
    /// <param name="note">The note.</param>
    /// <param name="leaveRecordId">The leave record, when approved.</param>
    public void Decide(bool approved, long userId, string? note, long? leaveRecordId)
    {
        Status = approved ? SelfServiceStatus.Approved : SelfServiceStatus.Rejected;
        DecidedByUserId = userId;
        Note = note;
        LeaveRecordId = leaveRecordId;
    }
}

/// <summary>
/// A change to an employee's details waiting for a second person (<c>hr.detail_change_request</c>): bank or wallet details asked for through
/// self-service or by HR, a net-pay split, and - when the company requires payroll approval - a salary or payment-method change. It is
/// applied only when someone other than the requester approves it, so neither pay nor its destination is changed by one person alone.
/// </summary>
public sealed class DetailChangeRequest : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private DetailChangeRequest()
    {
        Field = string.Empty;
        NewValue = string.Empty;
    }

    /// <summary>Creates the request.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="employeeId">The employee.</param>
    /// <param name="field">The detail.</param>
    /// <param name="oldValue">Its current value.</param>
    /// <param name="newValue">The value asked for.</param>
    public DetailChangeRequest(long companyId, long employeeId, string field, string? oldValue, string newValue)
    {
        CompanyId = companyId;
        EmployeeId = employeeId;
        Field = field;
        OldValue = oldValue;
        NewValue = newValue;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The employee.</summary>
    public long EmployeeId { get; private set; }

    /// <summary>The detail.</summary>
    public string Field { get; private set; }

    /// <summary>Its value when asked.</summary>
    public string? OldValue { get; private set; }

    /// <summary>The value asked for.</summary>
    public string NewValue { get; private set; }

    /// <summary>The status.</summary>
    public SelfServiceStatus Status { get; private set; }

    /// <summary>Who decided.</summary>
    public long? DecidedByUserId { get; private set; }

    /// <summary>The decision note.</summary>
    public string? Note { get; private set; }

    /// <summary>Who asked for the change (the employee through self-service, or an HR officer); they cannot approve it.</summary>
    public long? RequestedByUserId { get; set; }

    /// <summary>
    /// The date a salary or payment-method change takes effect (null for bank details, which apply when approved). With the requester
    /// (<see cref="RequestedByUserId"/>, <see cref="AuditableEntity.CreatedOnUtc"/>), the old and new values and the approver, the
    /// decided requests are the employee's dated salary history.
    /// </summary>
    public DateOnly? EffectiveFrom { get; set; }

    /// <summary>
    /// The day an approved salary or payment-method change was written onto the employee; null while it waits for approval or for its
    /// <see cref="EffectiveFrom"/> date (the payroll run applies it then), and for bank details, which apply when approved.
    /// </summary>
    public DateOnly? AppliedOn { get; private set; }

    /// <summary>Records the decision.</summary>
    /// <param name="approved">Whether approved.</param>
    /// <param name="userId">Who decided.</param>
    /// <param name="note">The note.</param>
    public void Decide(bool approved, long userId, string? note)
    {
        Status = approved ? SelfServiceStatus.Approved : SelfServiceStatus.Rejected;
        DecidedByUserId = userId;
        Note = note;
    }

    /// <summary>
    /// Records a change the company lets one person make (its payroll does not need a second approver): approved by whoever asked, kept so the
    /// salary history stays complete.
    /// </summary>
    /// <param name="note">Why no second person decided.</param>
    public void ApproveWithoutSecondPerson(string note)
    {
        Status = SelfServiceStatus.Approved;
        DecidedByUserId = RequestedByUserId;
        Note = note;
    }

    /// <summary>Records that the approved change has been written onto the employee.</summary>
    /// <param name="on">The day it was applied.</param>
    public void MarkApplied(DateOnly on) => AppliedOn = on;
}
