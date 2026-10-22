using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using One4All.Infrastructure;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Provides a throwaway PostgreSQL database (migrated) and a wired service provider for the accounting
/// test suite. Mirrors the integration-test fixture; skips gracefully when no server is reachable.
/// </summary>
public sealed class AccountingFixture : IAsyncLifetime
{
    private string _adminConnectionString = string.Empty;
    private string _databaseName = string.Empty;
    private bool _created;

    /// <summary>True when a database was created.</summary>
    public bool Available { get; private set; }

    /// <summary>The skip reason when unavailable.</summary>
    public string? SkipReason { get; private set; }

    /// <summary>The connection string to the throwaway database.</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>Builds a wired service provider against the test database.</summary>
    /// <returns>A disposable provider.</returns>
    public ServiceProvider BuildServiceProvider() => BuildServiceProvider(_ => { });

    /// <summary>Builds a provider with extra registrations (fakes for external gateways, say) added after the infrastructure.</summary>
    /// <param name="configure">The extra registrations.</param>
    /// <returns>The provider.</returns>
    public ServiceProvider BuildServiceProvider(Action<IServiceCollection> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        ServiceCollection services = new();
        services.AddOne4AllInfrastructure(ConnectionString);
        services.AddSingleton<One4All.Licensing.Core.IClock, TestClock>();
        configure(services);
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
        _databaseName = $"one4all_acc_{Guid.NewGuid():N}";
        _adminConnectionString = new NpgsqlConnectionStringBuilder(baseConnection) { Database = "postgres" }.ConnectionString;

        try
        {
            await using (NpgsqlConnection connection = new(_adminConnectionString))
            {
                await connection.OpenAsync();
                await DropAbandonedAsync(connection);
                await using NpgsqlCommand create = new($"CREATE DATABASE \"{_databaseName}\"", connection);
                await create.ExecuteNonQueryAsync();
                _created = true;
            }

            csb.Database = _databaseName;
            ConnectionString = csb.ConnectionString;

            await using ServiceProvider provider = BuildServiceProvider();
            await provider.GetRequiredService<One4AllDbContext>().Database.MigrateAsync();

            Available = true;
        }
        catch (Exception ex) when (ex is NpgsqlException or System.Net.Sockets.SocketException)
        {
            Available = false;
            SkipReason = $"PostgreSQL not reachable: {ex.Message}";
        }
    }

    // Test databases left by runs that were stopped before they could clean up (a killed build, a closed terminal): any one4all_acc_ database
    // nobody is connected to belongs to no running test, so it is dropped. Best effort.
    private static async Task DropAbandonedAsync(NpgsqlConnection connection)
    {
        try
        {
            List<string> abandoned = [];
            await using (NpgsqlCommand find = new(
                "SELECT d.datname FROM pg_database d WHERE d.datname LIKE 'one4all\\_acc\\_%' AND NOT EXISTS (SELECT 1 FROM pg_stat_activity a WHERE a.datname = d.datname)", connection))
            await using (NpgsqlDataReader reader = await find.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    abandoned.Add(reader.GetString(0));
                }
            }

            foreach (string name in abandoned)
            {
                await using NpgsqlCommand drop = new($"DROP DATABASE IF EXISTS \"{name}\"", connection);
                await drop.ExecuteNonQueryAsync();
            }
        }
        catch (NpgsqlException)
        {
            // best-effort
        }
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        // Dropped whenever it was created - also when migrating it failed - so no test database is left behind.
        if (!_created)
        {
            return;
        }

        try
        {
            await using NpgsqlConnection connection = new(_adminConnectionString);
            await connection.OpenAsync();
            await using NpgsqlCommand terminate = new(
                $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{_databaseName}' AND pid <> pg_backend_pid();", connection);
            await terminate.ExecuteNonQueryAsync();
            await using NpgsqlCommand drop = new($"DROP DATABASE IF EXISTS \"{_databaseName}\"", connection);
            await drop.ExecuteNonQueryAsync();
        }
        catch (NpgsqlException)
        {
            // best-effort
        }
    }
}

/// <summary>Shared collection so the database is created once per run.</summary>
[CollectionDefinition(Name)]
public sealed class AccountingCollection : ICollectionFixture<AccountingFixture>
{
    /// <summary>The collection name.</summary>
    public const string Name = "accounting";
}
