using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Payroll;
using One4All.Core.Payroll;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers PAYE from the effective-dated tax table: per period, cumulative year-to-date (bands times the period number less tax already
/// deducted), annual payments spread (annualised and apportioned), and a clear refusal when no table is in force.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class TaxTableBasisTests
{
    private readonly AccountingFixture _fixture;

    public TaxTableBasisTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Tax_is_calculated_from_the_table_per_period_cumulatively_and_with_bonuses_spread()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        TaxTable table = new(companyId, "PAYE26", new DateOnly(2026, 1, 1));
        table.AddBand(0m, 500m, 0m);
        table.AddBand(500m, 2000m, 20m);
        table.AddBand(2000m, null, 30m);
        db.Add(table);
        PayElement basic = new(companyId, "BASIC", "Basic Salary", PayElementType.Earning, CalculationBasis.Formula, 1, "6100") { Formula = "BASIC", Taxable = true };
        PayElement bonus = new(companyId, "BONUS", "Annual bonus", PayElementType.Earning, CalculationBasis.FixedAmount, 2, "6100") { Taxable = true, IsAnnualPayment = true };
        PayElement periodTax = new(companyId, "PAYE-P", "PAYE", PayElementType.Deduction, CalculationBasis.TaxTable, 3, "2400");
        PayElement cumulativeTax = new(companyId, "PAYE-C", "PAYE (cumulative)", PayElementType.Deduction, CalculationBasis.TaxTable, 3, "2400") { CumulativeTax = true };
        PayElement missing = new(companyId, "PAYE-X", "PAYE (no table)", PayElementType.Deduction, CalculationBasis.TaxTable, 3, "2400") { TaxTableCode = "NONE" };
        db.PayElements.AddRange(basic, bonus, periodTax, cumulativeTax, missing);
        await db.SaveChangesAsync();
        Employee pam = new(companyId, "TT-1", "Pam Period", 1000m, new DateOnly(2025, 1, 1));
        Employee cal = new(companyId, "TT-2", "Cal Cumulative", 1000m, new DateOnly(2025, 1, 1));
        pam.AssignPayElement(basic.Id);
        pam.AssignPayElement(periodTax.Id);
        cal.AssignPayElement(basic.Id);
        cal.AssignPayElement(cumulativeTax.Id);
        db.Employees.AddRange(pam, cal);
        await db.SaveChangesAsync();

        IPayrollService payroll = scope.ServiceProvider.GetRequiredService<IPayrollService>();

        // January: both pay 20% of 500.
        (await payroll.CalculateAsync(companyId, pam.Id)).Value.Lines.Should().Contain(new PayLine("PAYE-P", 100m));
        (await payroll.RunAsync(companyId, "2026-01", new DateOnly(2026, 1, 25), "2420", "TT-01")).Value.TotalDeductions.Should().Be(200m);

        // February: Cal's raise to 3,000 is taxed cumulatively — 4,000 to date on doubled bands is 600, less January's 100 = 500
        // (per period it would be 600). Pam's 1,200 bonus is spread: 100 on her pay plus 1,440 − 1,200 = 240 on the bonus (not 260).
        cal.BasicSalary = 3000m;
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<IPayrollVariableService>().SetAsync(companyId, pam.Id, "BONUS", "2026-02", null, 1200m)).IsSuccess.Should().BeTrue();
        var february = await payroll.RunAsync(companyId, "2026-02", new DateOnly(2026, 2, 25), "2420", "TT-02");
        february.IsSuccess.Should().BeTrue(february.IsFailure ? february.Error.ToString() : null);
        february.Value.TotalDeductions.Should().Be(500m + 340m);

        // March back to 1,000: 5,000 to date on tripled bands is 700, less the 600 already deducted = 100 (Pam pays her usual 100).
        cal.BasicSalary = 1000m;
        await db.SaveChangesAsync();
        (await payroll.RunAsync(companyId, "2026-03", new DateOnly(2026, 3, 25), "2420", "TT-03")).Value.TotalDeductions.Should().Be(100m + 100m);

        // An element pointing at a table that does not exist says so.
        Employee max = new(companyId, "TT-3", "Max Missing", 1000m, new DateOnly(2025, 1, 1));
        max.AssignPayElement(basic.Id);
        max.AssignPayElement(missing.Id);
        db.Employees.Add(max);
        await db.SaveChangesAsync();
        (await payroll.CalculateAsync(companyId, max.Id)).Error.Code.Should().Be("Payroll.NoTaxTable");
    }
}
