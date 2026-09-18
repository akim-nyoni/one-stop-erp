using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Payroll;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Payroll;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class PayrollTests
{
    private readonly AccountingFixture _fixture;

    public PayrollTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Formula_driven_run_pays_net_and_posts_balanced_journal()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long employeeId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);

            // Pay elements (formula-driven; order matters: pension before tax).
            PayElement basic = new(companyId, "BASIC", "Basic Salary", PayElementType.Earning, CalculationBasis.Formula, 1, "6100") { Formula = "BASIC", Taxable = true };
            PayElement housing = new(companyId, "HOUSING", "Housing Allowance", PayElementType.Earning, CalculationBasis.PercentOfBasic, 2, "6100") { Rate = 20m, Taxable = true };
            PayElement pension = new(companyId, "PENSION", "Pension (5% of basic)", PayElementType.Deduction, CalculationBasis.PercentOfBasic, 3, "2410") { Rate = 5m };
            // PAYE 20% of (taxable less pension relief) above a 1,000 threshold.
            PayElement paye = new(companyId, "PAYE", "PAYE", PayElementType.Deduction, CalculationBasis.Formula, 4, "2400") { Formula = "TAXABLE - PENSION > 1000 ? (TAXABLE - PENSION - 1000) * 20 / 100 : 0" };
            db.PayElements.AddRange(basic, housing, pension, paye);
            await db.SaveChangesAsync();

            Employee emp = new(companyId, "EMP-001", "Alice Worker", 2000m, new DateOnly(2025, 1, 1));
            emp.AssignPayElement(basic.Id);
            emp.AssignPayElement(housing.Id);
            emp.AssignPayElement(pension.Id);
            emp.AssignPayElement(paye.Id);
            db.Employees.Add(emp);
            await db.SaveChangesAsync();
            employeeId = emp.Id;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPayrollService payroll = scope.ServiceProvider.GetRequiredService<IPayrollService>();
            var calc = await payroll.CalculateAsync(companyId, employeeId);
            calc.IsSuccess.Should().BeTrue(calc.IsFailure ? calc.Error.ToString() : null);
            // Gross = 2000 + 400 housing = 2400. Pension = 100. Taxable-relief = 2400-100 = 2300.
            // PAYE = (2300-1000)*20% = 260. Deductions = 360. Net = 2040.
            calc.Value.Gross.Should().Be(2400m);
            calc.Value.Deductions.Should().Be(360m);
            calc.Value.Net.Should().Be(2040m);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPayrollService payroll = scope.ServiceProvider.GetRequiredService<IPayrollService>();
            var run = await payroll.RunAsync(companyId, "2026-01", new DateOnly(2026, 1, 25), "2420", "PR-2026-01");
            run.IsSuccess.Should().BeTrue(run.IsFailure ? run.Error.ToString() : null);
            run.Value.PayslipCount.Should().Be(1);
            run.Value.TotalNet.Should().Be(2040m);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 1, 31));
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Should().Contain(r => r.AccountCode == "6100" && r.Debit == 2400m);   // gross wages expense
            tb.Rows.Should().Contain(r => r.AccountCode == "2400" && r.Credit == 260m);   // PAYE payable
            tb.Rows.Should().Contain(r => r.AccountCode == "2410" && r.Credit == 100m);   // pension payable
            tb.Rows.Should().Contain(r => r.AccountCode == "2420" && r.Credit == 2040m);  // net pay control
        }
    }
}
