using Microsoft.EntityFrameworkCore;

namespace One4All.Infrastructure.Persistence.Auditing;

/// <summary>
/// Serialises writers of one company's audit hash chain (brief §6): a PostgreSQL transaction-scoped advisory lock per chain, taken before the
/// chain's last hash is read and held until the transaction ends, so two transactions saving at once never link to the same previous entry.
/// </summary>
public static class AuditChainLock
{
    private const long Base = 7_700_000_000_000L;

    /// <summary>Takes the locks for the chains about to be extended (in a fixed order, so writers never wait on each other in a cycle).</summary>
    /// <param name="context">The context (its connection must be in a transaction).</param>
    /// <param name="companyIds">The chains (null: the system chain).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task.</returns>
    public static async Task AcquireAsync(DbContext context, IEnumerable<long?> companyIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.Database.IsNpgsql())
        {
            return;
        }

        foreach (long key in companyIds.Select(id => Base + (id ?? -1L)).Distinct().Order())
        {
            await context.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({key})", ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The chain head this context already wrote in its current transaction (the chain's lock is still held, so nothing else can have extended it),
    /// or null when the chain has not been written in this transaction.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="companyId">The chain (null: the system chain).</param>
    /// <returns>The head, or null.</returns>
    public static string? HeldHead(DbContext context, long? companyId)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context is One4AllDbContext db && context.Database.CurrentTransaction is { } transaction && db.ChainHeads is { } held && ReferenceEquals(held.Transaction, transaction)
            && held.Heads.TryGetValue(companyId ?? -1L, out string? head) ? head : null;
    }

    /// <summary>Remembers a chain's new head for the rest of the context's current transaction (saving the lock and the read on the next write).</summary>
    /// <param name="context">The context.</param>
    /// <param name="companyId">The chain.</param>
    /// <param name="head">The hash just written.</param>
    public static void Advance(DbContext context, long? companyId, string head)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context is One4AllDbContext db && context.Database.CurrentTransaction is { } transaction)
        {
            if (db.ChainHeads is not { } held || !ReferenceEquals(held.Transaction, transaction))
            {
                db.ChainHeads = (transaction, new Dictionary<long, string>());
            }

            db.ChainHeads.Value.Heads[companyId ?? -1L] = head;
        }
    }
}
