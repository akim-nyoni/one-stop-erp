using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Payroll;
using One4All.Core.Payroll;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers rendering an employee's payslip from the computed pay run.</summary>
[Collection(AccountingCollection.Name)]
public sealed class PayslipTests
{
    private readonly AccountingFixture _fixture;

    public PayslipTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task An_employee_payslip_shows_earnings_deductions_and_net()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long employeeId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);

            PayElement basic = new(companyId, "BASIC", "Basic Salary", PayElementType.Earning, CalculationBasis.Formula, 1, "6100") { Formula = "BASIC", Taxable = true };
            PayElement pension = new(companyId, "PENSION", "Pension", PayElementType.Deduction, CalculationBasis.PercentOfBasic, 2, "2410") { Rate = 5m };
            db.PayElements.AddRange(basic, pension);
            await db.SaveChangesAsync();

            Employee emp = new(companyId, "EMP-007", "Bob Payee", 2000m, new DateOnly(2025, 1, 1));
            emp.AssignPayElement(basic.Id);
            emp.AssignPayElement(pension.Id);
            db.Employees.Add(emp);
            await db.SaveChangesAsync();
            employeeId = emp.Id;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPayrollService payroll = scope.ServiceProvider.GetRequiredService<IPayrollService>();
            var result = await payroll.RenderPayslipTextAsync(companyId, employeeId, "2026-01", new DateOnly(2026, 1, 25));

            result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : null);
            string text = result.Value;
            text.Should().Contain("Test Trading Co");
            text.Should().Contain("PAYSLIP");
            text.Should().Contain("Employee: Bob Payee").And.Contain("EMP-007");
            text.Should().Contain("EARNINGS").And.Contain("Basic Salary").And.Contain("2,000.00");
            text.Should().Contain("DEDUCTIONS").And.Contain("Pension").And.Contain("100.00"); // 5% of 2000
            text.Should().Contain("NET PAY").And.Contain("1,900.00");

            (await payroll.RenderPayslipTextAsync(companyId, 999_999, "2026-01", new DateOnly(2026, 1, 25))).Error.Code.Should().Be("Payroll.EmployeeNotFound");
        }
    }
}
