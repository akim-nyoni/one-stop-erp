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
public sealed class PayrollDeductionsReportTests
{
    private readonly AccountingFixture _fixture;

    public PayrollDeductionsReportTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Deductions_report_aggregates_by_element_across_payslips()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);

            PayElement basic = new(companyId, "BASIC", "Basic Salary", PayElementType.Earning, CalculationBasis.Formula, 1, "6100") { Formula = "BASIC", Taxable = true };
            PayElement pension = new(companyId, "PENSION", "Pension (5% of basic)", PayElementType.Deduction, CalculationBasis.PercentOfBasic, 2, "2410") { Rate = 5m };
            PayElement paye = new(companyId, "PAYE", "PAYE", PayElementType.Deduction, CalculationBasis.Formula, 3, "2400") { Formula = "TAXABLE - PENSION > 1000 ? (TAXABLE - PENSION - 1000) * 20 / 100 : 0" };
            db.PayElements.AddRange(basic, pension, paye);
            await db.SaveChangesAsync();

            Employee emp = new(companyId, "PD-EMP", "Pat Payee", 2000m, new DateOnly(2025, 1, 1));
            emp.AssignPayElement(basic.Id);
            emp.AssignPayElement(pension.Id);
            emp.AssignPayElement(paye.Id);
            db.Employees.Add(emp);
            await db.SaveChangesAsync();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPayrollService payroll = scope.ServiceProvider.GetRequiredService<IPayrollService>();
            var run = await payroll.RunAsync(companyId, "2026-01", new DateOnly(2026, 1, 25), "2420", "PDR-2026-01");
            run.IsSuccess.Should().BeTrue(run.IsFailure ? run.Error.ToString() : null);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            // Basic 2000, pension 100 (5%), taxable-relief 1900 → PAYE (1900-1000)*20% = 180.
            IReportDataProvider vertical = scope.ServiceProvider.GetServices<IReportDataProvider>().First(p => p.CanHandle("PayrollDeductions"));
            var parameters = new Dictionary<string, object?> { ["FromDate"] = new DateOnly(2026, 1, 1), ["ToDate"] = new DateOnly(2026, 1, 31) };
            var rows = await vertical.GetRowsAsync(companyId, "PayrollDeductions", parameters);

            rows.Should().Contain(r => (string)r["ElementCode"]! == "PENSION" && (decimal)r["Amount"]! == 100m);
            rows.Should().Contain(r => (string)r["ElementCode"]! == "PAYE" && (decimal)r["Amount"]! == 180m);

            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            string html = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "PAY-DED", parameters, ReportFormat.Html)).Value.Content);
            html.Should().Contain("PENSION").And.Contain("PAYE").And.Contain("180.00");
        }
    }
}
