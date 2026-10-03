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

    /// <summary>How many times a unit of work that lost a concurrency race is attempted in all.</summary>
    internal const int MaxAttempts = 3;

    private static async Task<TResult> RunAsync<TResult>(One4AllDbContext db, Func<Task<TResult>> work, Func<TResult, bool> succeeded, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            return await work().ConfigureAwait(false);
        }

        for (int attempt = 1; ; attempt++)
        {
            List<TrackedState> before = Snapshot(db);
            await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            try
            {
                TResult result = await work().ConfigureAwait(false);
                if (succeeded(result))
                {
                    await transaction.CommitAsync(ct).ConfigureAwait(false);
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
    /// tracked gets its values, original values (concurrency token included) and state back — the same instances, so
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
                // detached — so this instance, with its earlier values, is tracked again.
                entry.State = was.State;
            }

            entry.CurrentValues.SetValues(was.Values);
            entry.OriginalValues.SetValues(was.OriginalValues);
            entry.State = was.State;
        }
    }

    private sealed record TrackedState(EntityEntry Entry, EntityState State, PropertyValues Values, PropertyValues OriginalValues);
}
