using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Payroll;
using One4All.Core.Payroll;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers shift rosters: a rotating pattern generates the roster from its anchor, a day changed by hand survives regeneration, lateness is measured
/// against the rostered start (across midnight, within the grace), and night-shift allowances are paid only for rostered shifts worked.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ShiftRosterTests
{
    private readonly AccountingFixture _fixture;

    public ShiftRosterTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Patterns_generate_the_roster_and_drive_lateness_and_allowances()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Employee guard = new(companyId, "SR-E1", "Night Guard", 2_000m, new DateOnly(2025, 1, 1));
        db.Employees.Add(guard);
        db.PayElements.Add(new PayElement(companyId, "SHIFTALL", "Shift allowance", PayElementType.Earning, CalculationBasis.FixedAmount, 5, "6100"));
        await db.SaveChangesAsync();

        IRosterService roster = scope.ServiceProvider.GetRequiredService<IRosterService>();
        (await roster.SaveShiftAsync(companyId, new ShiftRequest("D", "Day", new TimeOnly(6, 0), new TimeOnly(14, 0), GraceMinutes: 5))).IsSuccess.Should().BeTrue();
        (await roster.SaveShiftAsync(companyId, new ShiftRequest("N", "Night", new TimeOnly(22, 0), new TimeOnly(6, 0), Allowance: 15m))).IsSuccess.Should().BeTrue();
        (await roster.SavePatternAsync(companyId, "BAD", "D,Q")).Error.Code.Should().Be("Hr.ShiftPattern");
        (await roster.SavePatternAsync(companyId, "ROT", "d, d, n, n, off, off")).IsSuccess.Should().BeTrue();
        (await roster.AssignAsync(companyId, guard.Id, "ROT", new DateOnly(2026, 9, 1))).IsSuccess.Should().BeTrue();

        DateOnly from = new(2026, 9, 1), to = new(2026, 9, 12);
        (await roster.GenerateAsync(companyId, from, to)).Value.Should().Be(12);
        (await roster.SetDayAsync(companyId, guard.Id, new DateOnly(2026, 9, 5), "N")).IsSuccess.Should().BeTrue();
        (await roster.GenerateAsync(companyId, from, to)).Value.Should().Be(11);
        (await roster.RosterAsync(companyId, from, to)).Select(d => d.Shift).Should().Equal("D", "D", "N", "N", "N", "OFF", "D", "D", "N", "N", "OFF", "OFF");

        IAttendanceService attendance = scope.ServiceProvider.GetRequiredService<IAttendanceService>();
        async Task<long> Day(int day, AttendanceStatus status, TimeOnly? clockIn)
            => (await attendance.RecordDayAsync(companyId, guard.Id, new DateOnly(2026, 9, day), status, clockIn, null, 0, 0m, 1m)).Value;
        long early = await Day(1, AttendanceStatus.Present, new TimeOnly(6, 4));          // within the 5 minutes' grace
        long late = await Day(3, AttendanceStatus.Present, new TimeOnly(22, 20));         // 20 minutes late
        long pastMidnight = await Day(4, AttendanceStatus.Present, new TimeOnly(0, 10));  // 2h10 late for a 22:00 start
        await Day(5, AttendanceStatus.Present, new TimeOnly(21, 55));                      // covering, early
        await Day(9, AttendanceStatus.Absent, null);
        await Day(10, AttendanceStatus.Present, new TimeOnly(22, 0));
        Dictionary<long, int> lateness = await db.AttendanceRecords.AsNoTracking().Where(a => a.EmployeeId == guard.Id).ToDictionaryAsync(a => a.Id, a => a.LateMinutes);
        (lateness[early], lateness[late], lateness[pastMidnight]).Should().Be((0, 20, 130));

        // Night shifts worked on the 3rd, 4th, 5th and 10th (not the 9th): 4 × 15.
        ShiftAllowanceLine allowance = (await roster.ApplyShiftAllowancesAsync(companyId, "2026-09", from, to, "SHIFTALL")).Value.Should().ContainSingle().Subject;
        (allowance.ShiftsWorked, allowance.Amount).Should().Be((4, 60m));
        (await db.PayrollVariables.AsNoTracking().Where(v => v.EmployeeId == guard.Id && v.ElementCode == "SHIFTALL").Select(v => v.Amount).SingleAsync()).Should().Be(60m);
    }
}
