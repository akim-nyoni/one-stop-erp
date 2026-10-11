using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Payroll;
using One4All.Core.Payroll;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers importing a time-clock export: days recorded late against the roster with overtime, repeats and unknown badges reported.</summary>
[Collection(AccountingCollection.Name)]
public sealed class ClockImportTests
{
    private readonly AccountingFixture _fixture;

    public ClockImportTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Clock_punches_become_attendance_days()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Employee ann = new(companyId, "CK-1", "Ann", 1000m, new DateOnly(2025, 1, 1));
        Shift day = new(companyId, "CK-DAY", "Day shift", new TimeOnly(8, 0), new TimeOnly(17, 0)) { GraceMinutes = 5 };
        db.AddRange(ann, day);
        await db.SaveChangesAsync();
        db.Add(new RosterEntry(companyId, ann.Id, new DateOnly(2026, 5, 4), day.Id, true));
        await db.SaveChangesAsync();

        IAttendanceService attendance = scope.ServiceProvider.GetRequiredService<IAttendanceService>();
        const string export = "Badge,Punch\nCK-1,2026-05-04 08:20\nCK-1,2026-05-04 18:30\nCK-1,2026-05-05 07:55\nNOBODY,2026-05-04 08:00\nnonsense\n";
        ClockImport first = (await attendance.ImportClockAsync(companyId, export)).Value;
        (first.DaysRecorded, first.AlreadyRecorded).Should().Be((2, 0));
        first.UnknownBadges.Should().Equal("NOBODY");
        first.Problems.Should().ContainSingle();

        AttendanceRecord monday = await db.AttendanceRecords.AsNoTracking().SingleAsync(a => a.EmployeeId == ann.Id && a.Date == new DateOnly(2026, 5, 4));
        (monday.ClockIn, monday.ClockOut, monday.LateMinutes, monday.OvertimeHours, monday.OvertimeMultiplier)
            .Should().Be((new TimeOnly(8, 20), new TimeOnly(18, 30), 20, 1.17m, 1.5m), "20 minutes late, past the 5 of grace; 10h10 against a nine-hour shift");

        (await attendance.ImportClockAsync(companyId, export)).Value.Should().Match<ClockImport>(i => i.DaysRecorded == 0 && i.AlreadyRecorded == 2);
        (await attendance.ImportClockAsync(companyId, "junk")).Error.Code.Should().Be("Hr.ClockEmpty");
    }
}
