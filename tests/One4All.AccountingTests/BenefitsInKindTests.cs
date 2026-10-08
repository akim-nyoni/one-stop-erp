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
/// Covers taxable benefits in kind (brief §4.15.4): a company car valued from the vehicle scale by engine capacity, a housing benefit at its own
/// value and an interest-free loan's benefit at the deemed rate are taxed and shown on the payslip as memo lines, but neither paid nor posted; a
/// benefit that has ended no longer counts.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class BenefitsInKindTests
{
    private readonly AccountingFixture _fixture;

    public BenefitsInKindTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Benefits_are_taxed_as_memo_lines_without_being_paid()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Company company = await db.Companies.SingleAsync(c => c.Id == companyId);
        company.DeemedLoanInterestRatePercent = 10m;
        PayElement basic = new(companyId, "BASIC", "Basic", PayElementType.Earning, CalculationBasis.Formula, 1, "6100") { Formula = "BASIC", Taxable = true };
        PayElement paye = new(companyId, "PAYE", "PAYE", PayElementType.Deduction, CalculationBasis.TaxTable, 2, "2400");
        db.PayElements.AddRange(basic, paye);
        TaxTable table = new(companyId, "TAX26", new DateOnly(2026, 1, 1));
        table.AddBand(0m, 1000m, 0m);
        table.AddBand(1000m, null, 20m);
        db.Add(table);
        DateOnly scaleFrom = new(2026, 1, 1);
        db.VehicleBenefitScales.AddRange(
            new VehicleBenefitScale(companyId, VehicleScaleBasis.EngineCapacity, 0m, 1500m, 100m, scaleFrom),
            new VehicleBenefitScale(companyId, VehicleScaleBasis.EngineCapacity, 1501m, 2500m, 200m, scaleFrom),
            new VehicleBenefitScale(companyId, VehicleScaleBasis.EngineCapacity, 2501m, null, 300m, scaleFrom));
        Employee employee = new(companyId, "BEN-1", "Benny Benefits", 1000m, new DateOnly(2025, 1, 1));
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        employee.AssignPayElement(basic.Id);
        employee.AssignPayElement(paye.Id);
        db.EmployeeBenefits.AddRange(
            new EmployeeBenefit(companyId, employee.Id, BenefitKind.MotorVehicle, "ABC 1234", new DateOnly(2026, 1, 1)) { EngineCapacityCc = 2000 },
            new EmployeeBenefit(companyId, employee.Id, BenefitKind.Housing, "Staff house 4", new DateOnly(2026, 1, 1)) { MonthlyValue = 150m },
            new EmployeeBenefit(companyId, employee.Id, BenefitKind.Medical, "Ended cover", new DateOnly(2025, 1, 1)) { MonthlyValue = 999m, To = new DateOnly(2025, 12, 31) });
        db.EmployeeLoans.Add(new EmployeeLoan(companyId, employee.Id, "LN-7", 12000m, 500m, new DateOnly(2026, 1, 2), "1200") { DeductFromPay = false });
        await db.SaveChangesAsync();

        (await scope.ServiceProvider.GetRequiredService<IPayrollService>().RunAsync(companyId, "2026-01", new DateOnly(2026, 1, 25), "2420", "BEN-2026-01")).IsSuccess.Should().BeTrue();
        Payslip slip = await db.Payslips.AsNoTracking().Include(p => p.Lines).SingleAsync(p => p.EmployeeId == employee.Id);
        slip.Lines.Where(l => l.Type == PayElementType.Memo).Select(l => (l.ElementCode, l.Amount)).Should().BeEquivalentTo(new[]
        {
            ("BEN-MOTORVEHICLE", 200m), ("BEN-HOUSING", 150m), ("BEN-LOAN", 100m),
        }, "a 2,000cc car on the scale, the house at its value, 12,000 interest-free at a 10% deemed rate");
        slip.Lines.Single(l => l.ElementCode == "PAYE").Amount.Should().Be(90m, "taxable pay is 1,000 plus 450 of benefits");
        slip.GrossEarnings.Should().Be(1000m);
        slip.NetPay.Should().Be(910m, "benefits are taxed, not paid");

        List<string> posted = await db.Journals.AsNoTracking().Where(j => j.CompanyId == companyId && j.DocumentNumber == "BEN-2026-01").SelectMany(j => j.Lines).Select(l => l.AccountCode).ToListAsync();
        posted.Should().Contain("6100").And.NotContain(a => a.StartsWith("BEN", StringComparison.Ordinal));
    }

    [Fact]
    public void A_loan_charging_the_deemed_rate_has_no_benefit()
    {
        BenefitValuation.LoanBenefit(12000m, 0m, 10m).Should().Be(100m);
        BenefitValuation.LoanBenefit(12000m, 4m, 10m).Should().Be(60m);
        BenefitValuation.LoanBenefit(12000m, 12m, 10m).Should().Be(0m);
        BenefitValuation.LoanBenefit(0m, 0m, 10m).Should().Be(0m);
    }
}
