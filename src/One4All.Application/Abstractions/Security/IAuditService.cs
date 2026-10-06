using One4All.Core.Audit;

namespace One4All.Application.Abstractions.Security;

/// <summary>A filter for the audit trail.</summary>
public sealed record AuditQuery
{
    /// <summary>The company.</summary>
    public long CompanyId { get; init; }

    /// <summary>Entries on or after this date (UTC), or null for no lower bound.</summary>
    public DateOnly? From { get; init; }

    /// <summary>Entries on or before this date (UTC), or null for no upper bound.</summary>
    public DateOnly? To { get; init; }

    /// <summary>Only this entity type (e.g. <c>Customer</c>), or null for all.</summary>
    public string? EntityType { get; init; }

    /// <summary>Only this entity id, or null for all.</summary>
    public string? EntityId { get; init; }

    /// <summary>Only this user name (case-insensitive), or null for all.</summary>
    public string? UserName { get; init; }

    /// <summary>Only this action, or null for all.</summary>
    public AuditAction? Action { get; init; }

    /// <summary>The most entries to return (newest first); capped at 5,000.</summary>
    public int Take { get; init; } = 500;
}

/// <summary>One audit-trail entry for display.</summary>
/// <param name="Id">The entry id (its position in the chain).</param>
/// <param name="OccurredOnUtc">When.</param>
/// <param name="UserName">Who ("system" when no user).</param>
/// <param name="Action">What.</param>
/// <param name="EntityType">The entity type.</param>
/// <param name="EntityId">The entity id.</param>
/// <param name="ChangedFields">The fields that changed (updates).</param>
/// <param name="OldValues">Prior values JSON.</param>
/// <param name="NewValues">New values JSON.</param>
/// <param name="Reason">The recorded reason, if any.</param>
/// <param name="Terminal">The terminal, if known.</param>
public sealed record AuditEntryView(long Id, DateTimeOffset OccurredOnUtc, string UserName, AuditAction Action, string EntityType, string? EntityId,
    IReadOnlyList<string> ChangedFields, string? OldValues, string? NewValues, string? Reason, string? Terminal);

/// <summary>The result of verifying a company's audit hash chain.</summary>
/// <param name="EntriesChecked">Entries walked.</param>
/// <param name="IsIntact">True when every entry recomputes from its predecessor's hash.</param>
/// <param name="FirstBrokenEntryId">The first entry whose hash does not recompute, or null.</param>
/// <param name="Message">A one-line summary.</param>
public sealed record AuditChainVerification(int EntriesChecked, bool IsIntact, long? FirstBrokenEntryId, string Message);

/// <summary>
/// The audit trail (brief §6): query the append-only change log, and verify its SHA-256 hash chain so any
/// tampering with a historical row — even by direct SQL — is detected and located.
/// </summary>
/// <summary>An event to record that is not a data change (a print, export, override, permission or licence change, sign-in).</summary>
/// <param name="CompanyId">The company, or null for system-wide events.</param>
/// <param name="UserId">Who.</param>
/// <param name="UserName">Who, by name.</param>
/// <param name="Module">The module.</param>
/// <param name="EntityType">What it concerns (e.g. Report, User, Licence).</param>
/// <param name="EntityId">Which one.</param>
/// <param name="Action">The action.</param>
/// <param name="Reason">Why, or what (mandatory for overrides).</param>
/// <param name="Details">Further details as JSON.</param>
/// <param name="Terminal">The machine or IP.</param>
public sealed record AuditEvent(long? CompanyId, long? UserId, string UserName, string Module, string EntityType, string? EntityId, AuditAction Action, string? Reason = null,
    string? Details = null, string? Terminal = null);

/// <summary>A change caught by the database triggers.</summary>
/// <param name="Id">The id.</param>
/// <param name="OccurredOnUtc">When.</param>
/// <param name="TableName">The table.</param>
/// <param name="Operation">INSERT, UPDATE or DELETE.</param>
/// <param name="RowId">The row.</param>
/// <param name="DbUser">The database user.</param>
/// <param name="ApplicationName">The connection's application name.</param>
/// <param name="ClientAddress">The client address.</param>
/// <param name="OutsideApplication">Whether it came from outside the application.</param>
public sealed record DatabaseChangeView(long Id, DateTimeOffset OccurredOnUtc, string TableName, string Operation, string? RowId, string DbUser, string? ApplicationName, string? ClientAddress,
    bool OutsideApplication);

public interface IAuditService
{
    /// <summary>
    /// Records an event that is not a data change in the hash-chained change log (brief §6 explicit calls): prints, exports, overrides,
    /// permission, configuration and licence changes, sign-ins. An override needs a reason (<c>Audit.ReasonRequired</c>).
    /// </summary>
    /// <param name="auditEvent">The event.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The entry id.</returns>
    Task<Core.Common.Result<long>> RecordAsync(AuditEvent auditEvent, CancellationToken ct = default);

    /// <summary>Row changes the database triggers caught on sensitive tables, newest first; optionally only those made outside the application.</summary>
    /// <param name="from">From.</param>
    /// <param name="to">To.</param>
    /// <param name="outsideApplicationOnly">Only direct changes.</param>
    /// <param name="take">At most this many.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The changes.</returns>
    Task<IReadOnlyList<DatabaseChangeView>> DatabaseChangesAsync(DateTimeOffset from, DateTimeOffset to, bool outsideApplicationOnly, int take = 500, CancellationToken ct = default);

    /// <summary>Queries the audit trail, newest first.</summary>
    /// <param name="query">The filter.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The matching entries.</returns>
    Task<IReadOnlyList<AuditEntryView>> QueryAsync(AuditQuery query, CancellationToken ct = default);

    /// <summary>
    /// Walks a company's chain in order, recomputing each entry's hash from the previous entry's stored hash
    /// (the first from the empty string), and reports the first entry that does not match.
    /// </summary>
    /// <param name="companyId">The company, or null for the system-wide chain.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The verification.</returns>
    Task<AuditChainVerification> VerifyChainAsync(long? companyId, CancellationToken ct = default);
}
