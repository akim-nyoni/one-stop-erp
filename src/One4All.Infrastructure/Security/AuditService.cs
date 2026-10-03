using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Security;
using One4All.Core.Audit;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Security;

/// <summary>Reads and verifies the append-only, hash-chained change log.</summary>
public sealed class AuditService : IAuditService
{
    private const int MaxTake = 5_000;
    private const int VerifyBatch = 2_000;
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public AuditService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<IReadOnlyList<AuditEntryView>> QueryAsync(AuditQuery query, CancellationToken ct = default)
    {
        IQueryable<ChangeLogEntry> q = _db.ChangeLog.AsNoTracking().Where(c => c.CompanyId == query.CompanyId);

        if (query.From is { } from)
        {
            DateTimeOffset fromUtc = new(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            q = q.Where(c => c.OccurredOnUtc >= fromUtc);
        }

        if (query.To is { } to)
        {
            DateTimeOffset toUtc = new(to.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
            q = q.Where(c => c.OccurredOnUtc <= toUtc);
        }

        if (!string.IsNullOrWhiteSpace(query.EntityType))
        {
            string type = query.EntityType.Trim();
            q = q.Where(c => c.EntityType == type);
        }

        if (!string.IsNullOrWhiteSpace(query.EntityId))
        {
            string id = query.EntityId.Trim();
            q = q.Where(c => c.EntityId == id);
        }

        if (!string.IsNullOrWhiteSpace(query.UserName))
        {
            string user = query.UserName.Trim().ToLower();
            q = q.Where(c => c.UserName.ToLower() == user);
        }

        if (query.Action is { } action)
        {
            q = q.Where(c => c.Action == action);
        }

        int take = Math.Clamp(query.Take, 1, MaxTake);
        List<ChangeLogEntry> entries = await q.OrderByDescending(c => c.Id).Take(take).ToListAsync(ct).ConfigureAwait(false);

        return entries.Select(c => new AuditEntryView(
            c.Id, c.OccurredOnUtc, string.IsNullOrEmpty(c.UserName) ? "system" : c.UserName, c.Action, c.EntityType, c.EntityId,
            c.ChangedFields, c.OldValues, c.NewValues, c.Reason, c.Terminal)).ToList();
    }

    /// <inheritdoc />
    public async Task<AuditChainVerification> VerifyChainAsync(long? companyId, CancellationToken ct = default)
    {
        // Walk the chain in id order, in batches so a long log never loads at once.
        string previous = string.Empty;
        long lastId = 0;
        int checkedCount = 0;
        while (true)
        {
            List<ChangeLogEntry> batch = await _db.ChangeLog.AsNoTracking()
                .Where(c => c.CompanyId == companyId && c.Id > lastId)
                .OrderBy(c => c.Id)
                .Take(VerifyBatch)
                .ToListAsync(ct).ConfigureAwait(false);
            if (batch.Count == 0)
            {
                break;
            }

            foreach (ChangeLogEntry entry in batch)
            {
                checkedCount++;
                if (!entry.VerifyAgainst(previous))
                {
                    return new AuditChainVerification(checkedCount, false, entry.Id,
                        $"Audit chain broken at entry {entry.Id} ({entry.Action} {entry.EntityType} {entry.EntityId}, {entry.OccurredOnUtc:yyyy-MM-dd HH:mm:ss} UTC): the entry or its predecessor has been altered.");
                }

                previous = entry.RowHash;
                lastId = entry.Id;
            }
        }

        return new AuditChainVerification(checkedCount, true, null, $"Audit chain intact: {checkedCount} entr{(checkedCount == 1 ? "y" : "ies")} verified.");
    }
}
