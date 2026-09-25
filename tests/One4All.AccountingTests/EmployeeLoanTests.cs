using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Payroll;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Payroll;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class EmployeeLoanTests
{
    private readonly AccountingFixture _fixture;

    public EmployeeLoanTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Loan_advance_and_repayments_move_the_receivable_and_settle()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long employeeId;
        long loanId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Employee emp = new(companyId, "E-100", "Sam Staff", 5_000m, new DateOnly(2025, 1, 1));
            db.Set<Employee>().Add(emp);
            await db.SaveChangesAsync();
            employeeId = emp.Id;

            IEmployeeLoanService loans = scope.ServiceProvider.GetRequiredService<IEmployeeLoanService>();
            // Advance a 1,200 loan repayable at 400/month.
            var grant = await loans.GrantAsync(companyId, employeeId, "LN-1", 1_200m, 400m, new DateOnly(2026, 1, 15), "1350", "1100", "LOAN-1");
            grant.IsSuccess.Should().BeTrue(grant.IsFailure ? grant.Error.ToString() : null);
            loanId = grant.Value;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IEmployeeLoanService loans = scope.ServiceProvider.GetRequiredService<IEmployeeLoanService>();
            // Two repayments of 400 (e.g. from payroll deductions transferred from net-pay control 2420).
            (await loans.RecordRepaymentAsync(companyId, loanId, 400m, new DateOnly(2026, 2, 28), "2420", "REP-1")).Value.Should().Be(800m);
            (await loans.RecordRepaymentAsync(companyId, loanId, 400m, new DateOnly(2026, 3, 31), "2420", "REP-2")).Value.Should().Be(400m);

            // Over-repayment is refused.
            (await loans.RecordRepaymentAsync(companyId, loanId, 500m, new DateOnly(2026, 4, 30), "2420", "REP-X")).IsFailure.Should().BeTrue();

            // Final 400 settles it.
            (await loans.RecordRepaymentAsync(companyId, loanId, 400m, new DateOnly(2026, 4, 30), "2420", "REP-3")).Value.Should().Be(0m);

            // A repayment against the settled loan is refused.
            (await loans.RecordRepaymentAsync(companyId, loanId, 1m, new DateOnly(2026, 5, 31), "2420", "REP-Y")).IsFailure.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            EmployeeLoan loan = await db.Set<EmployeeLoan>().FirstAsync(l => l.Id == loanId);
            loan.Status.Should().Be(EmployeeLoanStatus.Settled);
            loan.Outstanding.Should().Be(0m);

            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 4, 30));
            tb.IsBalanced.Should().BeTrue();
            // Loan receivable fully repaid: 1200 Dr (advance) − 1200 Cr (repayments) = 0.
            tb.Rows.Should().NotContain(r => r.AccountCode == "1350" && (r.Debit != 0m || r.Credit != 0m));
        }
    }
}
