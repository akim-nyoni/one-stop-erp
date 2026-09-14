using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using One4All.Application.Abstractions.Security;
using One4All.Core.Audit;
using One4All.Core.Common;
using One4All.Licensing.Core;

namespace One4All.Infrastructure.Persistence.Auditing;

/// <summary>
/// EF Core interceptor that (a) stamps created/modified audit fields and (b) writes the append-only
/// <c>audit.change_log</c> with its per-company SHA-256 hash chain for every insert/update/delete
/// (brief §6). This is the technical change-audit layer; posting/transaction audit is written explicitly
/// by the posting engine. DB triggers act as a second net on the most sensitive tables.
/// </summary>
public sealed class AuditingInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly List<PendingAudit> _pending = [];
    private bool _writingAudit;

    /// <summary>Creates the interceptor.</summary>
    /// <param name="currentUser">The ambient current user (for stamping and audit attribution).</param>
    /// <param name="clock">The clock.</param>
    public AuditingInterceptor(ICurrentUser currentUser, IClock clock)
    {
        _currentUser = currentUser;
        _clock = clock;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null && !_writingAudit)
        {
            Collect(eventData.Context);
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <inheritdoc />
    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null && _pending.Count > 0 && !_writingAudit)
        {
            await WriteAuditAsync(eventData.Context, cancellationToken).ConfigureAwait(false);
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
    }

    private void Collect(DbContext context)
    {
        _pending.Clear();
        DateTimeOffset now = _clock.UtcNow;
        long? userId = _currentUser.UserId;

        foreach (EntityEntry entry in context.ChangeTracker.Entries())
        {
            if (entry.Entity is ChangeLogEntry || entry.Entity is not Entity)
            {
                continue;
            }

            // Stamp created/modified fields on auditable entities.
            if (entry.Entity is AuditableEntity auditable)
            {
                if (entry.State == EntityState.Added)
                {
                    auditable.CreatedOnUtc = now;
                    auditable.CreatedByUserId = userId ?? 0;
                }
                else if (entry.State == EntityState.Modified)
                {
                    auditable.ModifiedOnUtc = now;
                    auditable.ModifiedByUserId = userId;
                }
            }

            AuditAction? action = entry.State switch
            {
                EntityState.Added => AuditAction.Insert,
                EntityState.Modified => AuditAction.Update,
                EntityState.Deleted => AuditAction.Delete,
                _ => null,
            };

            if (action is null)
            {
                continue;
            }

            _pending.Add(new PendingAudit(entry, action.Value, Snapshot(entry, action.Value)));
        }
    }

    private async Task WriteAuditAsync(DbContext context, CancellationToken ct)
    {
        _writingAudit = true;
        try
        {
            DateTimeOffset now = _clock.UtcNow;
            Dictionary<long, string> lastHashByCompany = []; // key: companyId ?? -1 (the system chain)

            foreach (PendingAudit pending in _pending)
            {
                long? companyId = ExtractCompanyId(pending.Entry);
                long companyKey = companyId ?? -1;
                if (!lastHashByCompany.TryGetValue(companyKey, out string? previousHash))
                {
                    previousHash = await context.Set<ChangeLogEntry>()
                        .Where(c => c.CompanyId == companyId)
                        .OrderByDescending(c => c.Id)
                        .Select(c => c.RowHash)
                        .FirstOrDefaultAsync(ct)
                        .ConfigureAwait(false) ?? string.Empty;
                }

                string entityType = pending.Entry.Entity.GetType().Name;
                string? entityId = pending.Entry.Entity is Entity e ? e.Id.ToString() : null;

                ChangeLogEntry log = ChangeLogEntry.Create(
                    previousRowHash: previousHash,
                    occurredOnUtc: now,
                    companyId: companyId,
                    userId: _currentUser.UserId,
                    userName: _currentUser.UserName,
                    module: ModuleOf(entityType),
                    entityType: entityType,
                    entityId: entityId,
                    action: pending.Action,
                    oldValues: pending.Snapshot.OldValues,
                    newValues: pending.Snapshot.NewValues,
                    changedFields: pending.Snapshot.ChangedFields,
                    terminal: _currentUser.Terminal);

                lastHashByCompany[companyKey] = log.RowHash;
                context.Set<ChangeLogEntry>().Add(log);
            }

            _pending.Clear();
            await context.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _writingAudit = false;
        }
    }

    private static long? ExtractCompanyId(EntityEntry entry)
    {
        if (entry.Entity is ICompanyScoped scoped)
        {
            return scoped.CompanyId;
        }

        PropertyEntry? companyProp = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "CompanyId");
        return companyProp?.CurrentValue as long?;
    }

    private static string ModuleOf(string entityType) => entityType;

    private static AuditSnapshot Snapshot(EntityEntry entry, AuditAction action)
    {
        Dictionary<string, object?> oldValues = [];
        Dictionary<string, object?> newValues = [];
        List<string> changed = [];

        foreach (PropertyEntry property in entry.Properties)
        {
            string name = property.Metadata.Name;
            if (name is "RowVersion")
            {
                continue;
            }

            switch (action)
            {
                case AuditAction.Insert:
                    newValues[name] = property.CurrentValue;
                    break;

                case AuditAction.Delete:
                    oldValues[name] = property.OriginalValue;
                    break;

                case AuditAction.Update when property.IsModified &&
                                              !Equals(property.OriginalValue, property.CurrentValue):
                    oldValues[name] = property.OriginalValue;
                    newValues[name] = property.CurrentValue;
                    changed.Add(name);
                    break;
            }
        }

        return new AuditSnapshot(
            oldValues.Count > 0 ? JsonSerializer.Serialize(oldValues) : null,
            newValues.Count > 0 ? JsonSerializer.Serialize(newValues) : null,
            changed);
    }

    private readonly record struct PendingAudit(EntityEntry Entry, AuditAction Action, AuditSnapshot Snapshot);

    private readonly record struct AuditSnapshot(string? OldValues, string? NewValues, IReadOnlyList<string> ChangedFields);
}
