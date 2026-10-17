using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Reporting;
using One4All.Core.Reporting;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using One4All.Reporting;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers report definition versioning: a system report is copied to a company version (once); saves create new versions
/// and keep the old; an unchanged or invalid save is refused; versions are diffed by path; a rollback restores an old
/// version as the newest; and the system report itself is never touched.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ReportVersioningTests
{
    private readonly AccountingFixture _fixture;

    public ReportVersioningTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Company_reports_keep_every_version_and_can_roll_back()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        ReportDefinitionRecord system = await db.ReportDefinitions.AsNoTracking().SingleAsync(r => r.Scope == ReportScope.System && r.Code == SystemReports.TrialBalanceCode);

        IReportDefinitionService reports = scope.ServiceProvider.GetRequiredService<IReportDefinitionService>();
        (await reports.CopySystemReportAsync(companyId, "NO-SUCH", 7)).Error.Code.Should().Be("Rpt.SystemReportNotFound");
        (await reports.CopySystemReportAsync(companyId, SystemReports.TrialBalanceCode, 7)).IsSuccess.Should().BeTrue();
        (await reports.CopySystemReportAsync(companyId, SystemReports.TrialBalanceCode, 7)).Error.Code.Should().Be("Rpt.CompanyVersionExists");

        // The company changes its title: version 2; the same again changes nothing; broken JSON is refused.
        JsonObject definition = JsonNode.Parse(system.DefinitionJson)!.AsObject();
        string nameKey = definition.ContainsKey("name") ? "name" : "Name";
        definition[nameKey] = "Trial Balance - Council format";
        string changed = definition.ToJsonString();
        (await reports.SaveAsync(companyId, SystemReports.TrialBalanceCode, "Trial Balance (council)", "GL", changed, 8, "House style")).Value.Should().Be(2);
        (await reports.SaveAsync(companyId, SystemReports.TrialBalanceCode, "Trial Balance (council)", "GL", changed, 8, null)).Error.Code.Should().Be("Rpt.NoChange");
        (await reports.SaveAsync(companyId, SystemReports.TrialBalanceCode, "Trial Balance (council)", "GL", "{ not json", 8, null)).Error.Code.Should().Be("Rpt.Definition");

        IReadOnlyList<JsonChange> diff = (await reports.DiffAsync(companyId, SystemReports.TrialBalanceCode, 1, 2)).Value;
        diff.Should().ContainSingle();
        (diff[0].Path, diff[0].Kind, diff[0].After).Should().Be(($"$.{nameKey}", JsonChangeKind.Changed, JsonValue.Create("Trial Balance - Council format").ToJsonString()));
        (await reports.DiffAsync(companyId, SystemReports.TrialBalanceCode, 1, 9)).Error.Code.Should().Be("Rpt.VersionNotFound");

        // Rolled back to version 1: saved as version 3, identical to version 1, with the history intact.
        (await reports.RollbackAsync(companyId, SystemReports.TrialBalanceCode, 3, 9)).Error.Code.Should().Be("Rpt.VersionNotFound");
        (await reports.RollbackAsync(companyId, SystemReports.TrialBalanceCode, 1, 9)).Value.Should().Be(3);
        (await reports.RollbackAsync(companyId, SystemReports.TrialBalanceCode, 3, 9)).Error.Code.Should().Be("Rpt.AlreadyCurrent");
        (await reports.DiffAsync(companyId, SystemReports.TrialBalanceCode, 1, 3)).Value.Should().BeEmpty();
        IReadOnlyList<ReportVersionEntry> history = (await reports.HistoryAsync(companyId, SystemReports.TrialBalanceCode)).Value;
        history.Select(h => (h.Version, h.IsCurrent)).Should().Equal((3, true), (2, false), (1, false));
        history.Select(h => h.Note).Should().Equal("Rolled back to v1", "House style", $"Copied from system report v{system.Version}");

        // The system report was never touched.
        ReportDefinitionRecord after = await db.ReportDefinitions.AsNoTracking().SingleAsync(r => r.Id == system.Id);
        (after.Version, JsonDiff.Compare(after.DefinitionJson, system.DefinitionJson).Count).Should().Be((system.Version, 0));
    }
}
