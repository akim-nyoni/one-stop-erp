using Npgsql;

namespace One4All.Infrastructure.Persistence;

/// <summary>
/// First-run database setup (brief §10 packaging): the server and the desktop create the One4All database on the PostgreSQL server
/// named in the connection string when it does not exist yet, then migrate and seed it as usual. The connection string's own login
/// must be allowed to create databases (the installer's service account is); otherwise the database is created by the administrator
/// and this is a no-op.
/// </summary>
public static class DatabaseBootstrap
{
    /// <summary>Creates the database named in the connection string when the server has no such database.</summary>
    /// <param name="connectionString">The application connection string.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True when the database was created now, false when it already existed.</returns>
    public static async Task<bool> EnsureDatabaseAsync(string connectionString, CancellationToken ct = default)
    {
        NpgsqlConnectionStringBuilder application = new(connectionString);
        string name = application.Database ?? throw new InvalidOperationException("The connection string names no database.");
        if (!IsSafeName(name))
        {
            throw new InvalidOperationException($"The database name '{name}' is not one this setup will create (lower-case letters, digits and underscores).");
        }

        NpgsqlConnectionStringBuilder maintenance = new(connectionString) { Database = "postgres", Pooling = false };
        await using NpgsqlConnection connection = new(maintenance.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using (NpgsqlCommand exists = new("SELECT 1 FROM pg_database WHERE datname = @n", connection))
        {
            exists.Parameters.AddWithValue("n", name);
            if (await exists.ExecuteScalarAsync(ct).ConfigureAwait(false) is not null)
            {
                return false;
            }
        }

        await using NpgsqlCommand create = new($"CREATE DATABASE \"{name}\" ENCODING 'UTF8'", connection); // the name is validated above
        await create.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>Whether the PostgreSQL server in the connection string answers at all (the installer's prerequisite check).</summary>
    /// <param name="connectionString">The application connection string.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The server version, or null when it cannot be reached.</returns>
    public static async Task<string?> ServerVersionAsync(string connectionString, CancellationToken ct = default)
    {
        try
        {
            NpgsqlConnectionStringBuilder maintenance = new(connectionString) { Database = "postgres", Pooling = false, Timeout = 5 };
            await using NpgsqlConnection connection = new(maintenance.ConnectionString);
            await connection.OpenAsync(ct).ConfigureAwait(false);
            return connection.PostgreSqlVersion.ToString();
        }
        catch (Exception ex) when (ex is NpgsqlException or TimeoutException or System.Net.Sockets.SocketException)
        {
            return null;
        }
    }

    private static bool IsSafeName(string name) => name.Length is >= 1 and <= 63 && char.IsAsciiLetterLower(name[0]) && name.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_');
}
