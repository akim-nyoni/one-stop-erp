using One4All.Core.Common;

namespace One4All.Infrastructure.Persistence.RawSql;

/// <summary>
/// Applies raw-SQL scripts (views, functions, triggers, seed data) that live outside the EF Core
/// migration model, as required by the brief (§2.2, §9). Scripts are applied idempotently: each is
/// tracked by relative path and content hash, and re-applied only when its content changes, so a
/// <c>CREATE OR REPLACE</c>-style script can evolve safely across releases.
/// </summary>
public interface ISqlScriptRunner
{
    /// <summary>
    /// Applies every <c>*.sql</c> file found under <paramref name="rootDirectory"/> (recursively,
    /// ordered by relative path) that has not already been applied with the same content hash.
    /// </summary>
    /// <param name="rootDirectory">The directory containing SQL scripts (e.g. <c>db/sql</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of scripts applied, or an <see cref="Error"/> describing the first failure.</returns>
    Task<Result<int>> ApplyAsync(string rootDirectory, CancellationToken ct = default);
}
