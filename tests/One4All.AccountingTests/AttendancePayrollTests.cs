using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Payroll;
using One4All.Core.Payroll;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Fourteenth audit #12: approved overtime and unpaid days reach the pay period as variables; unapproved overtime and covered absences do not.</summary>
[Collection(AccountingCollection.Name)]
public sealed class AttendancePayrollTests
{
    private readonly AccountingFixture _fixture;

    public AttendancePayrollTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Approved_overtime_and_unpaid_days_go_to_the_pay_period()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.PayElements.AddRange(
            new PayElement(companyId, "OVERTIME", "Overtime", PayElementType.Earning, CalculationBasis.RatePerUnit, 2, "6100") { Rate = 15m, Taxable = true },
            new PayElement(companyId, "OVERTIME2", "Double time", PayElementType.Earning, CalculationBasis.RatePerUnit, 3, "6100") { Rate = 20m, Taxable = true },
            new PayElement(companyId, "UNPAIDLV", "Unpaid days", PayElementType.Deduction, CalculationBasis.FixedAmount, 40, "6100"));
        Employee ann = new(companyId, "AP-1", "Ann Payne", 2_167m, new DateOnly(2025, 1, 1));
        db.Employees.Add(ann);
        await db.SaveChangesAsync();

        IAttendanceService attendance = scope.ServiceProvider.GetRequiredService<IAttendanceService>();
        async Task<long> Day(int day, AttendanceStatus status, decimal overtime = 0m, decimal multiplier = 1.5m)
            => (await attendance.RecordDayAsync(companyId, ann.Id, new DateOnly(2026, 3, day), status, null, null, 0, overtime, multiplier)).Value;
        (await attendance.ApproveOvertimeAsync(companyId, await Day(2, AttendanceStatus.Present, 3m), 1)).IsSuccess.Should().BeTrue();
        (await attendance.ApproveOvertimeAsync(companyId, await Day(3, AttendanceStatus.Present, 2m, 2m), 1)).IsSuccess.Should().BeTrue();
        await Day(4, AttendanceStatus.Present, 4m);
        await Day(5, AttendanceStatus.Absent);
        await Day(6, AttendanceStatus.Absent);
        db.LeaveRecords.AddRange(
            new LeaveRecord(companyId, ann.Id, "SICK", LeaveRecordKind.Taken, new DateOnly(2026, 3, 6), new DateOnly(2026, 3, 6), 1m, 0m),
            new LeaveRecord(companyId, ann.Id, "Unpaid", LeaveRecordKind.Taken, new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 11), 2m, 0m));
        await db.SaveChangesAsync();

        // 3 hours at time and a half and 2 at double time approved, 4 waiting; 1 absence no leave covers plus 2 days unpaid leave = 3 days at 100 a day.
        ILeavePolicyService policies = scope.ServiceProvider.GetRequiredService<ILeavePolicyService>();
        AttendancePayLine line = (await policies.ApplyAttendanceAsync(companyId, "2026-03", new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), "UNPAIDLV", 21.67m)).Value.Single();
        (line.OvertimeHours, line.DoubleTimeHours, line.UnapprovedHours, line.UnpaidDays, line.Deduction).Should().Be((3m, 2m, 4m, 3m, 300m));
        Dictionary<string, PayrollVariable> sent = await db.PayrollVariables.AsNoTracking().Where(v => v.CompanyId == companyId && v.PeriodLabel == "2026-03")
            .ToDictionaryAsync(v => v.ElementCode);
        (sent["OVERTIME"].Units, sent["OVERTIME2"].Units, sent["UNPAIDLV"].Amount).Should().Be((3m, 2m, 300m));

        // Sending again replaces rather than adds.
        (await policies.ApplyAttendanceAsync(companyId, "2026-03", new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), "UNPAIDLV", 21.67m)).IsSuccess.Should().BeTrue();
        (await db.PayrollVariables.CountAsync(v => v.CompanyId == companyId && v.PeriodLabel == "2026-03")).Should().Be(3);
    }
}
