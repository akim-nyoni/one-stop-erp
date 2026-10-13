using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Payroll;
using One4All.Core.Payroll;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the table-lookup pay element basis and formula preview: a lookup element with bad bands is refused and a good one saved; a formula is
/// worked out against an employee's basic pay and given values before it is saved, and a broken formula is reported.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PayElementLookupTests
{
    private readonly AccountingFixture _fixture;

    public PayElementLookupTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Lookup_elements_are_checked_and_formulas_previewed()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        string number = $"PEL-{companyId % 100000}";
        db.Employees.Add(new Employee(companyId, number, "Lookup Person", 2_000m, new DateOnly(2024, 1, 1)));
        await db.SaveChangesAsync();

        IPayrollSetupService setup = scope.ServiceProvider.GetRequiredService<IPayrollSetupService>();
        NewPayElement Housing(string bands) => new("HOUSELK", "Housing by band", PayElementType.Earning, CalculationBasis.TableLookup, 20, "6000") { LookupBands = bands };
        (await setup.CreatePayElementAsync(companyId, Housing("0-1000:50; 900-:60"))).Error.Code.Should().Be("Payroll.LookupOverlap");
        (await setup.CreatePayElementAsync(companyId, Housing("0-1000:50; 1000-:5%"))).IsSuccess.Should().BeTrue();

        (await setup.PreviewFormulaAsync(companyId, "BASIC * 3 / 2 / 173 * OVERTIMEHOURS", number, new Dictionary<string, decimal> { ["OvertimeHours"] = 10m })).Value
            .Should().BeApproximately(173.41m, 0.01m);
        (await setup.PreviewFormulaAsync(companyId, "BASIC *", number, new Dictionary<string, decimal>())).Error.Code.Should().Be("Payroll.FormulaError");
        (await setup.PreviewFormulaAsync(companyId, "BASIC", "NOBODY", new Dictionary<string, decimal>())).Error.Code.Should().Be("Payroll.EmployeeNotFound");
    }
}
