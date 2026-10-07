using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Integration;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using One4All.Licensing.Core;

namespace One4All.Infrastructure.Integration;

/// <summary>Idempotency keys kept in <c>sys.idempotency_record</c>, unique per user and key.</summary>
public sealed class IdempotencyStore : IIdempotencyStore
{
    private readonly One4AllDbContext _db;
    private readonly IClock _clock;

    /// <summary>Creates the store.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="clock">The clock.</param>
    public IdempotencyStore(One4AllDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<IdempotencyClaim> BeginAsync(long userId, string key, string method, string path, string requestHash, CancellationToken ct = default)
    {
        string trimmed = key.Trim();
        for (int attempt = 0; attempt < 2; attempt++)
        {
            IdempotencyRecord? existing = await _db.IdempotencyRecords.FirstOrDefaultAsync(r => r.UserId == userId && r.Key == trimmed, ct).ConfigureAwait(false);
            if (existing is not null && existing.StartedOnUtc < _clock.UtcNow - IIdempotencyStore.Retention)
            {
                _db.IdempotencyRecords.Remove(existing);
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);
                existing = null;
            }

            if (existing is not null)
            {
                return existing.RequestHash != requestHash ? new IdempotencyClaim(IdempotencyOutcome.Mismatch)
                    : existing.State == IdempotencyState.InProgress ? new IdempotencyClaim(IdempotencyOutcome.InProgress)
                    : new IdempotencyClaim(IdempotencyOutcome.Replay, existing.Id, existing.ResponseStatus ?? 200, existing.ResponseContentType, existing.ResponseBody ?? []);
            }

            IdempotencyRecord record = new(userId, trimmed, method, path, requestHash, _clock.UtcNow);
            _db.IdempotencyRecords.Add(record);
            try
            {
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);
                return new IdempotencyClaim(IdempotencyOutcome.Proceed, record.Id);
            }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
            {
                // Another request with the key got there first: look again.
                _db.Entry(record).State = EntityState.Detached;
            }
        }

        return new IdempotencyClaim(IdempotencyOutcome.InProgress);
    }

    /// <inheritdoc />
    public async Task CompleteAsync(long claimId, int status, string? contentType, byte[] body, CancellationToken ct = default)
    {
        if (await _db.IdempotencyRecords.FirstOrDefaultAsync(r => r.Id == claimId, ct).ConfigureAwait(false) is IdempotencyRecord record)
        {
            record.Complete(status, contentType, body, _clock.UtcNow);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public Task AbandonAsync(long claimId, CancellationToken ct = default)
        => _db.IdempotencyRecords.Where(r => r.Id == claimId).ExecuteDeleteAsync(ct);

    /// <inheritdoc />
    public Task<int> PurgeAsync(CancellationToken ct = default)
    {
        DateTimeOffset cutoff = _clock.UtcNow - IIdempotencyStore.Retention;
        return _db.IdempotencyRecords.Where(r => r.StartedOnUtc < cutoff).ExecuteDeleteAsync(ct);
    }
}

/// <summary>Built-in task: remove idempotency keys past their retention.</summary>
public sealed class IdempotencyPurgeTask : Application.Abstractions.Scheduling.IScheduledTask
{
    private readonly IIdempotencyStore _store;

    /// <summary>Creates the task.</summary>
    /// <param name="store">The store.</param>
    public IdempotencyPurgeTask(IIdempotencyStore store) => _store = store;

    /// <inheritdoc />
    public string Key => "system.idempotency-purge";

    /// <inheritdoc />
    public async Task<Core.Common.Result<string>> RunAsync(long companyId, DateTimeOffset runAt, CancellationToken ct = default)
        => $"{await _store.PurgeAsync(ct).ConfigureAwait(false)} expired idempotency keys removed.";
}
