using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Payroll;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Payroll;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class EmployeeLoanReportTests
{
    private readonly AccountingFixture _fixture;

    public EmployeeLoanReportTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Loan_report_lists_only_active_loans_with_their_balances()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long empId;
        long settledLoanId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Employee emp = new(companyId, "LR-1", "Lena Reporter", 4_000m, new DateOnly(2025, 1, 1));
            db.Set<Employee>().Add(emp);
            await db.SaveChangesAsync();
            empId = emp.Id;

            IEmployeeLoanService loans = scope.ServiceProvider.GetRequiredService<IEmployeeLoanService>();
            // An active loan (partly repaid) and a fully-settled loan.
            long active = (await loans.GrantAsync(companyId, empId, "LN-ACTIVE", 900m, 300m, new DateOnly(2026, 1, 10), "1350", "1100", "L-A")).Value;
            await loans.RecordRepaymentAsync(companyId, active, 300m, new DateOnly(2026, 2, 28), "2420", "R-A1"); // 600 left
            settledLoanId = (await loans.GrantAsync(companyId, empId, "LN-DONE", 200m, 200m, new DateOnly(2026, 1, 10), "1350", "1100", "L-D")).Value;
            await loans.RecordRepaymentAsync(companyId, settledLoanId, 200m, new DateOnly(2026, 2, 28), "2420", "R-D1"); // settled
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            string loans = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "PAY-LOANS", new Dictionary<string, object?>(), ReportFormat.Html)).Value.Content);
            loans.Should().Contain("Lena Reporter").And.Contain("LN-ACTIVE");
            loans.Should().Contain("600.00");           // outstanding on the active loan
            loans.Should().NotContain("LN-DONE");        // the settled loan is excluded
        }
    }
}
