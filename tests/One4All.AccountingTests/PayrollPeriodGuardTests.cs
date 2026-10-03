using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Payroll;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Payroll;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the payroll period guard: a period is paid once (a second run is refused and posts nothing), a
/// blank period is refused, and the next period runs normally.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PayrollPeriodGuardTests
{
    private readonly AccountingFixture _fixture;

    public PayrollPeriodGuardTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_payroll_period_cannot_be_paid_twice()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            PayElement basic = new(companyId, "PG-BASIC", "Basic Salary", PayElementType.Earning, CalculationBasis.Formula, 1, "6100") { Formula = "BASIC", Taxable = true };
            db.PayElements.Add(basic);
            await db.SaveChangesAsync();

            Employee emp = new(companyId, "PG-001", "Guarded Worker", 1500m, new DateOnly(2025, 1, 1));
            emp.AssignPayElement(basic.Id);
            db.Employees.Add(emp);
            await db.SaveChangesAsync();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPayrollService payroll = scope.ServiceProvider.GetRequiredService<IPayrollService>();

            (await payroll.RunAsync(companyId, "  ", new DateOnly(2026, 1, 25), "2420", "PG-X")).Error.Code.Should().Be("Payroll.PeriodRequired");

            var first = await payroll.RunAsync(companyId, "2026-01", new DateOnly(2026, 1, 25), "2420", "PG-2026-01");
            first.IsSuccess.Should().BeTrue(first.IsFailure ? first.Error.ToString() : null);
            first.Value.TotalNet.Should().Be(1500m);

            // Same period again — including with stray whitespace — is refused.
            (await payroll.RunAsync(companyId, "2026-01", new DateOnly(2026, 1, 26), "2420", "PG-2026-01B")).Error.Code.Should().Be("Payroll.PeriodAlreadyRun");
            (await payroll.RunAsync(companyId, " 2026-01 ", new DateOnly(2026, 1, 26), "2420", "PG-2026-01C")).Error.Code.Should().Be("Payroll.PeriodAlreadyRun");

            // The next period runs.
            (await payroll.RunAsync(companyId, "2026-02", new DateOnly(2026, 2, 25), "2420", "PG-2026-02")).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            (await db.Set<Payslip>().CountAsync(p => p.CompanyId == companyId)).Should().Be(2);

            // Net pay control carries two months, not three.
            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 2, 28));
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Should().Contain(r => r.AccountCode == "2420" && r.Credit == 3000m);
        }
    }
}
