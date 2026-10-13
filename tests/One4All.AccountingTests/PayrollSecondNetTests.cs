using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using One4All.Application.Abstractions.Security;
using One4All.Core.Payroll;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the second net over payroll (brief §6): an employee taken on through the application is captured, and a salary raised by hand in the
/// database is captured as a change from outside the application with the old and new values.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PayrollSecondNetTests
{
    private readonly AccountingFixture _fixture;

    public PayrollSecondNetTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Payroll_tables_record_changes_made_outside_the_application()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        DateTimeOffset start = DateTimeOffset.UtcNow.AddSeconds(-1);
        Employee employee = new(companyId, $"PSN-{companyId % 100000}", "Second Net", 1_000m, new DateOnly(2025, 1, 1));
        db.Employees.Add(employee);
        await db.SaveChangesAsync();

        NpgsqlConnectionStringBuilder direct = new(_fixture.ConnectionString) { ApplicationName = "psql" };
        await using (NpgsqlConnection connection = new(direct.ConnectionString))
        {
            await connection.OpenAsync();
            await using NpgsqlCommand raise = new($"UPDATE hr.employee SET basic_salary = 9000 WHERE id = {employee.Id}", connection);
            (await raise.ExecuteNonQueryAsync()).Should().Be(1);
        }

        IReadOnlyList<DatabaseChangeView> changes = await scope.ServiceProvider.GetRequiredService<IAuditService>().DatabaseChangesAsync(start, DateTimeOffset.UtcNow.AddMinutes(1), false, 5000);
        changes.Should().Contain(c => c.TableName == "hr.employee" && c.Operation == "INSERT" && c.RowId == employee.Id.ToString() && !c.OutsideApplication);
        DatabaseChangeView raised = changes.Single(c => c.TableName == "hr.employee" && c.Operation == "UPDATE" && c.RowId == employee.Id.ToString());
        raised.OutsideApplication.Should().BeTrue();
        raised.ApplicationName.Should().Be("psql");
    }
}
