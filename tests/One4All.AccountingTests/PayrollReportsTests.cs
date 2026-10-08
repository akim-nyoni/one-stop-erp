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
        employee.NationalIdNumber = "63-123456A07";
        employee.TaxNumber = "TX-1";
        employee.DateOfBirth = new DateOnly(1990, 5, 5);
        employee.BankName = "First Bank";
        employee.BankAccountNumber = "123456789";
        employee.Grade = "G3";
        employee.Gender = Gender.Female;
        employee.ContractEndDate = new DateOnly(2026, 3, 15);
        Employee casual = new(companyId, "PR-EMP2", "Casey Casual", 1500m, new DateOnly(2026, 1, 5)) { PaymentMethod = PayMethod.MobileMoney, Grade = "G1" };
        casual.AssignPayElement(basic.Id);
        casual.HoldSalary("Pending verification");
        db.Employees.AddRange(employee, casual);
        await db.SaveChangesAsync();
        StatutoryScheme fund = new(companyId, "PEN", "Pension Fund", SchemeKind.Pension, "Fund Administrators", "PENSION", null);
        db.Add(fund);
        TaxTable table = new(companyId, "TAX26", new DateOnly(2026, 1, 1));
        table.AddBand(0m, 1000m, 0m);
        table.AddBand(1000m, null, 20m);
        db.Add(table);
        await db.SaveChangesAsync();
        db.AddRange(new SchemeMembership(companyId, fund.Id, employee.Id, "PF-77", new DateOnly(2025, 1, 1)), new SchemeRemittance(companyId, fund.Id, "2026-01", 100m, new DateOnly(2026, 2, 5), "FUND-JAN"));
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

        (await Render("PR2")).Should().Contain("Deduction,PAYE,PAYE,1,180").And.Contain("Earning,BASIC,Basic Salary,2,3306.45"); // the casual started on 5 January: 27/31 of 1,500
        (await Render("PR16")).Should().Contain("2026-01,2,\"3,306.45\",280.00,\"3,026.45\",\"3,026.45\",0");
        (await Render("PR29")).Should().Contain("PR-EMP Pat Payee,\"2,000.00\",9.0%,180");
        string gl = await Render("PR64");
        gl.Should().Contain("6100,").And.Contain("\"3,306.45\",\"3,306.45\",0").And.Contain("-100.00,-100.00,0");
        (await Render("PR70")).Should().Contain("PR-EMP Pat Payee,—,\"2,000.00\",\"2,000.00\",280.00,0.00,1720");

        (await Render("PR4")).Should().Contain("PR-EMP2 Casey Casual,—,MobileMoney,No national ID; No tax number; Mobile money without a wallet number; No date of birth").And.NotContain("PR-EMP Pat Payee");
        (await Render("PR11")).Should().Contain("PR-EMP Pat Payee,First Bank,—,123456789,2026-01,1720");
        (await Render("PR13")).Should().NotContain("Casey"); // held
        (await Render("PR15")).Should().Contain("PR-EMP2 Casey Casual,—,MobileMoney,Pending verification,1306.45");
        (await Render("PR46")).Should().Contain("Starter,PR-EMP2 Casey Casual,—,2026-01-05");
        (await Render("PR48")).Should().Contain("G3,1,1,0,0").And.Contain("G1,1,0,0,1");
        (await Render("PR51")).Should().Contain("Contract end,PR-EMP Pat Payee,—,2026-03-15,in 43 days");
        (await Render("PR68")).Should().Contain("PR-EMP2 Casey Casual,—,2026-01-05,No national ID");

        (await Render("PR7")).Should().Contain("2026-01,2026-01-25,2,\"3,306.45\",280.00,JNL-");
        (await Render("PR19")).Should().Contain("PEN Pension Fund,PR-EMP Pat Payee,PF-77,100.00,0.00,100");
        (await Render("PR20")).Should().Contain("PEN Pension Fund,2026-01,100.00,100.00,Remitted,0");
        (await Render("PR27")).Should().Contain("2026-01,2,\"3,306.45\",261.29,-81.29,180"); // the formula relieves pension; the table does not
        (await Render("PR31")).Should().Contain("PEN Pension Fund,PR-EMP Pat Payee,PF-77,100.00,0.00,100");
        (await Render("PR36")).Should().Contain("TAX26,2026-01-01,0.00,\"1,000.00\",0,0.00").And.Contain("and above");

        (await Render("PR58")).Should().Contain("PR-EMP Pat Payee,—,1,\"2,000.00\",0.00,\"24,000.00\",2000");

        foreach (string code in PayrollReports.All().Select(r => r.Code))
        {
            await Render(code);
        }
    }
}
