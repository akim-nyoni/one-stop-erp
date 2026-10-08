using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Security;
using One4All.Core.Accounting;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the widened second net (brief §6): journal headers and the chart are captured, journal lines only when changed or inserted from
/// outside the application (the application's own postings are not doubled), and the dimension and permission tables are watched too.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class SecondNetCoverageTests
{
    private readonly AccountingFixture _fixture;

    public SecondNetCoverageTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Ledger_and_permission_tables_are_watched_without_doubling_postings()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await Scenario.DimensionsAsync(db, companyId, "WTC");
        DateTimeOffset start = DateTimeOffset.UtcNow.AddSeconds(-1);

        PostingReceipt posted = (await scope.ServiceProvider.GetRequiredService<IPostingEngine>().PostAsync(new JournalRequest
        {
            CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 3, 10), TransactionCurrency = Scenario.Usd, EnforceManualJournalRules = false,
            Lines =
            [
                new JournalLineRequest { AccountCode = "6800", Side = JournalSide.Debit, TransactionAmount = 25m, CostCentre = "WTC" },
                new JournalLineRequest { AccountCode = "1000", Side = JournalSide.Credit, TransactionAmount = 25m },
            ],
        })).Value;

        long costCentre = await db.CostCentres.Where(c => c.CompanyId == companyId && c.Code == "WTC").Select(c => c.Id).SingleAsync();
        long account = await db.Accounts.Where(a => a.CompanyId == companyId && a.AccountCode == "6800").Select(a => a.Id).SingleAsync();
        NpgsqlConnectionStringBuilder direct = new(_fixture.ConnectionString) { ApplicationName = "psql" };
        await using (NpgsqlConnection connection = new(direct.ConnectionString))
        {
            await connection.OpenAsync();
            await using NpgsqlCommand rename = new($"UPDATE gl.cost_centre SET name = 'renamed by hand' WHERE id = {costCentre}", connection);
            (await rename.ExecuteNonQueryAsync()).Should().Be(1);
            await using NpgsqlCommand retitle = new($"UPDATE gl.account SET name = 'retitled by hand' WHERE id = {account}", connection);
            (await retitle.ExecuteNonQueryAsync()).Should().Be(1);
        }

        IReadOnlyList<DatabaseChangeView> changes = await scope.ServiceProvider.GetRequiredService<IAuditService>().DatabaseChangesAsync(start, DateTimeOffset.UtcNow.AddMinutes(1), false, 5000);
        changes.Should().Contain(c => c.TableName == "gl.journal" && c.Operation == "INSERT" && c.RowId == posted.JournalId.ToString() && !c.OutsideApplication);
        changes.Should().NotContain(c => c.TableName == "gl.journal_line" && c.Operation == "INSERT" && !c.OutsideApplication, "the application's own lines are not doubled");
        changes.Should().Contain(c => c.TableName == "gl.cost_centre" && c.Operation == "UPDATE" && c.RowId == costCentre.ToString() && c.OutsideApplication);
        changes.Should().Contain(c => c.TableName == "gl.account" && c.Operation == "UPDATE" && c.RowId == account.ToString() && c.OutsideApplication);
    }
}
