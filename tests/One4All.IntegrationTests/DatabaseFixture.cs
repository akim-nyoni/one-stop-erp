using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using One4All.Infrastructure;
using One4All.Infrastructure.Licensing;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.IntegrationTests;

/// <summary>
/// Provides a real PostgreSQL database for integration tests. Each test run gets its own throwaway
/// database (dropped on dispose) so runs never interfere. The connection is taken from
/// <c>ONE4ALL_TEST_CONNECTION</c>, else <c>ONE4ALL_CONNECTION</c>, else the local development default.
/// If no server is reachable, <see cref="Available"/> is false and dependent tests skip.
///
/// Docker is not required (the dev machine has none): tests run against the local PostgreSQL service,
/// and CI runs them against its <c>postgres</c> service container.
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    private string _adminConnectionString = string.Empty;
    private string _databaseName = string.Empty;

    /// <summary>True when a PostgreSQL server was reachable and the test database was created.</summary>
    public bool Available { get; private set; }

    /// <summary>The reason the fixture is unavailable, for the skip message.</summary>
    public string? SkipReason { get; private set; }

    /// <summary>The connection string to the throwaway test database.</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>Creates a new plain context against the test database (no interceptors).</summary>
    /// <returns>A configured context.</returns>
    public One4AllDbContext CreateContext()
    {
        DbContextOptionsBuilder<One4AllDbContext> builder = new();
        builder.UseOne4All(ConnectionString);
        return new One4AllDbContext(builder.Options);
    }

    /// <summary>
    /// Builds a fully wired service provider (audit interceptor, security and licensing services) against
    /// the test database, optionally with trusted licensing keys.
    /// </summary>
    /// <param name="licensingOptions">Optional licensing options.</param>
    /// <returns>The service provider (dispose it when done).</returns>
    public ServiceProvider BuildServiceProvider(LicensingOptions? licensingOptions = null)
    {
        ServiceCollection services = new();
        services.AddOne4AllInfrastructure(ConnectionString, licensingOptions);
        return services.BuildServiceProvider();
    }

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        string baseConnection =
            Environment.GetEnvironmentVariable("ONE4ALL_TEST_CONNECTION")
            ?? Environment.GetEnvironmentVariable("ONE4ALL_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=one4all;Username=postgres;Password=root";

        NpgsqlConnectionStringBuilder csb = new(baseConnection);
        _databaseName = $"one4all_it_{Guid.NewGuid():N}";
        NpgsqlConnectionStringBuilder admin = new(baseConnection) { Database = "postgres" };
        _adminConnectionString = admin.ConnectionString;

        try
        {
            await using (NpgsqlConnection connection = new(_adminConnectionString))
            {
                await connection.OpenAsync();
                await using NpgsqlCommand create = new($"CREATE DATABASE \"{_databaseName}\"", connection);
                await create.ExecuteNonQueryAsync();
            }

            csb.Database = _databaseName;
            ConnectionString = csb.ConnectionString;

            await using One4AllDbContext db = CreateContext();
            await db.Database.MigrateAsync();

            Available = true;
        }
        catch (Exception ex) when (ex is NpgsqlException or System.Net.Sockets.SocketException)
        {
            Available = false;
            SkipReason = $"PostgreSQL not reachable for integration tests: {ex.Message}";
        }
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (!Available)
        {
            return;
        }

        try
        {
            await using NpgsqlConnection connection = new(_adminConnectionString);
            await connection.OpenAsync();
            await using NpgsqlCommand terminate = new(
                $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{_databaseName}' AND pid <> pg_backend_pid();",
                connection);
            await terminate.ExecuteNonQueryAsync();
            await using NpgsqlCommand drop = new($"DROP DATABASE IF EXISTS \"{_databaseName}\"", connection);
            await drop.ExecuteNonQueryAsync();
        }
        catch (NpgsqlException)
        {
            // Best-effort cleanup; a leftover throwaway database is harmless.
        }
    }
}

/// <summary>Shared collection so the database is created once per test run.</summary>
[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    /// <summary>The collection name.</summary>
    public const string Name = "database";
}
