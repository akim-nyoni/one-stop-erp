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
}
