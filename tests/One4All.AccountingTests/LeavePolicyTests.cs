using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Payroll;
using One4All.Core.Payroll;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the leave policy run: monthly accrual with a service band and a pro-rata joiner, a month run twice accrues once, and as the leave
/// year turns only the carry-over cap is kept (the excess forfeited) before the new month's accrual.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class LeavePolicyTests
{
    private readonly AccountingFixture _fixture;

    public LeavePolicyTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Policies_accrue_monthly_once_and_cap_the_carry_over()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Employee senior = new(companyId, "LP-1", "Senior Clerk", 2000m, new DateOnly(2015, 6, 1));
        Employee joiner = new(companyId, "LP-2", "New Clerk", 1500m, new DateOnly(2026, 11, 16));
        db.Employees.AddRange(senior, joiner);
        await db.SaveChangesAsync();
        ILeavePolicyService policies = scope.ServiceProvider.GetRequiredService<ILeavePolicyService>();
        (await policies.RunMonthAsync(companyId, new DateOnly(2026, 11, 30))).Error.Code.Should().Be("Pay.NoLeavePolicy");
        (await policies.SavePolicyAsync(new LeavePolicyRequest(companyId, "annual", 18m, true, 1, 5m, true, [(0, 1m)]))).Error.Code.Should().Be("Pay.LeavePolicy");
        (await policies.SavePolicyAsync(new LeavePolicyRequest(companyId, "annual", 18m, true, 1, 5m, true, [(10, 24m)]))).IsSuccess.Should().BeTrue();

        // November: the senior earns 2 (24 a year after 10 years); the joiner from the 16th earns 15/30 of 1.5.
        LeaveAccrualRun november = (await policies.RunMonthAsync(companyId, new DateOnly(2026, 11, 30))).Value;
        (november.Accrued, november.DaysAccrued).Should().Be((2, 2.75m));
        (await policies.RunMonthAsync(companyId, new DateOnly(2026, 11, 30))).Value.AlreadyRun.Should().Be(2);
        (await policies.RunMonthAsync(companyId, new DateOnly(2026, 12, 31))).Value.DaysAccrued.Should().Be(3.5m);

        // The senior also had 12 days from before: January keeps only 5 of their 16 and then earns 2; the joiner's 2.25 is under the cap.
        EmployeeLeaveBalance seniorBalance = await db.Set<EmployeeLeaveBalance>().SingleAsync(b => b.EmployeeId == senior.Id);
        seniorBalance.Accrue(12m);
        await db.SaveChangesAsync();
        LeaveAccrualRun january = (await policies.RunMonthAsync(companyId, new DateOnly(2027, 1, 31))).Value;
        january.DaysForfeited.Should().Be(11m);
        (await db.Set<EmployeeLeaveBalance>().AsNoTracking().Where(b => b.CompanyId == companyId).OrderBy(b => b.EmployeeId).Select(b => new { b.AccruedDays, b.ForfeitedDays }).ToListAsync())
            .Select(b => (b.AccruedDays, b.ForfeitedDays)).Should().Equal((7m, 11m), (3.75m, 0m));
    }
}
