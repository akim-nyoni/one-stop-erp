using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Onboarding;
using One4All.Application.Abstractions.Payroll;
using One4All.Core.Payroll;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers payroll year-to-date openings: the previous payroll's taxable pay and tax for the year are imported (checked line by line first) and
/// cumulative tax in the first run on this payroll counts them.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PayrollYtdOpeningTests
{
    private readonly AccountingFixture _fixture;

    public PayrollYtdOpeningTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Cumulative_tax_counts_the_previous_payrolls_year_to_date()
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
        PayElement paye = new(companyId, "PAYE", "PAYE (cumulative)", PayElementType.Deduction, CalculationBasis.TaxTable, 3, "2400") { CumulativeTax = true };
        db.PayElements.AddRange(basic, paye);
        await db.SaveChangesAsync();
        Employee migrated = new(companyId, "YTD-1", "Mid-year Migrant", 3000m, new DateOnly(2024, 1, 1));
        migrated.AssignPayElement(basic.Id);
        migrated.AssignPayElement(paye.Id);
        db.Employees.Add(migrated);
        await db.SaveChangesAsync();

        IVerticalImportService registers = scope.ServiceProvider.GetRequiredService<IVerticalImportService>();
        ImportReport bad = await registers.ImportPayrollYtdAsync(companyId, "employee_number,tax_year,code,amount\nYTD-1,2026,TAXABLE,2000\nYTD-9,2026,TAXABLE,1\nYTD-1,2026,BONUSX,5\n", commit: true);
        (bad.Committed, bad.Errors.Count).Should().Be((false, 2));
        ImportReport good = await registers.ImportPayrollYtdAsync(companyId, "employee_number,tax_year,code,amount\nYTD-1,2026,TAXABLE,2000\nytd-1,2026,paye,200\n", commit: true);
        (good.Committed, good.Created).Should().Be((true, 2));

        // March on tripled bands: 5,000 to date (2,000 from the old payroll) is 700 tax, less the 200 already deducted = 500. Without the
        // openings it would be 300 on 3,000.
        PayrollRunResult march = (await scope.ServiceProvider.GetRequiredService<IPayrollService>().RunAsync(companyId, "2026-03", new DateOnly(2026, 3, 25), "2420", "YTD-03")).Value;
        march.TotalDeductions.Should().Be(500m);
    }
}
