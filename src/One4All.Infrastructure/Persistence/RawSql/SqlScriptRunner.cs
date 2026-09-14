using System.Security.Cryptography;
using System.Text;
using Npgsql;
using One4All.Core.Common;

namespace One4All.Infrastructure.Persistence.RawSql;

/// <summary>
/// Npgsql-based implementation of <see cref="ISqlScriptRunner"/>. Applies each script inside its own
/// transaction and records it in <c>sys.__sql_script_history</c>. Ordering is by relative path, so a
/// numeric prefix (e.g. <c>01_views/…</c>, <c>02_functions/…</c>) controls execution order.
/// </summary>
public sealed class SqlScriptRunner : ISqlScriptRunner
{
    private readonly string _connectionString;

    /// <summary>Creates the runner for a given connection string.</summary>
    /// <param name="connectionString">A PostgreSQL connection string.</param>
    public SqlScriptRunner(string connectionString) => _connectionString = connectionString;

    /// <inheritdoc />
    public async Task<Result<int>> ApplyAsync(string rootDirectory, CancellationToken ct = default)
    {
        if (!Directory.Exists(rootDirectory))
        {
            return Error.NotFound("Sql.RootMissing", $"SQL script directory not found: {rootDirectory}");
        }

        await using NpgsqlConnection connection = new(_connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);

        await EnsureHistoryTableAsync(connection, ct).ConfigureAwait(false);
        HashSet<string> applied = await LoadAppliedAsync(connection, ct).ConfigureAwait(false);

        string[] files = Directory
            .GetFiles(rootDirectory, "*.sql", SearchOption.AllDirectories)
            .OrderBy(path => Path.GetRelativePath(rootDirectory, path), StringComparer.OrdinalIgnoreCase)
            .ToArray();

        int count = 0;
        foreach (string file in files)
        {
            string relative = Path.GetRelativePath(rootDirectory, file).Replace('\\', '/');
            string sql = await File.ReadAllTextAsync(file, ct).ConfigureAwait(false);
            string hash = Hash(sql);
            string key = $"{relative}:{hash}";

            if (applied.Contains(key))
            {
                continue;
            }

            await using NpgsqlTransaction tx = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            try
            {
                await using (NpgsqlCommand cmd = new(sql, connection, tx))
                {
                    await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                await using (NpgsqlCommand record = new(
                    "INSERT INTO sys.__sql_script_history (script_path, content_hash, applied_at) " +
                    "VALUES (@p, @h, now()) " +
                    "ON CONFLICT (script_path) DO UPDATE SET content_hash = EXCLUDED.content_hash, applied_at = now()",
                    connection, tx))
                {
                    record.Parameters.AddWithValue("p", relative);
                    record.Parameters.AddWithValue("h", hash);
                    await record.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                await tx.CommitAsync(ct).ConfigureAwait(false);
                count++;
            }
            catch (PostgresException ex)
            {
                await tx.RollbackAsync(ct).ConfigureAwait(false);
                return Error.Failure("Sql.ScriptFailed", $"Script '{relative}' failed: {ex.MessageText}");
            }
        }

        return count;
    }

    private static async Task EnsureHistoryTableAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        const string ddl =
            "CREATE SCHEMA IF NOT EXISTS sys; " +
            "CREATE TABLE IF NOT EXISTS sys.__sql_script_history (" +
            "  script_path text PRIMARY KEY, " +
            "  content_hash text NOT NULL, " +
            "  applied_at timestamptz NOT NULL DEFAULT now());";

        await using NpgsqlCommand cmd = new(ddl, connection);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<HashSet<string>> LoadAppliedAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        HashSet<string> applied = new(StringComparer.OrdinalIgnoreCase);
        await using NpgsqlCommand cmd = new("SELECT script_path, content_hash FROM sys.__sql_script_history", connection);
        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            applied.Add($"{reader.GetString(0)}:{reader.GetString(1)}");
        }

        return applied;
    }

    private static string Hash(string content)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content.Replace("\r\n", "\n")));
        return Convert.ToHexStringLower(bytes);
    }
}
