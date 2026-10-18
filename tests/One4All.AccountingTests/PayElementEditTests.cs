using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Payroll;
using One4All.Core.Payroll;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Thirteenth audit #23: a pay element is edited in full after it exists, checked as a new one is, its code and type fixed.</summary>
[Collection(AccountingCollection.Name)]
public sealed class PayElementEditTests
{
    private readonly AccountingFixture _fixture;

    public PayElementEditTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task An_element_changes_everything_but_its_code_and_type()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        IPayrollSetupService setup = scope.ServiceProvider.GetRequiredService<IPayrollSetupService>();
        (await setup.CreatePayElementAsync(companyId, new NewPayElement("PEE-ALW", "Allowance", PayElementType.Earning, CalculationBasis.FixedAmount, 20, "6000") { FixedAmount = 50m })).IsSuccess.Should().BeTrue();

        NewPayElement edited = new("pee-alw", "Site allowance", PayElementType.Earning, CalculationBasis.Formula, 25, "6800")
        {
            Formula = "BASIC * 0.05", Taxable = true, Pensionable = true, Group = "allowances", Ceiling = 300m, RoundTo = 1m, Rounding = PayRounding.Up,
        };
        (await setup.EditPayElementAsync(companyId, edited with { Formula = null })).Error.Code.Should().Be("Payroll.FormulaRequired");
        (await setup.EditPayElementAsync(companyId, edited with { AccountCode = "9999" })).Error.Code.Should().Be("Payroll.AccountNotFound");
        (await setup.EditPayElementAsync(companyId, edited with { Type = PayElementType.Deduction })).Error.Code.Should().Be("Payroll.ElementTypeFixed");
        (await setup.EditPayElementAsync(companyId, edited with { Code = "PEE-NONE" })).Error.Code.Should().Be("Payroll.ElementNotFound");
        (await setup.EditPayElementAsync(companyId, edited)).IsSuccess.Should().BeTrue();

        PayElement saved = await db.PayElements.AsNoTracking().SingleAsync(e => e.CompanyId == companyId && e.Code == "PEE-ALW");
        (saved.Name, saved.Basis, saved.Sequence, saved.AccountCode, saved.Formula, saved.Taxable, saved.Pensionable, saved.Group, saved.Ceiling, saved.Rounding)
            .Should().Be(("Site allowance", CalculationBasis.Formula, 25, "6800", "BASIC * 0.05", true, true, "ALLOWANCES", (decimal?)300m, PayRounding.Up));
        saved.FixedAmount.Should().Be(0m, "the edit is the element in full");
    }
}
