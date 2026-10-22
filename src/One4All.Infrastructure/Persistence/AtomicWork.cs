using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
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
    /// and puts the change tracker back as it was before the work (see <see cref="RestoreTracker"/>), so nothing the
    /// work did can be saved afterwards, while the caller's own entities stay tracked and usable.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="db">The database context.</param>
    /// <param name="work">The unit of work.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The work's result.</returns>
    public static Task<Result<T>> AtomicAsync<T>(this One4AllDbContext db, Func<Task<Result<T>>> work, CancellationToken ct)
        => RunAsync(db, work, r => r.IsSuccess, ct);

    /// <summary>As <see cref="AtomicAsync{T}"/>, for work that returns a plain <see cref="Result"/>.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="work">The unit of work.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The work's result.</returns>
    public static Task<Result> AtomicAsync(this One4AllDbContext db, Func<Task<Result>> work, CancellationToken ct)
        => RunAsync(db, work, r => r.IsSuccess, ct);

    /// <summary>
    /// Runs <paramref name="work"/> once the unit of work in progress has committed: for what must never happen for work that is rolled
    /// back or attempted again after a lost race, such as asking the fiscal device to sign a document. Outside any transaction it runs
    /// now. Work registered inside a transaction some other caller opened (not one of these units) does not run; what it was for must be
    /// picked up again later (a document left queued for the device is signed by the retry task).
    /// </summary>
    /// <param name="db">The database context.</param>
    /// <param name="work">The work; it saves what it changes itself, and should not throw (a failure is swallowed: the unit has committed).</param>
    /// <returns>A task that completes when the work has run or been registered.</returns>
    public static async Task AfterCommitAsync(this One4AllDbContext db, Func<Task> work)
    {
        if (db.Database.CurrentTransaction is IDbContextTransaction open)
        {
            db.AfterCommit.Add((open, work));
            return;
        }

        await work().ConfigureAwait(false);
    }

    // The work waiting for this transaction, in the order registered. The unit has committed: a failure here must not make the caller
    // think it did not, so it is swallowed and whatever the work was for stays as the unit left it (a document stays queued).
    private static async Task RunAfterCommitAsync(One4AllDbContext db, IDbContextTransaction committed)
    {
        List<Func<Task>> waiting = [.. db.AfterCommit.Where(w => ReferenceEquals(w.Transaction, committed)).Select(w => w.Work)];
        db.AfterCommit.Clear();
        foreach (Func<Task> work in waiting)
        {
#pragma warning disable CA1031 // The unit has committed; what follows it cannot undo it, and must not report it as failed.
            try
            {
                await work().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Left for the retry that picks up what this work was for.
            }
#pragma warning restore CA1031
        }
    }

    /// <summary>How many times a unit of work that lost a concurrency race is attempted in all.</summary>
    internal const int MaxAttempts = 8;

    private static async Task<TResult> RunAsync<TResult>(One4AllDbContext db, Func<Task<TResult>> work, Func<TResult, bool> succeeded, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            return await work().ConfigureAwait(false);
        }

        for (int attempt = 1; ; attempt++)
        {
            // Work registered to follow an attempt that did not commit (or a transaction of some other caller) never runs.
            db.AfterCommit.Clear();
            List<TrackedState> before = Snapshot(db);
            await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            try
            {
                TResult result = await work().ConfigureAwait(false);
                if (succeeded(result))
                {
                    await transaction.CommitAsync(ct).ConfigureAwait(false);
                    await RunAfterCommitAsync(db, transaction).ConfigureAwait(false);
                    return result;
                }

                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                RestoreTracker(db, before);
                return result;
            }
            catch (DbUpdateException ex) when (attempt < MaxAttempts && LostRace(ex))
            {
                // Another user changed a row this work updates (typically the journal number sequence or a period
                // balance, when two postings race). Everything is rolled back and put back; the work runs again and
                // re-reads those rows.
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                RestoreTracker(db, before);
                // A short, growing, jittered pause so twenty tills contending for one stock balance do not all retry in step.
                await Task.Delay(Random.Shared.Next(5, 25 * attempt), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Npgsql.PostgresException ex) when (attempt < MaxAttempts && ex.SqlState is Npgsql.PostgresErrorCodes.DeadlockDetected or Npgsql.PostgresErrorCodes.SerializationFailure)
            {
                // Two units of work took the same rows under lock in opposite orders: PostgreSQL ended one of them; it runs again after a pause.
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                RestoreTracker(db, before);
                await Task.Delay(Random.Shared.Next(5, 25 * attempt), CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                RestoreTracker(db, before);
                throw;
            }
        }
    }

    /// <summary>Constraints two concurrent postings can both try to satisfy with the same new row.</summary>
    private static readonly HashSet<string> RaceConstraints = new(StringComparer.Ordinal)
    {
        "ix_journal_company_id_journal_number",                          // both took the same next journal number
        "ix_period_balance_company_id_fiscal_period_id_account_code_cos", // both created the same period balance
        "ix_number_sequence_company_id_branch_id_document_type",         // both created the company's sequence
        "pk_number_sequence_year",                                       // both opened the same year's counter
        "ix_stock_balance_company_id_item_id_warehouse_id",              // both created the same stock balance
        "ix_journal_company_id_idempotency_key",                         // the same request posted twice at once: the retry finds the first
    };

    /// <summary>
    /// Whether a failed save lost a race with another posting rather than broke a rule: an optimistic-concurrency
    /// conflict (the row's <c>xmin</c> moved on), or a unique violation on one of the counter / balance constraints that
    /// concurrent postings both try to satisfy.
    /// </summary>
    private static bool LostRace(DbUpdateException ex)
        => ex is DbUpdateConcurrencyException
           || (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation } pg && pg.ConstraintName is { } name && RaceConstraints.Contains(name));

    private static List<TrackedState> Snapshot(One4AllDbContext db)
    {
        db.ChangeTracker.DetectChanges();
        return [.. db.ChangeTracker.Entries().Select(e => new TrackedState(e, e.State, e.CurrentValues.Clone(), e.OriginalValues.Clone()))];
    }

    /// <summary>
    /// Puts the change tracker back as it was before the failed work: every entity the work started tracking is removed
    /// from the collections of entities that were already tracked and then detached; every entity that was already
    /// tracked gets its values, original values (concurrency token included) and state back - the same instances, so
    /// collections and references that hold them stay valid and match the rolled-back database.
    /// </summary>
    private static void RestoreTracker(One4AllDbContext db, List<TrackedState> before)
    {
        db.ChangeTracker.DetectChanges();
        HashSet<object> known = new(before.Select(s => s.Entry.Entity), ReferenceEqualityComparer.Instance);
        List<EntityEntry> added = [.. db.ChangeTracker.Entries().Where(e => !known.Contains(e.Entity))];

        foreach (TrackedState was in before)
        {
            foreach (INavigationBase navigation in was.Entry.Metadata.GetNavigations().Cast<INavigationBase>().Where(n => n.IsCollection))
            {
                IClrCollectionAccessor? accessor = navigation.GetCollectionAccessor();
                if (accessor is null || navigation.GetGetter().GetClrValue(was.Entry.Entity) is not System.Collections.IEnumerable items)
                {
                    continue;
                }

                foreach (object item in items.Cast<object>().Where(i => !known.Contains(i)).ToList())
                {
                    accessor.Remove(was.Entry.Entity, item);
                }
            }
        }

        foreach (EntityEntry entry in added)
        {
            entry.State = EntityState.Detached;
        }

        // Owners before the entities they own: an owned entity cannot be tracked without its owner.
        foreach (TrackedState was in before.OrderBy(s => s.Entry.Metadata.IsOwned()))
        {
            EntityEntry entry = was.Entry;
            if (entry.State == EntityState.Detached && was.State != EntityState.Detached)
            {
                // The work stopped tracking it (e.g. to re-read a counter row) and loaded another copy, which has just been
                // detached - so this instance, with its earlier values, is tracked again.
                entry.State = was.State;
            }

            entry.CurrentValues.SetValues(was.Values);
            entry.OriginalValues.SetValues(was.OriginalValues);
            entry.State = was.State;
        }
    }

    private sealed record TrackedState(EntityEntry Entry, EntityState State, PropertyValues Values, PropertyValues OriginalValues);
}
