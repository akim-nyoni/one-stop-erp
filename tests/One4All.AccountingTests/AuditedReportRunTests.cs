using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Reporting;
using One4All.Core.Security;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using One4All.Reporting;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers audited report runs: each purpose needs its permission flag; a repeated print of the same report and parameters
/// becomes a numbered reprint needing the reprint flag; refused runs are not logged; unattributed (desktop) runs are; and
/// the log carries parameters and the document fingerprint.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class AuditedReportRunTests
{
    private readonly AccountingFixture _fixture;

    public AuditedReportRunTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Report_runs_are_permission_checked_and_logged_with_reprints_numbered()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        User Grant(string name, OperationFlags flags)
        {
            Role role = new($"Reports {name} {companyId}");
            role.Grant("Reporting.Report", flags);
            db.Roles.Add(role);
            User user = new($"rpt-{name}-{companyId}", name);
            db.Users.Add(user);
            db.SaveChanges();
            user.AssignRole(role.Id);
            db.SaveChanges();
            return user;
        }

        User viewer = Grant("viewer", OperationFlags.View);
        User printer = Grant("printer", OperationFlags.View | OperationFlags.Print);
        User clerk = Grant("clerk", OperationFlags.View | OperationFlags.Print | OperationFlags.Export | OperationFlags.Reprint);

        IAuditedReportRunner reports = scope.ServiceProvider.GetRequiredService<IAuditedReportRunner>();
        Dictionary<string, object?> may = new() { ["AsAtDate"] = new DateOnly(2026, 5, 31) };
        Task<One4All.Core.Common.Result<AuditedReport>> Run(ReportRunPurpose purpose, long? user, Dictionary<string, object?>? parameters = null)
            => reports.RunAsync(companyId, SystemReports.TrialBalanceCode, parameters ?? may, ReportFormat.Html, purpose, user);

        (await Run(ReportRunPurpose.View, viewer.Id)).IsSuccess.Should().BeTrue();
        (await Run(ReportRunPurpose.Export, viewer.Id)).Error.Code.Should().Be("Rpt.NotAuthorised");

        AuditedReport original = (await Run(ReportRunPurpose.Print, printer.Id)).Value;
        (original.Purpose, original.CopyNumber).Should().Be((ReportRunPurpose.Print, (int?)1));
        (await Run(ReportRunPurpose.Print, printer.Id)).Error.Code.Should().Be("Rpt.NotAuthorised"); // it would be a reprint
        AuditedReport copy = (await Run(ReportRunPurpose.Print, clerk.Id)).Value;
        (copy.Purpose, copy.CopyNumber).Should().Be((ReportRunPurpose.Reprint, (int?)2));
        (await Run(ReportRunPurpose.Print, clerk.Id, new Dictionary<string, object?> { ["AsAtDate"] = new DateOnly(2026, 4, 30) })).Value.CopyNumber.Should().Be(1);
        (await Run(ReportRunPurpose.Export, null)).IsSuccess.Should().BeTrue(); // the desktop viewer, not yet signed in per user

        IReadOnlyList<ReportRunEntry> log = await reports.RunLogAsync(companyId, SystemReports.TrialBalanceCode);
        log.Should().HaveCount(5);
        log.Select(l => (l.Purpose, l.UserId)).Should().BeEquivalentTo(new[]
        {
            (ReportRunPurpose.View, (long?)viewer.Id), (ReportRunPurpose.Print, (long?)printer.Id), (ReportRunPurpose.Reprint, (long?)clerk.Id),
            (ReportRunPurpose.Print, (long?)clerk.Id), (ReportRunPurpose.Export, (long?)null),
        });
        log.Where(l => l.Parameters == "AsAtDate=2026-05-31" && l.Purpose != ReportRunPurpose.View).Select(l => l.ContentHash).Distinct().Should().ContainSingle().Which.Should().HaveLength(64);
    }
}
