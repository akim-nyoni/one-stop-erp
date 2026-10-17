using FluentAssertions;
using Npgsql;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>First-run setup creates the database named in the connection string once, and reports the server it reaches.</summary>
[Collection(AccountingCollection.Name)]
public sealed class DatabaseBootstrapTests
{
    private readonly AccountingFixture _fixture;

    public DatabaseBootstrapTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task The_database_is_created_on_first_run_and_left_alone_after()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        string name = "one4all_boot_" + Guid.NewGuid().ToString("N")[..8];
        string connection = new NpgsqlConnectionStringBuilder(_fixture.ConnectionString) { Database = name }.ConnectionString;
        try
        {
            (await DatabaseBootstrap.ServerVersionAsync(connection)).Should().NotBeNullOrEmpty();
            (await DatabaseBootstrap.EnsureDatabaseAsync(connection)).Should().BeTrue("the database did not exist");
            (await DatabaseBootstrap.EnsureDatabaseAsync(connection)).Should().BeFalse("it exists now");
            await using NpgsqlConnection created = new(connection);
            await created.OpenAsync();
            created.State.Should().Be(System.Data.ConnectionState.Open);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using NpgsqlConnection admin = new(new NpgsqlConnectionStringBuilder(_fixture.ConnectionString) { Database = "postgres" }.ConnectionString);
            await admin.OpenAsync();
            await using NpgsqlCommand drop = new($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }

        Func<Task> bad = () => DatabaseBootstrap.EnsureDatabaseAsync(new NpgsqlConnectionStringBuilder(_fixture.ConnectionString) { Database = "Bad Name; DROP" }.ConnectionString);
        await bad.Should().ThrowAsync<InvalidOperationException>();
        (await DatabaseBootstrap.ServerVersionAsync("Host=localhost;Port=1;Database=x;Username=u;Password=p")).Should().BeNull();
    }
}
