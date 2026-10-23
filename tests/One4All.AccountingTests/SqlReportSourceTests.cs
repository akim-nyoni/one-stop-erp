using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using One4All.Application.Abstractions.Reporting;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Reporting;
using One4All.Reporting.Engine;
using One4All.Reporting.Model;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers user-authored report data sources: the statement checks (one SELECT filtered by company, no protected schemas or file functions,
/// views and functions only from rpt), and running them for real - company rows only, report parameters bound, a reporting view read, and a
/// data-changing CTE that slips past the text checks still stopped by the read-only transaction.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class SqlReportSourceTests
{
    private readonly AccountingFixture _fixture;

    public SqlReportSourceTests(AccountingFixture fixture) => _fixture = fixture;

    [Theory]
    [InlineData("sql:SELECT account_code FROM gl.account WHERE company_id = @CompanyId", true, null)]
    [InlineData("sql:WITH t AS (SELECT 1 AS x, @CompanyId AS company_id) SELECT x FROM t WHERE company_id = @CompanyId", true, null)]
    [InlineData("sql:WITH t AS (SELECT 1 AS x) SELECT x FROM t WHERE @CompanyId > 0", false, "company_id = @CompanyId")]
    [InlineData("sql:SELECT * FROM gl.account", false, "@CompanyId")]
    [InlineData("sql:SELECT * FROM \"sec\".\"user\" WHERE company_id = @CompanyId", false, "sec")]
    [InlineData("sql:SELECT * FROM gl.account WHERE company_id = @CompanyId AND set_config('role', 'postgres', true) IS NOT NULL", false, "set_config")]
    [InlineData("sql:DELETE FROM gl.account WHERE company_id = @CompanyId", false, "SELECT")]
    [InlineData("sql:SELECT 1 WHERE @CompanyId > 0; DROP TABLE gl.account", false, "one statement")]
    [InlineData("sql:SELECT password_hash FROM sec.user_account WHERE @CompanyId > 0", false, "sec")]
    [InlineData("sql:SELECT * FROM pay.employee WHERE company_id = @CompanyId", false, "pay")]
    [InlineData("sql:SELECT pg_read_file('x') WHERE @CompanyId > 0", false, "pg_read_file")]
    [InlineData("sql:SELECT 'sec.user; drop' AS note FROM gl.account WHERE company_id = @CompanyId", true, null)]
    [InlineData("view:rpt.aged_debt", true, null)]
    [InlineData("view:sec.user_account", false, "rpt.")]
    [InlineData("function:rpt.sales_between", true, null)]
    [InlineData("function:public.anything", false, "rpt.")]
    public void Sql_sources_are_checked(string command, bool allowed, string? reason)
    {
        (string? sql, string? refusal) = SqlReportDataProvider.Statement(command, ["FromDate", "To Date", "ToDate"]);
        (sql is not null).Should().Be(allowed, refusal);
        if (reason is not null)
        {
            refusal.Should().Contain(reason);
        }

        if (command.StartsWith("function:", StringComparison.Ordinal) && allowed)
        {
            sql.Should().Be("SELECT * FROM rpt.sales_between(@CompanyId, @FromDate, @ToDate)", "parameter names that are not identifiers are left out");
        }
    }

    [SkippableFact]
    public async Task Queries_run_read_only_for_the_company()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        long other = await Scenario.SeedAsync(db);
        IReportDataProvider sql = scope.ServiceProvider.GetServices<IReportDataProvider>().Single(p => p.CanHandle("sql:x"));

        IReadOnlyList<IReadOnlyDictionary<string, object?>> accounts = await sql.GetRowsAsync(companyId,
            "sql:SELECT account_code, name, company_id FROM gl.account WHERE company_id = @CompanyId AND account_code >= @From ORDER BY account_code", new Dictionary<string, object?> { ["From"] = "6000" });
        accounts.Should().NotBeEmpty().And.OnlyContain(r => (long)r["company_id"]! == companyId && string.CompareOrdinal((string)r["account_code"]!, "6000") >= 0);
        accounts.Should().NotContain(r => (long)r["company_id"]! == other);

        (await sql.GetRowsAsync(companyId, "view:rpt.aged_debt", new Dictionary<string, object?>())).Should().NotContain(r => (long)r["company_id"]! != companyId);

        // A WHERE that names the company but does not bind it (1 = 1 OR ...) is cut to the company again on the way out.
        (await sql.GetRowsAsync(companyId, "sql:SELECT account_code, company_id FROM gl.account WHERE 1 = 1 OR company_id = @CompanyId", new Dictionary<string, object?>()))
            .Should().NotBeEmpty().And.OnlyContain(r => (long)r["company_id"]! == companyId);

        // The statement runs as the reporting role, which may read the business schemas only: a schema outside them is denied by the database itself.
        (await sql.GetRowsAsync(companyId, "sql:SELECT current_user::text AS who FROM gl.account WHERE company_id = @CompanyId LIMIT 1", new Dictionary<string, object?>()))
            .Should().ContainSingle().Which["who"].Should().Be(SqlReportDataProvider.ReportRole);
        Func<Task> messages = () => sql.GetRowsAsync(companyId, "sql:SELECT * FROM msg.outbound_message WHERE company_id = @CompanyId", new Dictionary<string, object?>());
        (await messages.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501", "the reporting role has no rights on the messaging schema");

        // A source past the cap ends with a visible notice row rather than stopping short in silence.
        IReadOnlyList<IReadOnlyDictionary<string, object?>> capped = await sql.GetRowsAsync(companyId,
            $"sql:SELECT n AS number, 'x' AS label FROM generate_series(1, {SqlReportDataProvider.MaxRows + 5}) AS s(n), (SELECT @CompanyId AS company_id) c WHERE c.company_id = @CompanyId", new Dictionary<string, object?>());
        capped.Should().HaveCount(SqlReportDataProvider.MaxRows + 1);
        capped[^1]["number"].Should().Be(SqlReportDataProvider.CapNoticeText);
        capped[^1]["label"].Should().BeNull();
        (await sql.GetRowsAsync(companyId, $"sql:SELECT n FROM generate_series(1, {SqlReportDataProvider.MaxRows}) AS s(n), (SELECT @CompanyId AS company_id) c WHERE c.company_id = @CompanyId", new Dictionary<string, object?>()))
            .Should().HaveCount(SqlReportDataProvider.MaxRows);

        Func<Task> sneaky = () => sql.GetRowsAsync(companyId, "sql:WITH gone AS (DELETE FROM gl.account WHERE company_id = @CompanyId RETURNING 1) SELECT * FROM gone",
            new Dictionary<string, object?>());
        (await sneaky.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().BeOneOf("25006", "0A000");
        Func<Task> writes = () => sql.GetRowsAsync(companyId, "sql:SELECT nextval('gl.account_id_seq') AS n FROM gl.account WHERE company_id = @CompanyId", new Dictionary<string, object?>());
        (await writes.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().BeOneOf(["25006", "42501"], "the transaction is read-only and the reporting role cannot use sequences");
        Func<Task> refused = () => sql.GetRowsAsync(companyId, "sql:SELECT * FROM gl.account", new Dictionary<string, object?>());
        await refused.Should().ThrowAsync<InvalidOperationException>().WithMessage("*@CompanyId*");

        ReportDefinition authored = new() { Code = "SQLX", Name = "SQL", DataSources = [new ReportDataSource { Kind = DataSourceKind.Sql, Command = "SELECT 1 WHERE company_id = @CompanyId" }] };
        ReportCommands.For(authored).Should().Be("sql:SELECT 1 WHERE company_id = @CompanyId");

        // A report whose source would be refused is refused at save, with the same checks.
        IReportDefinitionService definitions = scope.ServiceProvider.GetRequiredService<IReportDefinitionService>();
        ReportDefinition unsafeReport = new() { Code = "SQLX", Name = "SQL", DataSources = [new ReportDataSource { Kind = DataSourceKind.Sql, Command = "SELECT * FROM \"pay\".\"employee\" WHERE company_id = @CompanyId" }] };
        (await definitions.SaveAsync(companyId, "SQLX", "SQL", "GL", ReportDefinitionSerializer.Serialize(unsafeReport), 900_001, null)).Error.Message.Should().Contain("pay");
        (await definitions.SaveAsync(companyId, "SQLX", "SQL", "GL", ReportDefinitionSerializer.Serialize(authored), 900_001, null)).IsSuccess.Should().BeTrue();
    }

    [SkippableFact]
    public async Task Fields_are_discovered_without_reading_rows()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        IReportFieldDiscovery discovery = scope.ServiceProvider.GetRequiredService<IReportFieldDiscovery>();

        var fields = (await discovery.DescribeAsync(companyId, "sql:SELECT account_code, name, budget_enabled FROM gl.account WHERE company_id = @CompanyId AND account_code >= @From",
            new Dictionary<string, object?> { ["From"] = "1000" })).Value;
        fields.Select(f => f.Name).Should().Equal("account_code", "name", "budget_enabled");
        fields[2].TypeName.Should().Be("boolean");
        (await discovery.DescribeAsync(companyId, "sql:SELECT * FROM sec.app_user", new Dictionary<string, object?>())).Error.Code.Should().Be("Rpt.Source");
        (await discovery.DescribeAsync(companyId, "sql:SELECT nope FROM gl.account WHERE company_id = @CompanyId", new Dictionary<string, object?>())).Error.Message.Should().Contain("nope");
    }
}
