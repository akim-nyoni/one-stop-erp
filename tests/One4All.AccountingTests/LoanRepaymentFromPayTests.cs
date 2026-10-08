using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Payroll;
using One4All.Core.Accounting;
using One4All.Core.Payroll;
using One4All.Core.Security;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers staff loans repaid from pay (brief §4.15.5): each run charges the period's interest and deducts the instalment, never taking net pay
/// below zero; a suspended loan is not deducted; reversing the run puts the repayment back and takes the interest off.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class LoanRepaymentFromPayTests
{
    private readonly AccountingFixture _fixture;

    public LoanRepaymentFromPayTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Runs_charge_interest_and_deduct_instalments_and_a_reversal_undoes_them()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.AddRange(new LedgerAccount(companyId, "1310", "Staff Loans", AccountType.Asset), new LedgerAccount(companyId, "4460", "Staff Loan Interest", AccountType.Income));
        Company company = await db.Companies.SingleAsync(c => c.Id == companyId);
        company.StaffLoanInterestAccountCode = "4460";
        PayElement basic = new(companyId, "BASIC", "Basic", PayElementType.Earning, CalculationBasis.Formula, 1, "6100") { Formula = "BASIC", Taxable = true };
        db.PayElements.Add(basic);
        Employee borrower = new(companyId, "LR-1", "Lou Borrower", 1000m, new DateOnly(2025, 1, 1));
        Employee stretched = new(companyId, "LR-2", "Stan Stretched", 200m, new DateOnly(2025, 1, 1));
        Employee resting = new(companyId, "LR-3", "Ria Resting", 1000m, new DateOnly(2025, 1, 1));
        db.Employees.AddRange(borrower, stretched, resting);
        User approver = new($"lr-approver-{companyId}", "Approver");
        db.Users.Add(approver);
        await db.SaveChangesAsync();
        foreach (Employee e in new[] { borrower, stretched, resting })
        {
            e.AssignPayElement(basic.Id);
        }

        EmployeeLoan loan = new(companyId, borrower.Id, "LN-1", 1200m, 300m, new DateOnly(2025, 12, 1), "1310") { InterestRatePercent = 12m };
        EmployeeLoan tight = new(companyId, stretched.Id, "LN-2", 900m, 300m, new DateOnly(2025, 12, 1), "1310");
        EmployeeLoan paused = new(companyId, resting.Id, "LN-3", 500m, 100m, new DateOnly(2025, 12, 1), "1310") { Suspended = true };
        db.EmployeeLoans.AddRange(loan, tight, paused);
        await db.SaveChangesAsync();

        (await scope.ServiceProvider.GetRequiredService<IPayrollService>().RunAsync(companyId, "2026-01", new DateOnly(2026, 1, 25), "2420", "LR-2026-01")).IsSuccess.Should().BeTrue();
        List<Payslip> slips = await db.Payslips.AsNoTracking().Include(p => p.Lines).Where(p => p.CompanyId == companyId).ToListAsync();
        Payslip lou = slips.Single(p => p.EmployeeId == borrower.Id);
        lou.Lines.Single(l => l.ElementCode == $"LOANINT-{loan.Id}").Amount.Should().Be(12m, "1% a month on 1,200");
        lou.Lines.Single(l => l.ElementCode == $"LOAN-{loan.Id}").Amount.Should().Be(300m);
        lou.Lines.Single(l => l.ElementCode == $"LOAN-{loan.Id}").Workings.Should().Contain("instalment 300.00 on a balance of 1,212.00");
        lou.NetPay.Should().Be(700m);
        slips.Single(p => p.EmployeeId == stretched.Id).NetPay.Should().Be(0m, "the instalment is held to what pay can bear");
        slips.Single(p => p.EmployeeId == resting.Id).Lines.Should().NotContain(l => l.ElementCode.StartsWith("LOAN", StringComparison.Ordinal), "a suspended loan is not deducted");

        await using (AsyncServiceScope check = provider.CreateAsyncScope())
        {
            One4AllDbContext fresh = check.ServiceProvider.GetRequiredService<One4AllDbContext>();
            Dictionary<long, decimal> balances = await fresh.EmployeeLoans.AsNoTracking().Where(l => l.CompanyId == companyId).ToDictionaryAsync(l => l.Id, l => l.Outstanding);
            balances[loan.Id].Should().Be(912m, "1,200 plus 12 interest less 300");
            balances[tight.Id].Should().Be(700m);
            balances[paused.Id].Should().Be(500m);
            List<JournalLine> journal = await fresh.Journals.AsNoTracking().Where(j => j.CompanyId == companyId && j.DocumentNumber == "LR-2026-01").SelectMany(j => j.Lines).ToListAsync();
            journal.Single(l => l.AccountCode == "4460").BaseCredit.Should().Be(12m);
            journal.Where(l => l.AccountCode == "1310").Sum(l => l.BaseCredit - l.BaseDebit).Should().Be(300m + 200m - 12m);
        }

        (await scope.ServiceProvider.GetRequiredService<IPayrollAdjustmentService>().ReverseRunAsync(companyId, "LR-2026-01", new DateOnly(2026, 1, 28), "Wrong month", approver.Id))
            .IsSuccess.Should().BeTrue();
        await using (AsyncServiceScope after = provider.CreateAsyncScope())
        {
            Dictionary<long, decimal> restored = await after.ServiceProvider.GetRequiredService<One4AllDbContext>().EmployeeLoans.AsNoTracking().Where(l => l.CompanyId == companyId)
                .ToDictionaryAsync(l => l.Id, l => l.Outstanding);
            restored[loan.Id].Should().Be(1200m);
            restored[tight.Id].Should().Be(900m);
        }
    }

    [Fact]
    public void The_instalment_never_exceeds_the_balance_with_its_interest()
    {
        EmployeeLoan.Instalment(1200m, 300m, 12m, 12).Should().Be((12m, 300m));
        EmployeeLoan.Instalment(100m, 300m, 12m, 12).Should().Be((1m, 101m));
        EmployeeLoan.Instalment(5200m, 300m, 0m, 52).Should().Be((0m, 300m));
    }
}
