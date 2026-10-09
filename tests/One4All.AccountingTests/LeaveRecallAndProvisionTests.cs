using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Payroll;
using One4All.Core.Accounting;
using One4All.Core.Payroll;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the leave calendar, cancelling and recalling leave (the days not taken go back to the balance), and the bonus and gratuity
/// provisions trued up to what active employees have earned.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class LeaveRecallAndProvisionTests
{
    private readonly AccountingFixture _fixture;

    public LeaveRecallAndProvisionTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Leave_is_recalled_onto_the_calendar_and_provisions_are_trued_up()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.AddRange(new LedgerAccount(companyId, "6190", "Bonus Expense", AccountType.Expense), new LedgerAccount(companyId, "2490", "Bonus Provision", AccountType.Liability));
        Employee ann = new(companyId, "LR-1", "Ann", 3_650m, new DateOnly(2020, 1, 1)) { Department = "Works" }, ben = new(companyId, "LR-2", "Ben", 3_650m, new DateOnly(2026, 3, 2)) { Department = "Admin" };
        db.Employees.AddRange(ann, ben);
        await db.SaveChangesAsync();
        ILeaveService leave = scope.ServiceProvider.GetRequiredService<ILeaveService>();
        IAttendanceService attendance = scope.ServiceProvider.GetRequiredService<IAttendanceService>();
        (await leave.AccrueAsync(companyId, ann.Id, "ANNUAL", 20m)).IsSuccess.Should().BeTrue();
        (await leave.AccrueAsync(companyId, ben.Id, "ANNUAL", 20m)).IsSuccess.Should().BeTrue();
        long annLeave = (await attendance.TakeLeaveAsync(companyId, ann.Id, "ANNUAL", new DateOnly(2026, 4, 6), new DateOnly(2026, 4, 15), 8m)).Value;
        long benLeave = (await attendance.TakeLeaveAsync(companyId, ben.Id, "ANNUAL", new DateOnly(2026, 5, 4), new DateOnly(2026, 5, 8), 5m)).Value;

        (await attendance.LeaveCalendarAsync(companyId, new DateOnly(2026, 4, 1), new DateOnly(2026, 5, 31))).Select(e => e.EmployeeNumber).Should().Equal("LR-1", "LR-2");
        (await attendance.LeaveCalendarAsync(companyId, new DateOnly(2026, 4, 1), new DateOnly(2026, 5, 31), "Admin")).Should().ContainSingle(e => e.EmployeeNumber == "LR-2");

        // Ann is recalled on the 11th: 5 of the 10 calendar days are left, so 4 of her 8 days go back; Ben's leave is cancelled before it starts.
        (await attendance.CancelLeaveAsync(companyId, annLeave, new DateOnly(2026, 4, 11))).Value.Should().Be(4m);
        (await attendance.CancelLeaveAsync(companyId, annLeave, new DateOnly(2026, 4, 20))).Error.Code.Should().Be("Hr.LeaveOver");
        (await attendance.CancelLeaveAsync(companyId, benLeave, null)).Value.Should().Be(5m);
        (await attendance.CancelLeaveAsync(companyId, benLeave, null)).Error.Code.Should().Be("Hr.LeaveNotFound");
        LeaveRecord recalled = await db.LeaveRecords.AsNoTracking().SingleAsync(l => l.Id == annLeave);
        (recalled.ToDate, recalled.Days).Should().Be((new DateOnly(2026, 4, 10), 4m));
        (await db.Set<EmployeeLeaveBalance>().AsNoTracking().Where(b => b.CompanyId == companyId).Select(b => b.AccruedDays - b.TakenDays).ToListAsync()).Should().BeEquivalentTo([16m, 20m]);
        (await attendance.LeaveCalendarAsync(companyId, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31))).Should().BeEmpty("Ben's leave was cancelled");
        (await attendance.TakeLeaveAsync(companyId, ben.Id, "ANNUAL", new DateOnly(2026, 5, 4), new DateOnly(2026, 5, 8), 5m)).IsSuccess.Should().BeTrue("the cancelled dates are free again");

        // The 13th cheque to the end of March: Ann 90 of 365 days, Ben (joined 2 March) 30 days, of a month's pay.
        PayrollProvisionRequest Bonus(DateOnly date, string document) => new()
        {
            CompanyId = companyId, Kind = PayrollProvisionKind.Bonus, Date = date, YearStart = new DateOnly(2026, 1, 1), ExpenseAccountCode = "6190", ProvisionAccountCode = "2490", DocumentNumber = document,
        };
        (await leave.RaisePayrollProvisionAsync(Bonus(new DateOnly(2026, 3, 31), "BON-03"))).Value.Should().Be(1_200m);
        (await leave.RaisePayrollProvisionAsync(Bonus(new DateOnly(2026, 4, 30), "BON-04"))).Value.Should().Be(1_200m + 2 * 300m, "April adds 30 days each");
        decimal Provision() => db.Journals.AsNoTracking().Where(j => j.CompanyId == companyId).SelectMany(j => j.Lines).Where(l => l.AccountCode == "2490").Sum(l => l.BaseCredit - l.BaseDebit);
        Provision().Should().Be(1_800m);
        (await leave.RaisePayrollProvisionAsync(new PayrollProvisionRequest { CompanyId = companyId, Kind = PayrollProvisionKind.Gratuity, Date = new DateOnly(2026, 4, 30) }))
            .Error.Code.Should().Be("Pay.Provision");
    }
}
