using One4All.Application.Abstractions.Security;
using One4All.Core.Audit;

namespace One4All.Infrastructure.Security;

/// <summary>
/// Writes the business events the change log alone does not name (brief §6: "post, approve, reject, void… with who, when and where"): a posting,
/// a decision in a workflow, a void or reversal — chained like every other audit entry, with the user and terminal of the caller.
/// </summary>
internal static class SemanticAudit
{
    /// <summary>Records an event when the host has an audit service.</summary>
    /// <param name="audit">The audit service (null: none in this host).</param>
    /// <param name="caller">Who is acting (null: the system).</param>
    /// <param name="companyId">The company.</param>
    /// <param name="module">The module.</param>
    /// <param name="entityType">What was acted on.</param>
    /// <param name="entityId">Its key (document or journal number).</param>
    /// <param name="action">The event.</param>
    /// <param name="details">What happened.</param>
    /// <param name="reason">Why, when given.</param>
    /// <param name="userId">The user, when known better than the caller (e.g. an approver acting by emailed link).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task.</returns>
    public static async Task RecordAsync(IAuditService? audit, ICurrentUser? caller, long companyId, string module, string entityType, string? entityId, AuditAction action, string? details,
        string? reason = null, long? userId = null, CancellationToken ct = default)
    {
        if (audit is null)
        {
            return;
        }

        long? who = userId ?? caller?.UserId;
        string name = caller is { UserId: not null } && (userId is null || userId == caller.UserId) ? caller.UserName : who is long id ? $"user {id}" : "system";
        await audit.RecordAsync(new AuditEvent(companyId, who, name, module, entityType, entityId, action, reason, details, caller?.Terminal ?? caller?.ClientAddress), ct).ConfigureAwait(false);
    }
}
