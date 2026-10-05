using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Payroll;
using One4All.Core.Payroll;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers e-mailed payslips: each employee of a run is queued a PDF protected by the company's password rule; employees without an
/// address or without the detail the rule needs are listed instead; with no rule the PDF is open.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PayslipDistributionTests
{
    private readonly AccountingFixture _fixture;

    public PayslipDistributionTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Run_payslips_are_emailed_as_protected_pdfs()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Company company = await db.Companies.SingleAsync(c => c.Id == companyId);
        company.PayslipPasswordRule = PayslipPasswordRule.DateOfBirth;
        PayElement basic = new(companyId, "BASIC", "Basic Salary", PayElementType.Earning, CalculationBasis.Formula, 1, "6100") { Formula = "BASIC", Taxable = true };
        db.PayElements.Add(basic);
        await db.SaveChangesAsync();
        Employee ada = new(companyId, "PD-1", "Ada Mail", 1000m, new DateOnly(2025, 1, 1)) { Email = "ada@example.test", DateOfBirth = new DateOnly(1980, 4, 12) };
        Employee bea = new(companyId, "PD-2", "Bea NoMail", 1000m, new DateOnly(2025, 1, 1)) { DateOfBirth = new DateOnly(1990, 1, 1) };
        Employee cal = new(companyId, "PD-3", "Cal NoBirthday", 1000m, new DateOnly(2025, 1, 1)) { Email = "cal@example.test" };
        foreach (Employee e in new[] { ada, bea, cal })
        {
            e.AssignPayElement(basic.Id);
        }

        db.Employees.AddRange(ada, bea, cal);
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<IPayrollService>().RunAsync(companyId, "2026-09", new DateOnly(2026, 9, 25), "2420", "PD-09")).IsSuccess.Should().BeTrue();

        IPayslipDistributionService payslips = scope.ServiceProvider.GetRequiredService<IPayslipDistributionService>();
        PayslipEmailRun sent = (await payslips.EmailRunAsync(companyId, "PD-09")).Value;
        sent.Queued.Should().Be(1);
        sent.NoEmail.Should().Equal("PD-2");
        sent.NoPassword.Should().Equal("PD-3");

        string locked = Encoding.Latin1.GetString((await payslips.PayslipPdfAsync(companyId, ada.Id, "2026-09")).Value);
        locked.Should().StartWith("%PDF-1.6").And.Contain("/Encrypt").And.NotContain("Basic Salary");
        (await payslips.PayslipPdfAsync(companyId, cal.Id, "2026-09")).Error.Code.Should().Be("Payroll.NoPassword");
        (await payslips.PayslipPdfAsync(companyId, ada.Id, "2026-08")).Error.Code.Should().Be("Ess.NoPayslip");

        company.PayslipPasswordRule = PayslipPasswordRule.None;
        await db.SaveChangesAsync();
        Encoding.Latin1.GetString((await payslips.PayslipPdfAsync(companyId, cal.Id, "2026-09")).Value).Should().NotContain("/Encrypt").And.Contain("Basic Salary");
    }
}
