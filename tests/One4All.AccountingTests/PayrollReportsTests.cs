using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Payroll;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Payroll;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using One4All.Reporting;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the PR-coded payroll reports (brief §14 O): a January run (basic 2,000, pension 5%, PAYE 180); the element
/// summary, net pay control, tax reconciliation, GL reconciliation and YTD register agree with it; every PR report renders.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PayrollReportsTests
{
    private readonly AccountingFixture _fixture;

    public PayrollReportsTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Payroll_reports_agree_with_the_run()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        PayElement basic = new(companyId, "BASIC", "Basic Salary", PayElementType.Earning, CalculationBasis.Formula, 1, "6100") { Formula = "BASIC", Taxable = true };
        PayElement pension = new(companyId, "PENSION", "Pension (5% of basic)", PayElementType.Deduction, CalculationBasis.PercentOfBasic, 2, "2410") { Rate = 5m };
        PayElement paye = new(companyId, "PAYE", "PAYE", PayElementType.Deduction, CalculationBasis.Formula, 3, "2400") { Formula = "TAXABLE - PENSION > 1000 ? (TAXABLE - PENSION - 1000) * 20 / 100 : 0" };
        db.PayElements.AddRange(basic, pension, paye);
        await db.SaveChangesAsync();
        Employee employee = new(companyId, "PR-EMP", "Pat Payee", 2000m, new DateOnly(2025, 1, 1));
        employee.AssignPayElement(basic.Id);
        employee.AssignPayElement(pension.Id);
        employee.AssignPayElement(paye.Id);
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<IPayrollService>().RunAsync(companyId, "2026-01", new DateOnly(2026, 1, 25), "2420", "PRR-2026-01")).IsSuccess.Should().BeTrue();

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        async Task<string> Render(string code)
        {
            Dictionary<string, object?> parameters = new() { ["FromDate"] = new DateOnly(2026, 1, 1), ["ToDate"] = new DateOnly(2026, 1, 31), ["AsAtDate"] = new DateOnly(2026, 1, 31) };
            var rendered = await runner.RenderAsync(companyId, code, parameters, ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? $"{code}: {rendered.Error.Message}" : null);
            return Encoding.UTF8.GetString(rendered.Value.Content);
        }

        (await Render("PR2")).Should().Contain("Deduction,PAYE,PAYE,1,180").And.Contain("Earning,BASIC,Basic Salary,1,2000");
        (await Render("PR16")).Should().Contain("2026-01,1,\"2,000.00\",280.00,\"1,720.00\",\"1,720.00\",0");
        (await Render("PR29")).Should().Contain("PR-EMP Pat Payee,\"2,000.00\",9.0%,180");
        string gl = await Render("PR64");
        gl.Should().Contain("6100,").And.Contain("\"2,000.00\",\"2,000.00\",0").And.Contain("-100.00,-100.00,0");
        (await Render("PR70")).Should().Contain("PR-EMP Pat Payee,—,\"2,000.00\",\"2,000.00\",280.00,0.00,1720");

        foreach (string code in PayrollReports.All().Select(r => r.Code))
        {
            await Render(code);
        }
    }
}
