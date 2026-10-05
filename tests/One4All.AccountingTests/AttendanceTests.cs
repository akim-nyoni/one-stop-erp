using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Payroll;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Payroll;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers dated leave and attendance (brief §14 O PR25, PR39–PR41, PR43): leave taken through the balance without overlaps, a Monday
/// sick day and a Friday one, an encashment, overtime approved (never by oneself) and not, a missing clock-out and an over-long day.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class AttendanceTests
{
    private readonly AccountingFixture _fixture;

    public AttendanceTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Leave_and_attendance_are_recorded_and_reported()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        Employee ann = new(companyId, "AT-A", "Ann", 1_733.30m, new DateOnly(2024, 1, 1)) { Department = "Ops", UserId = 5 };
        Employee ben = new(companyId, "AT-B", "Ben", 1_000m, new DateOnly(2024, 1, 1)) { Department = "Ops" };
        db.Employees.AddRange(ann, ben);
        await db.SaveChangesAsync();
        ILeaveService leave = scope.ServiceProvider.GetRequiredService<ILeaveService>();
        (await leave.AccrueAsync(companyId, ann.Id, "Annual", 20m)).IsSuccess.Should().BeTrue();
        (await leave.AccrueAsync(companyId, ann.Id, "Sick", 30m)).IsSuccess.Should().BeTrue();

        IAttendanceService attendance = scope.ServiceProvider.GetRequiredService<IAttendanceService>();
        (await attendance.TakeLeaveAsync(companyId, ann.Id, "Annual", new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 6), 8m)).Error.Code.Should().Be("Hr.LeaveDates");
        (await attendance.TakeLeaveAsync(companyId, ann.Id, "Annual", new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 6), 5m)).IsSuccess.Should().BeTrue();
        (await attendance.TakeLeaveAsync(companyId, ann.Id, "Annual", new DateOnly(2026, 3, 5), new DateOnly(2026, 3, 6), 2m)).Error.Code.Should().Be("Hr.LeaveOverlap");
        (await attendance.TakeLeaveAsync(companyId, ben.Id, "Annual", new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 2), 1m)).IsFailure.Should().BeTrue(); // no balance
        (await attendance.TakeLeaveAsync(companyId, ann.Id, "Sick", new DateOnly(2026, 3, 9), new DateOnly(2026, 3, 9), 1m)).IsSuccess.Should().BeTrue(); // a Monday
        (await attendance.EncashLeaveAsync(companyId, ann.Id, "Annual", new DateOnly(2026, 3, 31), 3m, 80m)).IsSuccess.Should().BeTrue();

        TimeOnly T(int h, int m = 0) => new(h, m);
        long annOvertime = (await attendance.RecordDayAsync(companyId, ann.Id, new DateOnly(2026, 3, 10), AttendanceStatus.Present, T(8), T(17), 0, 2m, 1.5m)).Value;
        (await attendance.RecordDayAsync(companyId, ann.Id, new DateOnly(2026, 3, 11), AttendanceStatus.Present, T(8, 15), T(23, 30), 15, 0m, 0m)).IsSuccess.Should().BeTrue();
        (await attendance.RecordDayAsync(companyId, ben.Id, new DateOnly(2026, 3, 10), AttendanceStatus.Present, T(8), null, 0, 0m, 0m)).IsSuccess.Should().BeTrue();
        (await attendance.RecordDayAsync(companyId, ben.Id, new DateOnly(2026, 3, 12), AttendanceStatus.Absent, null, null, 0, 0m, 0m)).IsSuccess.Should().BeTrue();
        (await attendance.RecordDayAsync(companyId, ben.Id, new DateOnly(2026, 3, 12), AttendanceStatus.Absent, null, null, 0, 0m, 0m)).Error.Code.Should().Be("Hr.AttendanceExists");
        (await attendance.RecordDayAsync(companyId, ben.Id, new DateOnly(2026, 3, 13), AttendanceStatus.Sick, null, null, 0, 1m, 1.5m)).Error.Code.Should().Be("Hr.AttendanceAbsentWork");
        (await attendance.RecordDayAsync(companyId, ben.Id, new DateOnly(2026, 3, 13), AttendanceStatus.Sick, null, null, 0, 0m, 0m)).IsSuccess.Should().BeTrue(); // a Friday
        (await attendance.RecordDayAsync(companyId, ben.Id, new DateOnly(2026, 3, 16), AttendanceStatus.Present, T(8), T(19), 0, 3m, 2m)).IsSuccess.Should().BeTrue();
        (await attendance.ApproveOvertimeAsync(companyId, annOvertime, 5)).Error.Code.Should().Be("Hr.SelfApproval");
        (await attendance.ApproveOvertimeAsync(companyId, annOvertime, 9)).IsSuccess.Should().BeTrue();

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        async Task<string> Render(string code)
        {
            Dictionary<string, object?> parameters = new() { ["FromDate"] = new DateOnly(2026, 3, 1), ["ToDate"] = new DateOnly(2026, 3, 31) };
            var rendered = await runner.RenderAsync(companyId, code, parameters, ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? $"{code}: {rendered.Error.Message}" : null);
            return Encoding.UTF8.GetString(rendered.Value.Content);
        }

        (await Render("PR25")).Should().Contain("Ops,AT-A Ann,1.5,2,2,0,30").And.Contain("Ops,AT-B Ben,2,3,0,3,34.62");
        (await Render("PR39")).Should().Contain("Annual,Ops,2026-03,1,1,5").And.Contain("Sick,Ops,2026-03,1,1,1");
        (await Render("PR40")).Should().Contain("AT-A Ann,0,1,15,1,1,1,Full pay").And.Contain("AT-B Ben,1,0,0,1,1,1,Full pay");
        (await Render("PR41")).Should().Contain("2026-03-31,AT-A Ann,Annual,3,80.00,240");
        string exceptions = await Render("PR43");
        exceptions.Should().Contain("2026-03-11,AT-A Ann,08:15,23:30,15.25,Over 12 hours").And.Contain("2026-03-10,AT-B Ben,08:00,—,—,No clock-out")
            .And.Contain("2026-03-16,AT-B Ben,08:00,19:00,11,Unapproved overtime 3 h").And.NotContain("2026-03-10,AT-A Ann");
    }
}
