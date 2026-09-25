using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Payroll;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Payroll;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class LeaveProvisionTests
{
    private readonly AccountingFixture _fixture;

    public LeaveProvisionTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Leave_provision_trues_up_to_the_value_of_outstanding_leave()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long empId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Employee emp = new(companyId, "LP-1", "Provision Person", 3_000m, new DateOnly(2025, 1, 1));
            db.Set<Employee>().Add(emp);
            await db.SaveChangesAsync();
            empId = emp.Id;

            ILeaveService leave = scope.ServiceProvider.GetRequiredService<ILeaveService>();
            await leave.AccrueAsync(companyId, empId, "ANNUAL", 10m); // 10 days outstanding
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ILeaveService leave = scope.ServiceProvider.GetRequiredService<ILeaveService>();
            // First true-up at 100/day → provision 1000.
            (await leave.RaiseLeaveProvisionAsync(companyId, new DateOnly(2026, 3, 31), 100m, "6600", "2460", "PROV-1")).Value.Should().Be(1000m);

            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            var tb1 = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 3, 31));
            tb1.Rows.Should().Contain(r => r.AccountCode == "2460" && r.Credit == 1000m);
            tb1.Rows.Should().Contain(r => r.AccountCode == "6600" && r.Debit == 1000m);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            // Two more days accrued → 12 outstanding; re-run only posts the +200 movement.
            ILeaveService leave = scope.ServiceProvider.GetRequiredService<ILeaveService>();
            await leave.AccrueAsync(companyId, empId, "ANNUAL", 2m);
            (await leave.RaiseLeaveProvisionAsync(companyId, new DateOnly(2026, 4, 30), 100m, "6600", "2460", "PROV-2")).Value.Should().Be(1200m);

            // Nil change re-run posts nothing (still 1200).
            (await leave.RaiseLeaveProvisionAsync(companyId, new DateOnly(2026, 4, 30), 100m, "6600", "2460", "PROV-3")).Value.Should().Be(1200m);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            var tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 4, 30));
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Should().Contain(r => r.AccountCode == "2460" && r.Credit == 1200m); // trued up to 12 days × 100
            tb.Rows.Should().Contain(r => r.AccountCode == "6600" && r.Debit == 1200m);
        }
    }
}
