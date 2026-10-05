using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Payroll;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Core.Payroll;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Employer contributions reach the ledger: a run with a contribution but no expense account is refused (it used to be
/// left off silently); with one, the run charges the cost to the expense account and owes it on the element's liability.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class EmployerContributionPostingTests
{
    private readonly AccountingFixture _fixture;

    public EmployerContributionPostingTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Employer_contributions_are_charged_and_owed()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long contributionId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            db.Accounts.Add(new LedgerAccount(companyId, "6105", "Employer Pension Contributions", AccountType.Expense));
            PayElement basic = new(companyId, "BASIC", "Basic Salary", PayElementType.Earning, CalculationBasis.Formula, 1, "6100") { Formula = "BASIC", Taxable = true };
            PayElement employer = new(companyId, "ERPEN", "Employer pension (10%)", PayElementType.EmployerContribution, CalculationBasis.PercentOfBasic, 2, "2410") { Rate = 10m };
            db.PayElements.AddRange(basic, employer);
            await db.SaveChangesAsync();
            Employee employee = new(companyId, "EC-EMP", "Erin Employer", 2000m, new DateOnly(2025, 1, 1));
            employee.AssignPayElement(basic.Id);
            employee.AssignPayElement(employer.Id);
            db.Employees.Add(employee);
            await db.SaveChangesAsync();
            contributionId = employer.Id;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<IPayrollService>().RunAsync(companyId, "2026-01", new DateOnly(2026, 1, 25), "2420", "EC-2026-01"))
                .Error.Code.Should().Be("Payroll.ContributionAccount");
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            (await db.PayElements.FindAsync(contributionId))!.ContributionExpenseAccountCode = "6105";
            await db.SaveChangesAsync();
            (await scope.ServiceProvider.GetRequiredService<IPayrollService>().RunAsync(companyId, "2026-01", new DateOnly(2026, 1, 25), "2420", "EC-2026-01")).IsSuccess.Should().BeTrue();

            ConsolidatedTrialBalance tb = (await scope.ServiceProvider.GetRequiredService<IConsolidationService>().ConsolidatedTrialBalanceAsync([companyId], new DateOnly(2026, 1, 31))).Value;
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Single(r => r.AccountCode == "6105").Debit.Should().Be(200m);
            tb.Rows.Single(r => r.AccountCode == "2410").Credit.Should().Be(200m);
            tb.Rows.Single(r => r.AccountCode == "6100").Debit.Should().Be(2000m);
            tb.Rows.Single(r => r.AccountCode == "2420").Credit.Should().Be(2000m); // net pay is unaffected by the employer's cost
        }
    }
}
