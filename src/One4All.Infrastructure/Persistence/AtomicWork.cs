using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using One4All.Core.Common;

namespace One4All.Infrastructure.Persistence;

/// <summary>
/// Runs a multi-step unit of work (several postings, ledger entries, stock movements) so it stands or falls together.
/// Each posting step saves as it goes, so without this a failure part-way leaves the earlier steps committed and the
/// entities the failed step had already changed waiting in the change tracker for the next save to persist.
/// </summary>
internal static class AtomicWork
{
    /// <summary>
    /// Runs <paramref name="work"/> inside a transaction. When a caller already has one open the work simply joins it
    /// (the caller decides). Otherwise the work commits on success; on a failure result or an exception it rolls back
    /// and clears the change tracker, so nothing the work loaded or changed can be saved afterwards from this scope.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="db">The database context.</param>
    /// <param name="work">The unit of work.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The work's result.</returns>
    public static async Task<Result<T>> AtomicAsync<T>(this One4AllDbContext db, Func<Task<Result<T>>> work, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            return await work().ConfigureAwait(false);
        }

        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            Result<T> result = await work().ConfigureAwait(false);
            if (result.IsSuccess)
            {
                await transaction.CommitAsync(ct).ConfigureAwait(false);
                return result;
            }

            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            db.ChangeTracker.Clear();
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            db.ChangeTracker.Clear();
            throw;
        }
    }
}
