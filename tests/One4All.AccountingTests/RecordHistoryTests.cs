using System.IO.Compression;
using System.Text;
using Dapper;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Security;
using One4All.Core.Audit;
using One4All.Core.Security;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers "who touched this record": created by, last changed by, everyone involved and every entry for a record, the database trigger's
/// view of its row with a change made outside the application flagged; and the audit trail exported as CSV, Excel and PDF, each export
/// itself logged.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class RecordHistoryTests
{
    private readonly AccountingFixture _fixture;

    public RecordHistoryTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_records_history_and_audit_exports()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);

        string name = $"Auditors {Guid.NewGuid():N}"[..20];
        Role role = new(name, "Reviewers");
        db.Roles.Add(role);
        await db.SaveChangesAsync();
        role.Description = "Reviewers and approvers";
        await db.SaveChangesAsync();

        // Someone changes the row directly, outside the application.
        await using (NpgsqlConnection direct = new(new NpgsqlConnectionStringBuilder(_fixture.ConnectionString) { ApplicationName = "psql" }.ConnectionString))
        {
            await direct.OpenAsync();
            await direct.ExecuteAsync("UPDATE sec.role SET description = 'changed by hand' WHERE id = @id", new { id = role.Id });
        }

        IAuditService audit = scope.ServiceProvider.GetRequiredService<IAuditService>();
        RecordHistory history = await audit.RecordHistoryAsync(companyId, nameof(Role), role.Id.ToString());
        history.Created.Should().NotBeNull();
        history.Created!.Action.Should().Be(AuditAction.Insert);
        history.LastChanged!.Action.Should().Be(AuditAction.Update);
        history.LastChanged.ChangedFields.Should().Contain("Description");
        history.Entries.Should().HaveCountGreaterThanOrEqualTo(2);
        history.Users.Should().NotBeEmpty();
        history.DatabaseChanges.Should().HaveCountGreaterThanOrEqualTo(3);
        history.OutsideApplication.Should().ContainSingle().Which.Should().Match<DatabaseChangeView>(c => c.Operation == "UPDATE" && c.ApplicationName == "psql");
        (await audit.EntityTypesAsync(companyId)).Should().Contain(nameof(Role));
        (await audit.RecordHistoryAsync(companyId, nameof(Role), "999999999")).Entries.Should().BeEmpty();

        // Exports, each logged.
        AuditQuery query = new() { CompanyId = companyId, IncludeUnscoped = true, EntityType = nameof(Role), EntityId = role.Id.ToString() };
        RenderedDocument csv = (await audit.ExportAsync(query, ReportFormat.Csv, null, "auditor")).Value;
        Encoding.UTF8.GetString(csv.Content).Should().Contain("Insert").And.Contain($"Role #{role.Id}");
        RenderedDocument xlsx = (await audit.ExportAsync(query, ReportFormat.Xlsx, null, "auditor")).Value;
        using (ZipArchive zip = new(new MemoryStream(xlsx.Content)))
        {
            zip.GetEntry("xl/worksheets/sheet1.xml").Should().NotBeNull();
        }

        RenderedDocument pdf = (await audit.ExportAsync(query, ReportFormat.Pdf, null, "auditor")).Value;
        Encoding.ASCII.GetString(pdf.Content, 0, 5).Should().Be("%PDF-");
        (await audit.ExportAsync(query, ReportFormat.Html, null, "auditor")).Error.Code.Should().Be("Audit.ExportFormat");
        (await db.ChangeLog.AsNoTracking().CountAsync(c => c.CompanyId == companyId && c.Action == AuditAction.Export && c.EntityType == "AuditTrail")).Should().Be(3);
    }
}
