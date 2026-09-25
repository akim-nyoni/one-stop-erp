using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Expenses;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Payroll;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class ExpenseClaimReportTests
{
    private readonly AccountingFixture _fixture;

    public ExpenseClaimReportTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Expense_claims_report_lists_claims_with_total_and_status()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Employee emp = new(companyId, "E-300", "Cara Claimant", 4_000m, new DateOnly(2025, 1, 1));
            db.Set<Employee>().Add(emp);
            await db.SaveChangesAsync();

            IExpenseClaimService claims = scope.ServiceProvider.GetRequiredService<IExpenseClaimService>();
            var reimbursed = await claims.CreateAsync(companyId, emp.Id, "EXP-R", new DateOnly(2026, 3, 5),
                [new ExpenseClaimLineRequest("Client lunch", "6700", 120m), new ExpenseClaimLineRequest("Paper", "6800", 30m)]);
            await claims.ApproveAsync(companyId, reimbursed.Value, approvedByUserId: 1);
            await claims.ReimburseAsync(companyId, reimbursed.Value, new DateOnly(2026, 3, 10), "1100", "PAY-1");

            // A second, still-draft claim.
            await claims.CreateAsync(companyId, emp.Id, "EXP-D", new DateOnly(2026, 3, 8),
                [new ExpenseClaimLineRequest("Taxi", "6700", 45m)]);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            string html = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "EXP-CLAIMS", new Dictionary<string, object?>(), ReportFormat.Html)).Value.Content);
            html.Should().Contain("EXP-R").And.Contain("Cara Claimant");
            html.Should().Contain("Reimbursed").And.Contain("150.00");
            html.Should().Contain("EXP-D").And.Contain("Draft").And.Contain("45.00");
        }
    }
}
