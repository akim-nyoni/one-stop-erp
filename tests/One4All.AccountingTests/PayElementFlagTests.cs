using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Payroll;
using One4All.Core.Payroll;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the pay element flags of brief §4.15.2: a pro-rated allowance follows the days employed like basic; a balance-driven deduction takes its
/// amount each run until the employee's balance is used up and no more; earnings flagged for union dues and terminal benefits feed the UNIONBASE
/// and TERMINALBASE variables a formula can take a percentage of.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PayElementFlagTests
{
    private readonly AccountingFixture _fixture;

    public PayElementFlagTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Pro_rated_balance_driven_and_base_flags_shape_the_payslip()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        PayElement basic = new(companyId, "BASIC", "Basic Salary", PayElementType.Earning, CalculationBasis.Formula, 1, "6100") { Formula = "BASIC", Taxable = true, UnionDues = true };
        PayElement transport = new(companyId, "TRANSPORT", "Transport allowance", PayElementType.Earning, CalculationBasis.FixedAmount, 2, "6100") { FixedAmount = 300m, ProRated = true, UnionDues = true, TerminalBenefit = true };
        PayElement phone = new(companyId, "PHONE", "Phone allowance", PayElementType.Earning, CalculationBasis.FixedAmount, 3, "6100") { FixedAmount = 100m };
        PayElement advance = new(companyId, "ADVANCE", "Advance recovery", PayElementType.Deduction, CalculationBasis.BalanceDriven, 4, "1250") { FixedAmount = 400m };
        PayElement dues = new(companyId, "UNION", "Union dues", PayElementType.Deduction, CalculationBasis.Formula, 5, "2450") { Formula = "UNIONBASE * 1 / 100" };
        db.PayElements.AddRange(basic, transport, phone, advance, dues);
        await db.SaveChangesAsync();

        // Joined on the 16th of a 30-day month: basic and the pro-rated transport allowance are paid for 15/30 days; the phone allowance in full.
        Employee joiner = new(companyId, "PF-1", "Pat Joiner", 3000m, new DateOnly(2026, 9, 16));
        foreach (PayElement element in new[] { basic, transport, phone, dues })
        {
            joiner.AssignPayElement(element.Id);
        }

        joiner.AssignPayElement(advance.Id, balance: 700m);
        db.Employees.Add(joiner);
        await db.SaveChangesAsync();

        IPayrollService payroll = scope.ServiceProvider.GetRequiredService<IPayrollService>();

        // September (30 days, joined on the 16th): basic and the pro-rated transport allowance for 15/30 days, the phone allowance in full,
        // the advance's 400 off a 700 balance, and union dues of 1% of the union base (basic + transport = 1,650).
        (await payroll.RunAsync(companyId, "2026-09", new DateOnly(2026, 9, 25), "2420", "PF-09")).IsSuccess.Should().BeTrue();
        Dictionary<string, decimal> september = await db.Set<PayslipLine>().AsNoTracking().Where(l => l.CompanyId == companyId && l.PayDate == new DateOnly(2026, 9, 25))
            .ToDictionaryAsync(l => l.ElementCode, l => l.Amount);
        september.Should().Contain("BASIC", 1500m).And.Contain("TRANSPORT", 150m).And.Contain("PHONE", 100m).And.Contain("ADVANCE", 400m).And.Contain("UNION", 16.50m);

        // Committed runs draw the balance down: 400 in September, the remaining 300 in October, nothing in November.
        (await db.Set<Employee>().AsNoTracking().Include(e => e.PayElements).SingleAsync(e => e.Id == joiner.Id)).PayElements.Single(a => a.PayElementId == advance.Id).Balance.Should().Be(300m);
        (await payroll.RunAsync(companyId, "2026-10", new DateOnly(2026, 10, 25), "2420", "PF-10")).IsSuccess.Should().BeTrue();
        (await payroll.RunAsync(companyId, "2026-11", new DateOnly(2026, 11, 25), "2420", "PF-11")).IsSuccess.Should().BeTrue();
        List<decimal> recovered = await db.Set<PayslipLine>().AsNoTracking().Where(l => l.CompanyId == companyId && l.ElementCode == "ADVANCE").OrderBy(l => l.PayDate).Select(l => l.Amount).ToListAsync();
        recovered.Should().Equal(400m, 300m, 0m);
    }
}
