using One4All.Core.Common;

namespace One4All.Core.Municipal;

/// <summary>How urgent a fault is.</summary>
public enum TicketPriority
{
    /// <summary>Low.</summary>
    Low = 1,

    /// <summary>Normal.</summary>
    Normal = 2,

    /// <summary>High.</summary>
    High = 3,

    /// <summary>Critical (e.g. a burst main).</summary>
    Critical = 4,
}

/// <summary>Where a fault ticket stands.</summary>
public enum TicketStatus
{
    /// <summary>Logged, not yet assigned.</summary>
    Open = 1,

    /// <summary>Assigned to an officer or crew.</summary>
    Assigned = 2,

    /// <summary>Fixed; awaiting confirmation.</summary>
    Resolved = 3,

    /// <summary>Closed.</summary>
    Closed = 4,
}

/// <summary>
/// A fault category (<c>mun.ticket_category</c>, brief §4.16: burst pipe, no water, refuse not collected...): the service it
/// concerns and its response and resolution targets in hours.
/// </summary>
public sealed class TicketCategory : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private TicketCategory()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    /// <summary>Defines a category.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="code">The code.</param>
    /// <param name="name">The name.</param>
    /// <param name="serviceType">The service it concerns, if any.</param>
    /// <param name="responseHours">The response target.</param>
    /// <param name="resolutionHours">The resolution target.</param>
    public TicketCategory(long companyId, string code, string name, ServiceType? serviceType, decimal responseHours, decimal resolutionHours)
    {
        CompanyId = companyId;
        Code = code;
        Name = name;
        ServiceType = serviceType;
        ResponseHours = responseHours;
        ResolutionHours = resolutionHours;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The code.</summary>
    public string Code { get; private set; }

    /// <summary>The name.</summary>
    public string Name { get; private set; }

    /// <summary>The service it concerns.</summary>
    public ServiceType? ServiceType { get; private set; }

    /// <summary>The response target in hours.</summary>
    public decimal ResponseHours { get; private set; }

    /// <summary>The resolution target in hours.</summary>
    public decimal ResolutionHours { get; private set; }

    /// <summary>
    /// Changes the category's name, service and targets (its code stays; tickets already logged keep the due times they were given). The caller has
    /// checked the name is given and the response target is above zero and no longer than the resolution target.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="serviceType">The service it concerns, if any.</param>
    /// <param name="responseHours">The response target.</param>
    /// <param name="resolutionHours">The resolution target.</param>
    public void Change(string name, ServiceType? serviceType, decimal responseHours, decimal resolutionHours)
    {
        Name = name.Trim();
        ServiceType = serviceType;
        ResponseHours = responseHours;
        ResolutionHours = resolutionHours;
    }
}

/// <summary>
/// A fault report (<c>mun.service_ticket</c>, brief §4.16 ticketing): its public reference, the property and service it
/// concerns, the reporter, priority, SLA due times, assignment, resolution and closure, with every step in its history.
/// </summary>
public sealed class ServiceTicket : AuditableEntity, ICompanyScoped
{
    private readonly List<TicketEvent> _events = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private ServiceTicket()
    {
        Reference = string.Empty;
        ReporterName = string.Empty;
        ReporterContact = string.Empty;
        Description = string.Empty;
    }

    /// <summary>Logs a fault.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="reference">The public reference.</param>
    /// <param name="categoryId">The category.</param>
    /// <param name="propertyId">The property, if known.</param>
    /// <param name="priority">The priority.</param>
    /// <param name="reporterName">Who reported it.</param>
    /// <param name="reporterContact">How to reach them.</param>
    /// <param name="description">What is wrong, and where.</param>
    /// <param name="reportedAt">When.</param>
    /// <param name="responseDueAt">When a response is due.</param>
    /// <param name="resolutionDueAt">When resolution is due.</param>
    public ServiceTicket(long companyId, string reference, long categoryId, long? propertyId, TicketPriority priority, string reporterName, string reporterContact,
        string description, DateTimeOffset reportedAt, DateTimeOffset responseDueAt, DateTimeOffset resolutionDueAt)
    {
        CompanyId = companyId;
        Reference = reference;
        CategoryId = categoryId;
        PropertyId = propertyId;
        Priority = priority;
        ReporterName = reporterName;
        ReporterContact = reporterContact;
        Description = description;
        ReportedAt = reportedAt;
        ResponseDueAt = responseDueAt;
        ResolutionDueAt = resolutionDueAt;
        Status = TicketStatus.Open;
        _events.Add(new TicketEvent(TicketStatus.Open, reportedAt, null, "Logged"));
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The public reference.</summary>
    public string Reference { get; private set; }

    /// <summary>Gets or sets the council rental unit the fault is in (a housing fault).</summary>
    public long? RentalUnitId { get; set; }

    /// <summary>Gets or sets the repair raised on the rental unit for the fault.</summary>
    public string? RepairNumber { get; set; }

    /// <summary>The category.</summary>
    public long CategoryId { get; private set; }

    /// <summary>The property, if known.</summary>
    public long? PropertyId { get; private set; }

    /// <summary>The priority.</summary>
    public TicketPriority Priority { get; private set; }

    /// <summary>Who reported it.</summary>
    public string ReporterName { get; private set; }

    /// <summary>How to reach them.</summary>
    public string ReporterContact { get; private set; }

    /// <summary>What is wrong, and where.</summary>
    public string Description { get; private set; }

    /// <summary>Where it stands.</summary>
    public TicketStatus Status { get; private set; }

    /// <summary>When it was reported.</summary>
    public DateTimeOffset ReportedAt { get; private set; }

    /// <summary>When a response is due.</summary>
    public DateTimeOffset ResponseDueAt { get; private set; }

    /// <summary>When resolution is due.</summary>
    public DateTimeOffset ResolutionDueAt { get; private set; }

    /// <summary>The officer it is assigned to.</summary>
    public long? AssignedToUserId { get; private set; }

    /// <summary>When it was first responded to (first assigned).</summary>
    public DateTimeOffset? RespondedAt { get; private set; }

    /// <summary>When it was resolved.</summary>
    public DateTimeOffset? ResolvedAt { get; private set; }

    /// <summary>How it was resolved.</summary>
    public string? Resolution { get; private set; }

    /// <summary>When it was closed.</summary>
    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>The history, oldest first.</summary>
    public IReadOnlyList<TicketEvent> Events => _events.AsReadOnly();

    /// <summary>Assigns it (the first assignment is the response).</summary>
    /// <param name="userId">The officer.</param>
    /// <param name="at">When.</param>
    /// <param name="note">A note.</param>
    public void Assign(long userId, DateTimeOffset at, string? note)
    {
        AssignedToUserId = userId;
        RespondedAt ??= at;
        Status = TicketStatus.Assigned;
        _events.Add(new TicketEvent(TicketStatus.Assigned, at, userId, note ?? "Assigned"));
    }

    /// <summary>Records the fix.</summary>
    /// <param name="resolution">How it was resolved.</param>
    /// <param name="at">When.</param>
    public void Resolve(string resolution, DateTimeOffset at)
    {
        Resolution = resolution;
        ResolvedAt = at;
        Status = TicketStatus.Resolved;
        _events.Add(new TicketEvent(TicketStatus.Resolved, at, AssignedToUserId, resolution));
    }

    /// <summary>Closes it.</summary>
    /// <param name="at">When.</param>
    public void Close(DateTimeOffset at)
    {
        ClosedAt = at;
        Status = TicketStatus.Closed;
        _events.Add(new TicketEvent(TicketStatus.Closed, at, null, "Closed"));
    }

    /// <summary>Gets when the ticket was escalated for breaching its resolution SLA (null: not escalated).</summary>
    public DateTimeOffset? EscalatedAt { get; private set; }

    /// <summary>Escalates a ticket past its resolution deadline: one priority step up (Critical stays), noted in its history.</summary>
    /// <param name="at">When.</param>
    public void Escalate(DateTimeOffset at)
    {
        EscalatedAt = at;
        if (Priority < TicketPriority.Critical)
        {
            Priority = Priority + 1;
        }

        _events.Add(new TicketEvent(Status, at, null, $"Escalated: resolution overdue since {ResolutionDueAt:yyyy-MM-dd HH:mm}; priority now {Priority}"));
    }

    /// <summary>Reopens a resolved ticket: the fault persists; it goes back to its officer and the fix no longer counts.</summary>
    /// <param name="reason">Why.</param>
    /// <param name="at">When.</param>
    public void Reopen(string reason, DateTimeOffset at)
    {
        ResolvedAt = null;
        Resolution = null;
        Status = AssignedToUserId is null ? TicketStatus.Open : TicketStatus.Assigned;
        _events.Add(new TicketEvent(Status, at, AssignedToUserId, $"Reopened: {reason}"));
    }
}

/// <summary>A step in a ticket's history (<c>mun.ticket_event</c>).</summary>
public sealed class TicketEvent : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private TicketEvent()
    {
        Note = string.Empty;
    }

    /// <summary>Records a step.</summary>
    /// <param name="status">The status it led to.</param>
    /// <param name="at">When.</param>
    /// <param name="userId">The officer involved.</param>
    /// <param name="note">What happened.</param>
    public TicketEvent(TicketStatus status, DateTimeOffset at, long? userId, string note)
    {
        Status = status;
        At = at;
        UserId = userId;
        Note = note;
    }

    /// <summary>The ticket.</summary>
    public long ServiceTicketId { get; private set; }

    /// <summary>The status it led to.</summary>
    public TicketStatus Status { get; private set; }

    /// <summary>When.</summary>
    public DateTimeOffset At { get; private set; }

    /// <summary>The officer involved.</summary>
    public long? UserId { get; private set; }

    /// <summary>What happened.</summary>
    public string Note { get; private set; }
}
