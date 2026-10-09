using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Payroll;
using One4All.Core.Payroll;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers tax credits in a payroll run: an elderly credit from 65 for everyone, a medical-aid credit of half the contributions for those given
/// it, and credits that never take the tax below nil.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class TaxCreditPayrollTests
{
    private readonly AccountingFixture _fixture;

    public TaxCreditPayrollTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Credits_reduce_the_tax_deducted_in_a_run()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        PayElement basic = new(companyId, "BASIC", "Basic", PayElementType.Earning, CalculationBasis.Formula, 1, "6100") { Formula = "BASIC", Taxable = true };
        PayElement medical = new(companyId, "MEDAID", "Medical aid", PayElementType.Deduction, CalculationBasis.FixedAmount, 2, "2410") { FixedAmount = 100m };
        PayElement paye = new(companyId, "PAYE", "PAYE", PayElementType.Deduction, CalculationBasis.TaxTable, 3, "2400");
        db.PayElements.AddRange(basic, medical, paye);
        TaxTable table = new(companyId, "TC26", new DateOnly(2026, 1, 1));
        table.AddBand(0m, 1000m, 0m);
        table.AddBand(1000m, null, 20m);
        db.Add(table);
        Employee senior = new(companyId, "TC-1", "Senior", 2000m, new DateOnly(2010, 1, 1)) { DateOfBirth = new DateOnly(1960, 1, 1) };
        Employee young = new(companyId, "TC-2", "Young", 2000m, new DateOnly(2020, 1, 1)) { DateOfBirth = new DateOnly(1995, 1, 1) };
        Employee low = new(companyId, "TC-3", "Low Earner", 1100m, new DateOnly(2020, 1, 1)) { DateOfBirth = new DateOnly(1950, 1, 1) };
        db.Employees.AddRange(senior, young, low);
        await db.SaveChangesAsync();
        foreach (Employee e in new[] { senior, young, low })
        {
            e.AssignPayElement(basic.Id);
            e.AssignPayElement(medical.Id);
            e.AssignPayElement(paye.Id);
        }

        await db.SaveChangesAsync();
        IPayrollSetupService setup = scope.ServiceProvider.GetRequiredService<IPayrollSetupService>();
        (await setup.SaveTaxCreditAsync(companyId, new TaxCreditInput("MED", "Medical aid credit", TaxCreditBasis.PercentOfElement, 0m, 50m, "NOPE", null, false))).Error.Code.Should().Be("Pay.ElementNotFound");
        (await setup.SaveTaxCreditAsync(companyId, new TaxCreditInput("MED", "Medical aid credit", TaxCreditBasis.PercentOfElement, 0m, 50m, "medaid", null, false))).IsSuccess.Should().BeTrue();
        (await setup.SaveTaxCreditAsync(companyId, new TaxCreditInput("ELDER", "Elderly credit", TaxCreditBasis.Fixed, 75m, 0m, null, 65, true))).IsSuccess.Should().BeTrue();
        (await setup.SetEmployeeTaxCreditAsync(companyId, "TC-2", "MED", true)).IsSuccess.Should().BeTrue();
        (await setup.SetEmployeeTaxCreditAsync(companyId, "TC-9", "MED", true)).Error.Code.Should().Be("Pay.TaxCreditNotFound");

        (await scope.ServiceProvider.GetRequiredService<IPayrollService>().RunAsync(companyId, "2026-01", new DateOnly(2026, 1, 25), "2420", "TC-2026-01")).IsSuccess.Should().BeTrue();
        Dictionary<long, decimal> tax = await db.Payslips.AsNoTracking().Include(p => p.Lines).Where(p => p.CompanyId == companyId)
            .ToDictionaryAsync(p => p.EmployeeId, p => p.Lines.Where(l => l.ElementCode == "PAYE").Sum(l => l.Amount));
        tax[senior.Id].Should().Be(200m - 75m, "20% of 1,000 above the threshold, less the elderly credit");
        tax[young.Id].Should().Be(200m - 50m, "less half the 100 medical aid");
        tax[low.Id].Should().Be(0m, "20 of tax is less than the 75 credit, and nothing is refunded");
    }
}
