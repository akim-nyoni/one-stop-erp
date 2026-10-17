using One4All.Core.Common;

namespace One4All.Core.Payroll;

/// <summary>Where a grievance stands.</summary>
public enum GrievanceStatus
{
    /// <summary>Lodged, not yet looked into.</summary>
    Open = 0,

    /// <summary>Being investigated.</summary>
    Investigating = 1,

    /// <summary>Resolved with an outcome.</summary>
    Resolved = 2,

    /// <summary>Withdrawn by the employee.</summary>
    Withdrawn = 3,
}

/// <summary>A grievance an employee lodges (<c>hr.grievance</c>; brief §4.15.6 HR records: grievances), followed to its outcome.</summary>
public sealed class EmployeeGrievance : AuditableEntity, ICompanyScoped
{
    private EmployeeGrievance()
    {
        Subject = string.Empty;
        Details = string.Empty;
    }

    /// <summary>Lodges a grievance.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="employeeId">The employee.</param>
    /// <param name="lodgedOn">When.</param>
    /// <param name="subject">What it is about.</param>
    /// <param name="details">The employee's account.</param>
    public EmployeeGrievance(long companyId, long employeeId, DateOnly lodgedOn, string subject, string details)
    {
        CompanyId = companyId;
        EmployeeId = employeeId;
        LodgedOn = lodgedOn;
        Subject = subject;
        Details = details;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>Gets the employee.</summary>
    public long EmployeeId { get; private set; }

    /// <summary>Gets when it was lodged.</summary>
    public DateOnly LodgedOn { get; private set; }

    /// <summary>Gets what it is about.</summary>
    public string Subject { get; private set; }

    /// <summary>Gets the employee's account.</summary>
    public string Details { get; private set; }

    /// <summary>Gets or sets who (or what) it is against, if anyone.</summary>
    public string? Against { get; set; }

    /// <summary>Gets where it stands.</summary>
    public GrievanceStatus Status { get; private set; }

    /// <summary>Gets the outcome, once resolved or withdrawn.</summary>
    public string? Outcome { get; private set; }

    /// <summary>Gets when it was closed.</summary>
    public DateOnly? ClosedOn { get; private set; }

    /// <summary>Moves it on: investigating, or closed (resolved / withdrawn) with the outcome.</summary>
    /// <param name="status">The new status.</param>
    /// <param name="on">When.</param>
    /// <param name="outcome">The outcome (when closing).</param>
    /// <returns>Whether the move was allowed.</returns>
    public bool Move(GrievanceStatus status, DateOnly on, string? outcome)
    {
        if (Status is GrievanceStatus.Resolved or GrievanceStatus.Withdrawn || status <= Status && status != GrievanceStatus.Withdrawn)
        {
            return false;
        }

        Status = status;
        if (status is GrievanceStatus.Resolved or GrievanceStatus.Withdrawn)
        {
            Outcome = outcome;
            ClosedOn = on;
        }

        return true;
    }
}

/// <summary>Company property issued to an employee (<c>hr.asset_issue</c>): laptop, phone, keys, uniform, tools - to be returned when they leave.</summary>
public sealed class EmployeeAssetIssue : AuditableEntity, ICompanyScoped
{
    private EmployeeAssetIssue() => Description = string.Empty;

    /// <summary>Issues an item.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="employeeId">The employee.</param>
    /// <param name="description">What was issued.</param>
    /// <param name="issuedOn">When.</param>
    public EmployeeAssetIssue(long companyId, long employeeId, string description, DateOnly issuedOn)
    {
        CompanyId = companyId;
        EmployeeId = employeeId;
        Description = description;
        IssuedOn = issuedOn;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>Gets the employee.</summary>
    public long EmployeeId { get; private set; }

    /// <summary>Gets what was issued.</summary>
    public string Description { get; private set; }

    /// <summary>Gets or sets its serial or asset number.</summary>
    public string? SerialNumber { get; set; }

    /// <summary>Gets or sets its value, for recovery if not returned.</summary>
    public decimal Value { get; set; }

    /// <summary>Gets when it was issued.</summary>
    public DateOnly IssuedOn { get; private set; }

    /// <summary>Gets when it came back, or null while out.</summary>
    public DateOnly? ReturnedOn { get; private set; }

    /// <summary>Gets the condition it came back in.</summary>
    public string? ReturnCondition { get; private set; }

    /// <summary>Records its return.</summary>
    /// <param name="on">When.</param>
    /// <param name="condition">Its condition.</param>
    public void Return(DateOnly on, string? condition)
    {
        ReturnedOn = on;
        ReturnCondition = condition;
    }
}

/// <summary>What a medical record is.</summary>
public enum MedicalRecordKind
{
    /// <summary>A fitness-for-work assessment.</summary>
    Fitness = 0,

    /// <summary>An illness.</summary>
    Illness = 1,

    /// <summary>An injury (on or off duty).</summary>
    Injury = 2,

    /// <summary>A disability and the accommodation made.</summary>
    Disability = 3,

    /// <summary>Anything else.</summary>
    Other = 4,
}

/// <summary>
/// A restricted medical record (<c>hr.medical_record</c>; brief §4.15.6): seen and kept only by users with the medical-records permission,
/// its note encrypted at rest.
/// </summary>
public sealed class EmployeeMedicalRecord : AuditableEntity, ICompanyScoped
{
    private EmployeeMedicalRecord() => Note = string.Empty;

    /// <summary>Records a medical note.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="employeeId">The employee.</param>
    /// <param name="date">The date.</param>
    /// <param name="kind">What it is.</param>
    /// <param name="note">The note (encrypted at rest).</param>
    public EmployeeMedicalRecord(long companyId, long employeeId, DateOnly date, MedicalRecordKind kind, string note)
    {
        CompanyId = companyId;
        EmployeeId = employeeId;
        Date = date;
        Kind = kind;
        Note = note;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>Gets the employee.</summary>
    public long EmployeeId { get; private set; }

    /// <summary>Gets the date.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>Gets what it is.</summary>
    public MedicalRecordKind Kind { get; private set; }

    /// <summary>Gets the note.</summary>
    public string Note { get; private set; }

    /// <summary>Gets or sets whether the employee is fit for their work (null: not assessed).</summary>
    public bool? FitForWork { get; set; }

    /// <summary>Gets or sets when it is to be reviewed.</summary>
    public DateOnly? ReviewOn { get; set; }
}
