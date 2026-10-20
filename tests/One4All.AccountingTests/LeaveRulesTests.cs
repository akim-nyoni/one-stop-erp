using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Payroll;
using One4All.Core.Payroll;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the leave rules: a balance ceiling stops accrual, carried-over days lapse if not taken in time (taking leave uses them first), leave
/// can be taken in advance only as far as the policy allows (and never paid out), and sick pay tiers turn sick days into a pay deduction.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class LeaveRulesTests
{
    private readonly AccountingFixture _fixture;

    public LeaveRulesTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Ceiling_lapse_advance_and_sick_tiers()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Employee clerk = new(companyId, "LR-1", "Leave Clerk", 2170m, new DateOnly(2015, 6, 1));
        db.Employees.Add(clerk);
        db.PayElements.Add(new PayElement(companyId, "SICKDED", "Unpaid sick leave", PayElementType.Deduction, CalculationBasis.FixedAmount, 5, "6100"));
        await db.SaveChangesAsync();

        ILeavePolicyService policies = scope.ServiceProvider.GetRequiredService<ILeavePolicyService>();
        ILeaveService leave = scope.ServiceProvider.GetRequiredService<ILeaveService>();
        (await policies.SavePolicyAsync(new LeavePolicyRequest(companyId, "ANNUAL", 24m, true, 1, null, true, [], CarryOverExpiryMonths: 13))).Error.Code.Should().Be("Pay.LeaveRules");
        (await policies.SavePolicyAsync(new LeavePolicyRequest(companyId, "ANNUAL", 24m, true, 1, null, true, [], MaxAccumulationDays: 10m, CarryOverExpiryMonths: 3))).IsSuccess.Should().BeTrue();

        // Ceiling 10: from 9, November earns only 1 of its 2; December and January earn nothing (2 + 2 forfeited).
        (await leave.AccrueAsync(companyId, clerk.Id, "ANNUAL", 9m)).IsSuccess.Should().BeTrue();
        (await policies.RunMonthAsync(companyId, new DateOnly(2026, 11, 30))).Value.DaysForfeited.Should().Be(1m);
        (await policies.RunMonthAsync(companyId, new DateOnly(2026, 12, 31))).Value.DaysForfeited.Should().Be(2m);
        (await policies.RunMonthAsync(companyId, new DateOnly(2027, 1, 31))).Value.DaysForfeited.Should().Be(2m);

        // January carried 10, usable until 1 April; 4 taken come off the carried days first, leaving 6 carried.
        (await leave.TakeAsync(companyId, clerk.Id, "ANNUAL", 4m)).Value.Should().Be(6m);
        (await policies.RunMonthAsync(companyId, new DateOnly(2027, 2, 28))).Value.DaysAccrued.Should().Be(2m);
        (await policies.RunMonthAsync(companyId, new DateOnly(2027, 3, 31))).Value.DaysAccrued.Should().Be(2m);

        // April: the 6 carried days not taken lapse, then April earns its 2.
        (await policies.RunMonthAsync(companyId, new DateOnly(2027, 4, 30))).Value.DaysForfeited.Should().Be(6m);
        EmployeeLeaveBalance annual = await db.Set<EmployeeLeaveBalance>().AsNoTracking().SingleAsync(b => b.EmployeeId == clerk.Id && b.LeaveType == "ANNUAL");
        (annual.BalanceDays, annual.ForfeitedDays, annual.CarriedOverDays, annual.CarryExpiresOn).Should().Be((6m, 11m, 0m, (DateOnly?)null));

        // Study leave may run 3 days into advance, but an advance cannot be paid out.
        (await policies.SavePolicyAsync(new LeavePolicyRequest(companyId, "STUDY", 0m, true, 1, null, true, [], NegativeBalanceDays: 3m))).IsSuccess.Should().BeTrue();
        (await leave.TakeAsync(companyId, clerk.Id, "STUDY", 2m)).Value.Should().Be(-2m);
        (await leave.TakeAsync(companyId, clerk.Id, "STUDY", 2m)).Error.Code.Should().Be("Pay.InsufficientLeave");
        IAttendanceService attendance = scope.ServiceProvider.GetRequiredService<IAttendanceService>();
        (await attendance.EncashLeaveAsync(companyId, clerk.Id, "STUDY", new DateOnly(2026, 10, 1), 1m, 100m)).Error.Code.Should().Be("Hr.EncashAdvance");
        db.ChangeTracker.Clear();
        (await db.Set<EmployeeLeaveBalance>().AsNoTracking().Where(b => b.EmployeeId == clerk.Id && b.LeaveType == "STUDY").Select(b => b.TakenDays).SingleAsync()).Should().Be(2m);

        // Sick tiers: days 1–5 full pay, 6–10 half, from 11 none. 4 days in September, 8 in October: October's are days 5–12 of the year,
        // so 1 on full pay, 5 at half and 2 unpaid → 4.5 unpaid days at 2,170 ÷ 21.7 = 450 deducted.
        (await policies.SavePolicyAsync(new LeavePolicyRequest(companyId, "SICK", 0m, true, 1, null, true, [], SickTiers: [(6, 50m), (11, 0m)]))).IsSuccess.Should().BeTrue();
        (await leave.AccrueAsync(companyId, clerk.Id, "SICK", 30m)).IsSuccess.Should().BeTrue();
        (await attendance.TakeLeaveAsync(companyId, clerk.Id, "SICK", new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 10), 4m)).IsSuccess.Should().BeTrue();
        (await attendance.TakeLeaveAsync(companyId, clerk.Id, "SICK", new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 14), 8m)).IsSuccess.Should().BeTrue();
        SickPayLine sick = (await policies.ApplySickPayAsync(companyId, "SICK", "2026-10", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), "SICKDED", 21.7m)).Value.Should().ContainSingle().Subject;
        (sick.SickDays, sick.UnpaidDays, sick.Deduction).Should().Be((8m, 4.5m, 450m));
        (await db.PayrollVariables.AsNoTracking().Where(v => v.EmployeeId == clerk.Id && v.ElementCode == "SICKDED" && v.PeriodLabel == "2026-10").Select(v => v.Amount).SingleAsync())
            .Should().Be(450m);

        // A rolling three-year sick cycle: January's 3 days follow the 12 already taken (not the first of a new leave year), so all 3 are unpaid.
        (await policies.SavePolicyAsync(new LeavePolicyRequest(companyId, "SICK", 0m, true, 1, null, true, [], SickTiers: [(6, 50m), (11, 0m)]) { SickCycleMonths = 36 }))
            .IsSuccess.Should().BeTrue();
        (await attendance.TakeLeaveAsync(companyId, clerk.Id, "SICK", new DateOnly(2027, 1, 11), new DateOnly(2027, 1, 13), 3m)).IsSuccess.Should().BeTrue();
        (await policies.ApplySickPayAsync(companyId, "SICK", "2027-01", new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 31), "SICKDED", 21.7m)).Value.Single()
            .Should().Be(new SickPayLine(clerk.Id, 3m, 3m, 300m));

        // Leave pay in advance: 5 annual days from 2 November at 2,170 ÷ 21.7 = 500, paid in October and recovered in November.
        db.PayElements.AddRange(
            new PayElement(companyId, "LEAVEPAY", "Leave pay", PayElementType.Earning, CalculationBasis.FixedAmount, 15, "6100"),
            new PayElement(companyId, "LEAVEADV", "Leave pay advance recovery", PayElementType.Deduction, CalculationBasis.FixedAmount, 62, "6100"));
        await db.SaveChangesAsync();
        (await attendance.TakeLeaveAsync(companyId, clerk.Id, "ANNUAL", new DateOnly(2027, 5, 3), new DateOnly(2027, 5, 7), 5m)).IsSuccess.Should().BeTrue();
        (await policies.AdvanceLeavePayAsync(companyId, "ANNUAL", new DateOnly(2027, 5, 1), new DateOnly(2027, 5, 31), "2027-04", "2027-04", "LEAVEPAY", "LEAVEADV", 21.7m))
            .Error.Code.Should().Be("Pay.LeaveAdvance");
        LeaveAdvanceLine advance = (await policies.AdvanceLeavePayAsync(companyId, "ANNUAL", new DateOnly(2027, 5, 1), new DateOnly(2027, 5, 31), "2027-04", "2027-05", "LEAVEPAY", "LEAVEADV", 21.7m))
            .Value.Should().ContainSingle().Subject;
        (advance.LeaveDays, advance.Amount).Should().Be((5m, 500m));
        List<PayrollVariable> advanceVariables = await db.PayrollVariables.AsNoTracking().Where(v => v.EmployeeId == clerk.Id && (v.ElementCode == "LEAVEPAY" || v.ElementCode == "LEAVEADV"))
            .OrderBy(v => v.PeriodLabel).ToListAsync();
        advanceVariables.Select(v => (v.ElementCode, v.PeriodLabel, v.Amount)).Should().Equal(("LEAVEPAY", "2027-04", (decimal?)500m), ("LEAVEADV", "2027-05", (decimal?)500m));
    }
}
