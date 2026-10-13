using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Payroll;
using One4All.Core.Payroll;
using One4All.Core.Security;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers department cover and time off in lieu: with at most one of a department away at once, a second colleague's overlapping application is
/// refused while one in another week is accepted; twelve overtime hours credit a day and a half of TOIL; grade days are saved and validated.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class LeaveCoverAndToilTests
{
    private readonly AccountingFixture _fixture;

    public LeaveCoverAndToilTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Departments_keep_cover_and_overtime_earns_time_off()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        User ann = new($"lc-ann-{companyId}", "Ann"), ben = new($"lc-ben-{companyId}", "Ben");
        db.Users.AddRange(ann, ben);
        await db.SaveChangesAsync();
        Employee first = new(companyId, $"LC1-{companyId % 100000}", "Ann", 1_000m, new DateOnly(2024, 1, 1)) { UserId = ann.Id, Department = "STORES" };
        Employee second = new(companyId, $"LC2-{companyId % 100000}", "Ben", 1_000m, new DateOnly(2024, 1, 1)) { UserId = ben.Id, Department = "STORES" };
        db.Employees.AddRange(first, second);
        await db.SaveChangesAsync();

        ILeavePolicyService policies = scope.ServiceProvider.GetRequiredService<ILeavePolicyService>();
        LeavePolicyRequest Policy(string? gradeDays, int? maxAway) => new(companyId, "ANNUAL", 20m, true, 1, null, true, []) { GradeDays = gradeDays, MaxAwayPerDepartment = maxAway };
        (await policies.SavePolicyAsync(Policy("B1 twenty", 1))).Error.Code.Should().Be("Pay.LeaveGradeDays");
        (await policies.SavePolicyAsync(Policy("B1=24", 0))).Error.Code.Should().Be("Pay.LeaveCover");
        (await policies.SavePolicyAsync(Policy("B1=24", 1))).IsSuccess.Should().BeTrue();

        ILeaveService leave = scope.ServiceProvider.GetRequiredService<ILeaveService>();
        (await leave.AccrueAsync(companyId, first.Id, "ANNUAL", 10m)).IsSuccess.Should().BeTrue();
        (await leave.AccrueAsync(companyId, second.Id, "ANNUAL", 10m)).IsSuccess.Should().BeTrue();

        IEmployeeSelfService ess = scope.ServiceProvider.GetRequiredService<IEmployeeSelfService>();
        (await ess.ApplyForLeaveAsync(ann.Id, "ANNUAL", new DateOnly(2026, 5, 4), new DateOnly(2026, 5, 8), 5m)).IsSuccess.Should().BeTrue();
        (await ess.ApplyForLeaveAsync(ben.Id, "ANNUAL", new DateOnly(2026, 5, 7), new DateOnly(2026, 5, 12), 4m)).Error.Code.Should().Be("Ess.LeaveCover");
        (await ess.ApplyForLeaveAsync(ben.Id, "ANNUAL", new DateOnly(2026, 5, 11), new DateOnly(2026, 5, 15), 5m)).IsSuccess.Should().BeTrue();

        (await policies.CreditTimeOffAsync(companyId, first.EmployeeNumber, 12m, 8m)).Value.Should().Be(1.5m);
        (await policies.CreditTimeOffAsync(companyId, first.EmployeeNumber, 4m, 8m, 1.5m)).Value.Should().Be(0.75m);
        (await policies.CreditTimeOffAsync(companyId, "NOBODY", 4m, 8m)).Error.Code.Should().Be("Pay.EmployeeNotFound");
        (await db.Set<EmployeeLeaveBalance>().AsNoTracking().SingleAsync(b => b.EmployeeId == first.Id && b.LeaveType == "TOIL")).BalanceDays.Should().Be(2.25m);
    }
}
