using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Offline;
using One4All.Core.Offline;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Offline;

/// <summary>The database-backed offline outbox (<c>sync.offline_operation</c>).</summary>
public sealed class EfOfflineOutbox : IOfflineOutbox
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the outbox.</summary>
    /// <param name="db">The database context.</param>
    public EfOfflineOutbox(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<bool> EnqueueAsync(OfflineOperation operation, CancellationToken ct = default)
    {
        bool exists = await _db.OfflineOperations
            .AnyAsync(o => o.OperationId == operation.OperationId, ct)
            .ConfigureAwait(false);
        if (exists)
        {
            return false;
        }

        _db.OfflineOperations.Add(operation);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OfflineOperation>> GetPendingAsync(int maxCount, CancellationToken ct = default) =>
        await _db.OfflineOperations
            .Where(o => o.Status == OfflineOperationStatus.Pending)
            .OrderBy(o => o.CreatedUtc).ThenBy(o => o.Id)
            .Take(maxCount)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<OfflineOperation>> GetByStatusAsync(OfflineOperationStatus status, int maxCount, CancellationToken ct = default) =>
        await _db.OfflineOperations
            .Where(o => o.Status == status)
            .OrderBy(o => o.CreatedUtc).ThenBy(o => o.Id)
            .Take(maxCount)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<OfflineOperation?> FindAsync(Guid operationId, CancellationToken ct = default) =>
        await _db.OfflineOperations
            .FirstOrDefaultAsync(o => o.OperationId == operationId, ct)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
