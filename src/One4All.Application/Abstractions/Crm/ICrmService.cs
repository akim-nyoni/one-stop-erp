using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Crm;

namespace One4All.Application.Abstractions.Crm;

/// <summary>A lead to capture.</summary>
/// <param name="CompanyId">The company.</param>
/// <param name="Name">The person.</param>
/// <param name="Organisation">Their organisation.</param>
/// <param name="Email">Their e-mail.</param>
/// <param name="Phone">Their phone.</param>
/// <param name="Source">Where it came from.</param>
/// <param name="OwnerUserId">Who works it.</param>
public sealed record NewLead(long CompanyId, string Name, string? Organisation, string? Email, string? Phone, string Source, long? OwnerUserId = null);

/// <summary>An activity to log.</summary>
/// <param name="CompanyId">The company.</param>
/// <param name="Type">The kind.</param>
/// <param name="Subject">What about.</param>
/// <param name="OwnerUserId">Whose it is.</param>
/// <param name="DueOnUtc">When it is due (tasks, calls to make).</param>
/// <param name="Notes">Notes.</param>
/// <param name="LeadId">The lead.</param>
/// <param name="OpportunityId">The opportunity.</param>
/// <param name="CustomerId">The customer.</param>
public sealed record NewActivity(long CompanyId, ActivityType Type, string Subject, long OwnerUserId, DateTimeOffset? DueOnUtc = null, string? Notes = null, long? LeadId = null,
    long? OpportunityId = null, long? CustomerId = null);

/// <summary>One line of a lead funnel.</summary>
/// <param name="Source">The source.</param>
/// <param name="Leads">Leads captured.</param>
/// <param name="Qualified">Qualified or converted.</param>
/// <param name="Converted">Converted to opportunities.</param>
/// <param name="Disqualified">Disqualified.</param>
/// <param name="ConversionPercent">Converted as a share of leads.</param>
public sealed record LeadFunnelLine(string Source, int Leads, int Qualified, int Converted, int Disqualified, decimal ConversionPercent);

/// <summary>Leads, activities and the quotation link (brief §4.16).</summary>
public interface ICrmService
{
    /// <summary>Captures a lead, flagging an earlier one with the same e-mail or phone as a possible duplicate.</summary>
    /// <param name="lead">The lead.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The lead id.</returns>
    Task<Result<long>> CaptureLeadAsync(NewLead lead, CancellationToken ct = default);

    /// <summary>Moves a lead to Contacted or Qualified.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="leadId">The lead.</param>
    /// <param name="status">The status.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    Task<Result> AdvanceLeadAsync(long companyId, long leadId, LeadStatus status, CancellationToken ct = default);

    /// <summary>Disqualifies a lead with a reason.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="leadId">The lead.</param>
    /// <param name="reason">Why.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    Task<Result> DisqualifyLeadAsync(long companyId, long leadId, string reason, CancellationToken ct = default);

    /// <summary>
    /// Converts a qualified lead into an opportunity (stage Qualified), optionally creating its customer first; the lead's activities
    /// follow to the opportunity.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="leadId">The lead.</param>
    /// <param name="opportunityName">The opportunity.</param>
    /// <param name="estimatedValue">Its estimated value.</param>
    /// <param name="customerCode">A customer code to create the customer under, or null for none yet.</param>
    /// <param name="currency">The customer's currency (when creating one).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The opportunity id.</returns>
    Task<Result<long>> ConvertLeadAsync(long companyId, long leadId, string opportunityName, decimal estimatedValue, string? customerCode, CurrencyCode? currency, CancellationToken ct = default);

    /// <summary>Logs an activity against a lead, opportunity or customer (at least one).</summary>
    /// <param name="activity">The activity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The activity id.</returns>
    Task<Result<long>> LogActivityAsync(NewActivity activity, CancellationToken ct = default);

    /// <summary>Marks an activity done with its outcome.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="activityId">The activity.</param>
    /// <param name="outcome">What came of it.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    Task<Result> CompleteActivityAsync(long companyId, long activityId, string? outcome, CancellationToken ct = default);

    /// <summary>A user's open activities due by a moment (overdue first).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="ownerUserId">The user.</param>
    /// <param name="dueBy">Due on or before.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The activities.</returns>
    Task<IReadOnlyList<CrmActivity>> AgendaAsync(long companyId, long ownerUserId, DateTimeOffset dueBy, CancellationToken ct = default);

    /// <summary>Everything logged against a lead, opportunity or customer, newest first.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="leadId">The lead.</param>
    /// <param name="opportunityId">The opportunity.</param>
    /// <param name="customerId">The customer.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The activities.</returns>
    Task<IReadOnlyList<CrmActivity>> TimelineAsync(long companyId, long? leadId, long? opportunityId, long? customerId, CancellationToken ct = default);

    /// <summary>Links a quotation to an opportunity (same customer), moving an earlier-stage opportunity to Proposal.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="opportunityId">The opportunity.</param>
    /// <param name="quotationId">The quotation.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    Task<Result> LinkQuotationAsync(long companyId, long opportunityId, long quotationId, CancellationToken ct = default);

    /// <summary>Leads by source: captured, qualified, converted, disqualified and the conversion rate.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The funnel, by source.</returns>
    Task<IReadOnlyList<LeadFunnelLine>> LeadFunnelAsync(long companyId, CancellationToken ct = default);
}
