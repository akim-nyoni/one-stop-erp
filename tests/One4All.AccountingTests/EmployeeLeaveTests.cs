using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Payroll;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Payroll;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class EmployeeLeaveTests
{
    private readonly AccountingFixture _fixture;

    public EmployeeLeaveTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Leave_accrues_and_is_taken_with_the_balance_guarded_and_reported()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long employeeId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Employee emp = new(companyId, "LV-1", "Leave Taker", 3_000m, new DateOnly(2025, 1, 1));
            db.Set<Employee>().Add(emp);
            await db.SaveChangesAsync();
            employeeId = emp.Id;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ILeaveService leave = scope.ServiceProvider.GetRequiredService<ILeaveService>();

            // Accrue 1.75 days per month for a few months → 21 days.
            for (int i = 0; i < 12; i++)
            {
                (await leave.AccrueAsync(companyId, employeeId, "annual", 1.75m)).IsSuccess.Should().BeTrue();
            }

            (await leave.TakeAsync(companyId, employeeId, "ANNUAL", 5m)).Value.Should().Be(16m); // 21 − 5

            // Cannot take more than the balance.
            (await leave.TakeAsync(companyId, employeeId, "ANNUAL", 100m)).IsFailure.Should().BeTrue();

            // A non-positive accrual/take is refused.
            (await leave.AccrueAsync(companyId, employeeId, "ANNUAL", 0m)).IsFailure.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            EmployeeLeaveBalance bal = db.Set<EmployeeLeaveBalance>().Single(b => b.EmployeeId == employeeId && b.LeaveType == "ANNUAL");
            bal.AccruedDays.Should().Be(21m);
            bal.TakenDays.Should().Be(5m);
            bal.BalanceDays.Should().Be(16m);

            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            string leave = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "PAY-LEAVE", new Dictionary<string, object?>(), ReportFormat.Html)).Value.Content);
            leave.Should().Contain("Leave Taker").And.Contain("ANNUAL").And.Contain("16.00");
        }
    }
}
