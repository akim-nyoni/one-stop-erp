using One4All.Core.Common;

namespace One4All.Core.Crm;

/// <summary>Where a lead stands.</summary>
public enum LeadStatus
{
    /// <summary>Just captured.</summary>
    New = 1,

    /// <summary>Spoken to.</summary>
    Contacted = 2,

    /// <summary>Worth pursuing.</summary>
    Qualified = 3,

    /// <summary>Not worth pursuing (with a reason).</summary>
    Disqualified = 4,

    /// <summary>Turned into an opportunity.</summary>
    Converted = 5,
}

/// <summary>A prospective customer not yet qualified (<c>crm.lead</c>, brief §4.16).</summary>
public sealed class Lead : AuditableEntity, ICompanyScoped
{
    private Lead()
    {
        Name = string.Empty;
        Source = string.Empty;
    }

    /// <summary>Captures the lead.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="name">The person.</param>
    /// <param name="organisation">Their organisation.</param>
    /// <param name="email">Their e-mail.</param>
    /// <param name="phone">Their phone.</param>
    /// <param name="source">Where it came from (web, referral, walk-in…).</param>
    public Lead(long companyId, string name, string? organisation, string? email, string? phone, string source)
    {
        CompanyId = companyId;
        Name = name;
        Organisation = organisation;
        Email = email;
        Phone = phone;
        Source = source;
        Status = LeadStatus.New;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets or sets the person.</summary>
    public string Name { get; set; }

    /// <summary>Gets or sets the organisation.</summary>
    public string? Organisation { get; set; }

    /// <summary>Gets or sets the e-mail.</summary>
    public string? Email { get; set; }

    /// <summary>Gets or sets the phone.</summary>
    public string? Phone { get; set; }

    /// <summary>Gets the source.</summary>
    public string Source { get; private set; }

    /// <summary>Gets or sets who works the lead.</summary>
    public long? OwnerUserId { get; set; }

    /// <summary>Gets the status.</summary>
    public LeadStatus Status { get; private set; }

    /// <summary>Gets why it was disqualified.</summary>
    public string? DisqualifiedReason { get; private set; }

    /// <summary>Gets an earlier lead it seems to repeat (same e-mail or phone).</summary>
    public long? PossibleDuplicateOfId { get; set; }

    /// <summary>Gets the opportunity it became.</summary>
    public long? OpportunityId { get; private set; }

    /// <summary>Moves it on (not once converted or disqualified).</summary>
    /// <param name="status">Contacted or Qualified.</param>
    /// <returns>Whether it moved.</returns>
    public bool Advance(LeadStatus status)
    {
        if (Status is LeadStatus.Converted or LeadStatus.Disqualified || status is not (LeadStatus.Contacted or LeadStatus.Qualified))
        {
            return false;
        }

        Status = status;
        return true;
    }

    /// <summary>Disqualifies it.</summary>
    /// <param name="reason">Why.</param>
    /// <returns>Whether it could be.</returns>
    public bool Disqualify(string reason)
    {
        if (Status == LeadStatus.Converted)
        {
            return false;
        }

        Status = LeadStatus.Disqualified;
        DisqualifiedReason = reason;
        return true;
    }

    /// <summary>Records the opportunity it became.</summary>
    /// <param name="opportunityId">The opportunity.</param>
    public void Convert(long opportunityId)
    {
        Status = LeadStatus.Converted;
        OpportunityId = opportunityId;
    }
}

/// <summary>The kind of activity.</summary>
public enum ActivityType
{
    /// <summary>A phone call.</summary>
    Call = 1,

    /// <summary>An e-mail.</summary>
    Email = 2,

    /// <summary>A meeting.</summary>
    Meeting = 3,

    /// <summary>A task to do.</summary>
    Task = 4,

    /// <summary>A note.</summary>
    Note = 5,
}

/// <summary>A call, e-mail, meeting, task or note against a lead, opportunity or customer (<c>crm.activity</c>).</summary>
public sealed class CrmActivity : AuditableEntity, ICompanyScoped
{
    private CrmActivity() => Subject = string.Empty;

    /// <summary>Records the activity.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="type">The kind.</param>
    /// <param name="subject">What about.</param>
    /// <param name="ownerUserId">Whose it is.</param>
    public CrmActivity(long companyId, ActivityType type, string subject, long ownerUserId)
    {
        CompanyId = companyId;
        Type = type;
        Subject = subject;
        OwnerUserId = ownerUserId;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the kind.</summary>
    public ActivityType Type { get; private set; }

    /// <summary>Gets what about.</summary>
    public string Subject { get; private set; }

    /// <summary>Gets or sets notes.</summary>
    public string? Notes { get; set; }

    /// <summary>Gets whose it is.</summary>
    public long OwnerUserId { get; private set; }

    /// <summary>Gets or sets when it is due.</summary>
    public DateTimeOffset? DueOnUtc { get; set; }

    /// <summary>Gets when it was done.</summary>
    public DateTimeOffset? DoneOnUtc { get; private set; }

    /// <summary>Gets or sets the lead.</summary>
    public long? LeadId { get; set; }

    /// <summary>Gets or sets the opportunity.</summary>
    public long? OpportunityId { get; set; }

    /// <summary>Gets or sets the customer.</summary>
    public long? CustomerId { get; set; }

    /// <summary>Marks it done, with an outcome added to the notes.</summary>
    /// <param name="at">When.</param>
    /// <param name="outcome">What came of it.</param>
    public void Complete(DateTimeOffset at, string? outcome)
    {
        DoneOnUtc = at;
        if (!string.IsNullOrWhiteSpace(outcome))
        {
            Notes = string.IsNullOrWhiteSpace(Notes) ? outcome.Trim() : $"{Notes}\n{outcome.Trim()}";
        }
    }
}
