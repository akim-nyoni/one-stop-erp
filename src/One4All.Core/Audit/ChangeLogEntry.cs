using System.Security.Cryptography;
using System.Text;
using One4All.Core.Common;

namespace One4All.Core.Audit;

/// <summary>
/// One append-only entry in the change audit log (<c>audit.change_log</c>, brief §6B). Entries are
/// never updated or deleted. Each row stores <see cref="RowHash"/> =
/// SHA256(previous row hash ‖ canonical payload), forming a per-company hash chain so any tampering
/// with a historical row (even by direct SQL) breaks the chain and is detectable.
/// </summary>
public sealed class ChangeLogEntry : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private ChangeLogEntry()
    {
        UserName = string.Empty;
        Module = string.Empty;
        EntityType = string.Empty;
        RowHash = string.Empty;
    }

    private ChangeLogEntry(
        DateTimeOffset occurredOnUtc,
        long? companyId,
        long? userId,
        string userName,
        string module,
        string entityType,
        string? entityId,
        AuditAction action,
        string? oldValues,
        string? newValues,
        IReadOnlyList<string> changedFields,
        string? reason,
        string? terminal,
        string? ipAddress,
        string? sessionId,
        string? correlationId,
        string rowHash)
    {
        OccurredOnUtc = occurredOnUtc;
        CompanyId = companyId;
        UserId = userId;
        UserName = userName;
        Module = module;
        EntityType = entityType;
        EntityId = entityId;
        Action = action;
        OldValues = oldValues;
        NewValues = newValues;
        ChangedFields = changedFields;
        Reason = reason;
        Terminal = terminal;
        IpAddress = ipAddress;
        SessionId = sessionId;
        CorrelationId = correlationId;
        RowHash = rowHash;
    }

    /// <summary>When the action occurred (UTC).</summary>
    public DateTimeOffset OccurredOnUtc { get; private set; }

    /// <summary>The company context, or null for system-wide events.</summary>
    public long? CompanyId { get; private set; }

    /// <summary>The acting user id, or null for system actions.</summary>
    public long? UserId { get; private set; }

    /// <summary>The acting user name (denormalized so the log is self-contained).</summary>
    public string UserName { get; private set; }

    /// <summary>The terminal/workstation, if known.</summary>
    public string? Terminal { get; private set; }

    /// <summary>The source IP address, if known.</summary>
    public string? IpAddress { get; private set; }

    /// <summary>The session id, if known.</summary>
    public string? SessionId { get; private set; }

    /// <summary>The module the action belongs to.</summary>
    public string Module { get; private set; }

    /// <summary>The entity type affected (or a pseudo-type for non-entity events).</summary>
    public string EntityType { get; private set; }

    /// <summary>The affected entity's identity, if applicable.</summary>
    public string? EntityId { get; private set; }

    /// <summary>The action performed.</summary>
    public AuditAction Action { get; private set; }

    /// <summary>The prior values as JSON, if applicable.</summary>
    public string? OldValues { get; private set; }

    /// <summary>The new values as JSON, if applicable.</summary>
    public string? NewValues { get; private set; }

    /// <summary>The names of the fields that changed.</summary>
    public IReadOnlyList<string> ChangedFields { get; private set; } = [];

    /// <summary>An optional reason (mandatory for overrides, voids and some approvals).</summary>
    public string? Reason { get; private set; }

    /// <summary>Correlation id tying related entries together (e.g. one posting batch).</summary>
    public string? CorrelationId { get; private set; }

    /// <summary>The hash-chain value for this row.</summary>
    public string RowHash { get; private set; }

    /// <summary>
    /// Creates an entry and computes its <see cref="RowHash"/> from the previous entry's hash. Pass the
    /// empty string as <paramref name="previousRowHash"/> for the first entry in a company's chain.
    /// </summary>
    /// <param name="previousRowHash">The <see cref="RowHash"/> of the previous entry in the same company chain.</param>
    /// <param name="occurredOnUtc">When the action occurred.</param>
    /// <param name="companyId">Company context.</param>
    /// <param name="userId">Acting user id.</param>
    /// <param name="userName">Acting user name.</param>
    /// <param name="module">Owning module.</param>
    /// <param name="entityType">Affected entity type.</param>
    /// <param name="entityId">Affected entity id.</param>
    /// <param name="action">The action.</param>
    /// <param name="oldValues">Prior values JSON.</param>
    /// <param name="newValues">New values JSON.</param>
    /// <param name="changedFields">Changed field names.</param>
    /// <param name="reason">Optional reason.</param>
    /// <param name="terminal">Terminal.</param>
    /// <param name="ipAddress">Source IP.</param>
    /// <param name="sessionId">Session id.</param>
    /// <param name="correlationId">Correlation id.</param>
    /// <returns>The created, hash-stamped entry.</returns>
    public static ChangeLogEntry Create(
        string previousRowHash,
        DateTimeOffset occurredOnUtc,
        long? companyId,
        long? userId,
        string userName,
        string module,
        string entityType,
        string? entityId,
        AuditAction action,
        string? oldValues = null,
        string? newValues = null,
        IReadOnlyList<string>? changedFields = null,
        string? reason = null,
        string? terminal = null,
        string? ipAddress = null,
        string? sessionId = null,
        string? correlationId = null)
    {
        IReadOnlyList<string> fields = changedFields ?? [];

        // Normalize to microsecond precision so the hash is stable across a PostgreSQL timestamptz
        // round-trip (Postgres keeps microseconds; .NET keeps 100 ns ticks). Without this, verifying a
        // reloaded entry would recompute a different hash and falsely report tampering.
        DateTimeOffset normalizedOccurred = TruncateToMicroseconds(occurredOnUtc);

        string payload = CanonicalPayload(
            normalizedOccurred, companyId, userId, module, entityType, entityId, action,
            oldValues, newValues, fields, reason, correlationId);
        string rowHash = ComputeHash(previousRowHash, payload);

        return new ChangeLogEntry(
            normalizedOccurred, companyId, userId, userName, module, entityType, entityId, action,
            oldValues, newValues, fields, reason, terminal, ipAddress, sessionId, correlationId, rowHash);
    }

    /// <summary>
    /// Recomputes this entry's hash from a candidate previous hash and returns whether it matches the
    /// stored <see cref="RowHash"/> - the primitive used by the chain-verification routine.
    /// </summary>
    /// <param name="previousRowHash">The previous entry's stored hash.</param>
    /// <returns>True when the recomputed hash matches.</returns>
    public bool VerifyAgainst(string previousRowHash)
    {
        string payload = CanonicalPayload(
            OccurredOnUtc, CompanyId, UserId, Module, EntityType, EntityId, Action,
            OldValues, NewValues, ChangedFields, Reason, CorrelationId);
        return string.Equals(ComputeHash(previousRowHash, payload), RowHash, StringComparison.Ordinal);
    }

    private static string CanonicalPayload(
        DateTimeOffset occurredOnUtc,
        long? companyId,
        long? userId,
        string module,
        string entityType,
        string? entityId,
        AuditAction action,
        string? oldValues,
        string? newValues,
        IReadOnlyList<string> changedFields,
        string? reason,
        string? correlationId)
    {
        StringBuilder sb = new();
        sb.Append(occurredOnUtc.UtcDateTime.ToString("O")).Append('|')
          .Append(companyId).Append('|')
          .Append(userId).Append('|')
          .Append(module).Append('|')
          .Append(entityType).Append('|')
          .Append(entityId).Append('|')
          .Append((int)action).Append('|')
          .Append(oldValues).Append('|')
          .Append(newValues).Append('|')
          .Append(string.Join(',', changedFields)).Append('|')
          .Append(reason).Append('|')
          .Append(correlationId);
        return sb.ToString();
    }

    private static string ComputeHash(string previousRowHash, string payload)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(previousRowHash + "␟" + payload));
        return Convert.ToHexStringLower(bytes);
    }

    private static DateTimeOffset TruncateToMicroseconds(DateTimeOffset value)
    {
        const long ticksPerMicrosecond = 10; // 1 tick = 100 ns; 1 µs = 10 ticks
        long ticks = value.UtcTicks - (value.UtcTicks % ticksPerMicrosecond);
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }
}
