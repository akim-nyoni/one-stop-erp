using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Payroll;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Payroll;
using One4All.Core.Security;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers net-pay protection: deductions that would take net pay below the minimum take-home (an amount or a percentage of gross) are
/// suspended - highest suspension priority first, statutory deductions never - carried forward as arrears, recovered when pay allows,
/// undone by a run reversal, and listed in PR71.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class NetPayProtectionTests
{
    private readonly AccountingFixture _fixture;

    public NetPayProtectionTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Deductions_are_suspended_to_protect_net_pay_and_recovered_later()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        long companyId = await Scenario.SeedAsync(db);
        Company company = await db.Companies.SingleAsync(c => c.Id == companyId);
        company.MinimumTakeHomeAmount = 600m;
        PayElement basic = new(companyId, "BASIC", "Basic Salary", PayElementType.Earning, CalculationBasis.Formula, 1, "6100") { Formula = "BASIC", Taxable = true };
        PayElement paye = new(companyId, "PAYE", "PAYE", PayElementType.Deduction, CalculationBasis.Formula, 2, "2400") { Formula = "TAXABLE * 10 / 100" };
        PayElement union = new(companyId, "UNION", "Union dues", PayElementType.Deduction, CalculationBasis.FixedAmount, 3, "2410") { FixedAmount = 150m, SuspensionPriority = 1 };
        PayElement loan = new(companyId, "LOAN", "Furniture loan", PayElementType.Deduction, CalculationBasis.FixedAmount, 4, "2410") { FixedAmount = 250m, SuspensionPriority = 2 };
        db.PayElements.AddRange(basic, paye, union, loan);
        User approver = new($"pn-approver-{companyId}", "Approver");
        db.Users.Add(approver);
        await db.SaveChangesAsync();
        Employee pia = new(companyId, "PN-1", "Pia Net", 1000m, new DateOnly(2025, 1, 1));
        foreach (PayElement element in new[] { basic, paye, union, loan })
        {
            pia.AssignPayElement(element.Id);
        }

        db.Employees.Add(pia);
        await db.SaveChangesAsync();
        IPayrollService payroll = scope.ServiceProvider.GetRequiredService<IPayrollService>();
        async Task<PayrollRunResult> Run(int month)
        {
            var run = await payroll.RunAsync(companyId, $"2026-{month:00}", new DateOnly(2026, month, 25), "2420", $"PN-{month:00}");
            run.IsSuccess.Should().BeTrue(run.IsFailure ? run.Error.ToString() : null);
            return run.Value;
        }

        // January: 1,000 − 100 PAYE − 150 union − 250 loan = 500, below 600: the loan (priority 2) gives up 100.
        (await payroll.CalculateAsync(companyId, pia.Id)).Value.Lines.Should().Contain([new PayLine("PAYE", 100m), new PayLine("UNION", 150m), new PayLine("LOAN", 150m)]);
        (await Run(1)).TotalNet.Should().Be(600m);

        // February: the loan tries 250 + 100 arrears; only 150 fits, so the arrears grow to 200.
        (await Run(2)).TotalNet.Should().Be(600m);

        // March: a raise to 1,500 lets the loan take 250 + 200 arrears in full.
        pia.BasicSalary = 1500m;
        await db.SaveChangesAsync();
        (await Run(3)).TotalNet.Should().Be(1500m - 150m - 150m - 450m);

        // April: a 70%-of-gross floor (1,050) suspends 100 of the loan again - then the run is reversed and so are its arrears.
        company.MinimumTakeHomeAmount = null;
        company.MinimumTakeHomePercent = 70m;
        await db.SaveChangesAsync();
        (await Run(4)).TotalNet.Should().Be(1050m);
        (await scope.ServiceProvider.GetRequiredService<IPayrollAdjustmentService>().ReverseRunAsync(companyId, "PN-04", new DateOnly(2026, 4, 28), "Wrong floor", approver.Id)).IsSuccess.Should().BeTrue();
        (await db.DeductionArrears.Where(a => a.CompanyId == companyId).SumAsync(a => a.Movement)).Should().Be(0m);

        var rendered = await scope.ServiceProvider.GetRequiredService<IReportRunner>().RenderAsync(companyId, "PR71",
            new Dictionary<string, object?> { ["FromDate"] = new DateOnly(2026, 1, 1), ["ToDate"] = new DateOnly(2026, 4, 30) }, ReportFormat.Csv);
        rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? rendered.Error.Message : null);
        string csv = Encoding.UTF8.GetString(rendered.Value.Content);
        csv.Should().Contain("PN-1 Pia Net,2026-01,LOAN,250.00,150.00,Suspended 100.00: net pay held at the minimum take-home of 600.00,100")
            .And.Contain("PN-1 Pia Net,2026-02,LOAN,350.00,150.00,Suspended 200.00: net pay held at the minimum take-home of 600.00,200")
            .And.Contain("PN-1 Pia Net,2026-03,LOAN,450.00,450.00,Arrears recovered,0")
            .And.Contain("PN-1 Pia Net,2026-04,LOAN,250.00,150.00,\"Suspended 100.00: net pay held at the minimum take-home of 1,050.00\",100")
            .And.Contain("PN-1 Pia Net,2026-04,LOAN,-250.00,-150.00,Reversed with run PN-04,0")
            .And.NotContain("UNION").And.NotContain("PAYE");
    }
}
