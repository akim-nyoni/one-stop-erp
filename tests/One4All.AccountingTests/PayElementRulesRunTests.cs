using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Payroll;
using One4All.Core.Accounting;
using One4All.Core.Payroll;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the pay element engine's rules in a run (brief §4.15.4): pensionable and social-security bases built by flagged earnings, a ceiling on
/// social security, a percentage of an element group rounded up, a memo line that is neither paid nor posted, a provision posted as an employer
/// accrual, and salaries posted to the department's own expense account.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PayElementRulesRunTests
{
    private readonly AccountingFixture _fixture;

    public PayElementRulesRunTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Flags_groups_limits_memos_and_provisions_work_in_a_run()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await Scenario.DimensionsAsync(db, companyId, "OPS");
        db.Accounts.AddRange(new LedgerAccount(companyId, "6115", "Salaries - Operations", AccountType.Expense), new LedgerAccount(companyId, "6155", "Leave Pay Expense", AccountType.Expense),
            new LedgerAccount(companyId, "2465", "Leave Pay Provision", AccountType.Liability), new LedgerAccount(companyId, "2475", "Social Security Payable", AccountType.Liability));
        await db.SaveChangesAsync();
        IPayrollSetupService setup = scope.ServiceProvider.GetRequiredService<IPayrollSetupService>();
        async Task<long> Element(NewPayElement element) => (await setup.CreatePayElementAsync(companyId, element)).Value;

        long[] ids =
        [
            await Element(new NewPayElement("BASIC", "Basic", PayElementType.Earning, CalculationBasis.Formula, 1, "6100")
                { Formula = "BASIC", Taxable = true, Pensionable = true, SubjectToSocialSecurity = true, DepartmentAccounts = "OPS=6115" }),
            await Element(new NewPayElement("HOUSING", "Housing", PayElementType.Earning, CalculationBasis.FixedAmount, 2, "6100") { FixedAmount = 333.33m, Group = "allowances", SubjectToSocialSecurity = true }),
            await Element(new NewPayElement("TRAVEL", "Travel", PayElementType.Earning, CalculationBasis.FixedAmount, 3, "6100") { FixedAmount = 120m, Group = "ALLOWANCES" }),
            await Element(new NewPayElement("PENSION", "Pension", PayElementType.Deduction, CalculationBasis.Formula, 10, "2410") { Formula = "PENSIONABLE * 5 / 100" }),
            await Element(new NewPayElement("NSSA", "Social security", PayElementType.Deduction, CalculationBasis.Formula, 11, "2475") { Formula = "SOCIALSECURITYBASE * 45 / 1000", Ceiling = 50m }),
            await Element(new NewPayElement("ALLOWLEVY", "Allowance levy", PayElementType.Deduction, CalculationBasis.PercentOfGroup, 12, "2400")
                { Rate = 10m, BasisGroup = "Allowances", RoundTo = 1m, Rounding = PayRounding.Up }),
            await Element(new NewPayElement("LEAVEDAYS", "Leave days accrued", PayElementType.Memo, CalculationBasis.FixedAmount, 20, "6100") { FixedAmount = 2.5m }),
            await Element(new NewPayElement("LEAVEPROV", "Leave pay provision", PayElementType.Provision, CalculationBasis.PercentOfBasic, 21, "2465") { Rate = 5m, ContributionExpenseAccountCode = "6155" }),
        ];
        (await setup.CreatePayElementAsync(companyId, new NewPayElement("BAD", "Bad", PayElementType.Deduction, CalculationBasis.PercentOfGroup, 30, "2400"))).Error.Code
            .Should().Be("Payroll.BasisGroupRequired");

        long employeeId = (await setup.CreateEmployeeAsync(companyId, new NewEmployee("RULES-1", "Rhoda Rules", 2000m, new DateOnly(2025, 1, 1)) { Department = "OPS" })).Value;
        Employee employee = await db.Employees.SingleAsync(e => e.Id == employeeId);
        foreach (long id in ids)
        {
            employee.AssignPayElement(id);
        }

        await db.SaveChangesAsync();
        var run = await scope.ServiceProvider.GetRequiredService<IPayrollService>().RunAsync(companyId, "2026-03", new DateOnly(2026, 3, 25), "2420", "RULES-2026-03");
        run.IsSuccess.Should().BeTrue(run.IsFailure ? run.Error.ToString() : null);

        Payslip slip = await db.Payslips.AsNoTracking().Include(p => p.Lines).SingleAsync(p => p.EmployeeId == employeeId);
        decimal Line(string code) => slip.Lines.Single(l => l.ElementCode == code).Amount;
        Line("PENSION").Should().Be(100m, "5% of pensionable pay (basic only)");
        Line("NSSA").Should().Be(50m, "4.5% of 2,333.33 is 105, held to the ceiling");
        Line("ALLOWLEVY").Should().Be(46m, "10% of the allowances group (453.33) rounded up");
        Line("LEAVEDAYS").Should().Be(2.5m);
        Line("LEAVEPROV").Should().Be(100m);
        slip.GrossEarnings.Should().Be(2453.33m);
        slip.NetPay.Should().Be(2453.33m - 100m - 50m - 46m, "memo and provision lines are not paid");

        List<JournalLine> journal = await db.Journals.AsNoTracking().Where(j => j.CompanyId == companyId && j.DocumentNumber == "RULES-2026-03").SelectMany(j => j.Lines).ToListAsync();
        journal.Single(l => l.AccountCode == "6115").BaseDebit.Should().Be(2000m, "the Operations department's salaries go to its own account");
        journal.Single(l => l.AccountCode == "6155").BaseDebit.Should().Be(100m);
        journal.Single(l => l.AccountCode == "2465").BaseCredit.Should().Be(100m);
        journal.Sum(l => l.BaseDebit).Should().Be(journal.Sum(l => l.BaseCredit));
        journal.Sum(l => l.BaseDebit).Should().Be(2000m + 333.33m + 120m + 100m, "the memo is not posted");
    }
}
