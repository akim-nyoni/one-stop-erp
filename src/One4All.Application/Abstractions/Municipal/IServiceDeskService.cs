using One4All.Application.Municipal;
using One4All.Core.Common;
using One4All.Core.Municipal;

namespace One4All.Application.Abstractions.Municipal;

/// <summary>A fault being reported.</summary>
public sealed class FaultReport
{
    /// <summary>The council.</summary>
    public long CompanyId { get; init; }

    /// <summary>The fault category.</summary>
    public string CategoryCode { get; init; } = string.Empty;

    /// <summary>The property, when known (by stand number).</summary>
    public string? StandNumber { get; init; }

    /// <summary>The priority.</summary>
    public TicketPriority Priority { get; init; } = TicketPriority.Normal;

    /// <summary>Who reports it.</summary>
    public string ReporterName { get; init; } = string.Empty;

    /// <summary>How to reach them.</summary>
    public string ReporterContact { get; init; } = string.Empty;

    /// <summary>What is wrong, and where.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>When it was reported.</summary>
    public DateTimeOffset ReportedAt { get; init; }
}

/// <summary>What the public sees for a reference.</summary>
/// <param name="Reference">The reference.</param>
/// <param name="Category">The fault.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="ReportedAt">When it was reported.</param>
/// <param name="ResolutionDueAt">When resolution is due.</param>
/// <param name="ResolvedAt">When it was resolved.</param>
public sealed record PublicTicketStatus(string Reference, string Category, TicketStatus Status, DateTimeOffset ReportedAt, DateTimeOffset ResolutionDueAt, DateTimeOffset? ResolvedAt);

/// <summary>A ticket on the SLA board.</summary>
/// <param name="Reference">The reference.</param>
/// <param name="Category">The fault.</param>
/// <param name="Priority">The priority.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="Response">The response target's state.</param>
/// <param name="Resolution">The resolution target's state.</param>
public sealed record TicketSla(string Reference, string Category, TicketPriority Priority, TicketStatus Status, SlaState Response, SlaState Resolution);

/// <summary>The SLA board as at a moment.</summary>
/// <param name="Tickets">Every ticket reported by then, most urgent first.</param>
/// <param name="ResolutionBreaches">Tickets whose resolution target is breached.</param>
/// <param name="AverageHoursToResolve">Average hours from report to resolution of the resolved tickets.</param>
public sealed record SlaBoard(IReadOnlyList<TicketSla> Tickets, int ResolutionBreaches, decimal AverageHoursToResolve);

/// <summary>
/// The municipal service desk (brief §4.16 ticketing): fault categories with response and resolution targets; faults
/// logged against a property and service with a public reference; assignment (notifying the officer in-app), resolution,
/// closure and reopening, every step kept; the public status by reference; and the SLA board.
/// </summary>
public interface IServiceDeskService
{
    /// <summary>Defines a fault category.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="code">The code.</param>
    /// <param name="name">The name.</param>
    /// <param name="serviceType">The service it concerns.</param>
    /// <param name="responseHours">The response target.</param>
    /// <param name="resolutionHours">The resolution target.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The category id, or why it was refused.</returns>
    Task<Result<long>> DefineCategoryAsync(long companyId, string code, string name, ServiceType? serviceType, decimal responseHours, decimal resolutionHours, CancellationToken ct = default);

    /// <summary>Logs a fault.</summary>
    /// <param name="report">The fault.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The public reference, or why it was refused.</returns>
    Task<Result<string>> LogAsync(FaultReport report, CancellationToken ct = default);

    /// <summary>Assigns a ticket to an officer and notifies them.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="reference">The ticket.</param>
    /// <param name="userId">The officer.</param>
    /// <param name="at">When.</param>
    /// <param name="note">A note.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> AssignAsync(long companyId, string reference, long userId, DateTimeOffset at, string? note, CancellationToken ct = default);

    /// <summary>Records the fix.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="reference">The ticket.</param>
    /// <param name="resolution">How it was resolved.</param>
    /// <param name="at">When.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> ResolveAsync(long companyId, string reference, string resolution, DateTimeOffset at, CancellationToken ct = default);

    /// <summary>Closes a resolved ticket.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="reference">The ticket.</param>
    /// <param name="at">When.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> CloseAsync(long companyId, string reference, DateTimeOffset at, CancellationToken ct = default);

    /// <summary>Reopens a resolved ticket whose fault persists.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="reference">The ticket.</param>
    /// <param name="reason">Why.</param>
    /// <param name="at">When.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> ReopenAsync(long companyId, string reference, string reason, DateTimeOffset at, CancellationToken ct = default);

    /// <summary>What the public sees for a reference.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="reference">The reference.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The status, or why there is none.</returns>
    Task<Result<PublicTicketStatus>> PublicStatusAsync(long companyId, string reference, CancellationToken ct = default);

    /// <summary>The SLA board as at a moment.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="now">The moment.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The board.</returns>
    Task<SlaBoard> SlaBoardAsync(long companyId, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>
    /// Escalates every open ticket past its resolution deadline that has not been escalated: one priority step up, a note in its history and a
    /// message to its officer (the SLA escalation job runs this on a schedule).
    /// </summary>
    /// <param name="companyId">The council.</param>
    /// <param name="now">The moment.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The references escalated.</returns>
    Task<IReadOnlyList<string>> EscalateBreachesAsync(long companyId, DateTimeOffset now, CancellationToken ct = default);
}
