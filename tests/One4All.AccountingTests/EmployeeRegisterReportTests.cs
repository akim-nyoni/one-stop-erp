using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Payroll;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class EmployeeRegisterReportTests
{
    private readonly AccountingFixture _fixture;

    public EmployeeRegisterReportTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Employee_register_lists_employees_with_department_and_basic()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Employee alice = new(companyId, "HR-1", "Alice Admin", 3_000m, new DateOnly(2025, 1, 1)) { Department = "Finance", CostCentre = "CC-100" };
            Employee bob = new(companyId, "HR-2", "Bob Builder", 2_500m, new DateOnly(2025, 6, 1)) { Department = "Operations" };
            db.Employees.AddRange(alice, bob);
            await db.SaveChangesAsync();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReportDataProvider vertical = scope.ServiceProvider.GetServices<IReportDataProvider>().First(p => p.CanHandle("EmployeeRegister"));
            var rows = await vertical.GetRowsAsync(companyId, "EmployeeRegister", new Dictionary<string, object?>());
            rows.Should().HaveCount(2);
            rows[0]["EmployeeNumber"].Should().Be("HR-1");            // ordered by number
            rows[0]["Department"].Should().Be("Finance");
            rows[0]["BasicSalary"].Should().Be(3_000m);

            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            string html = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "HR-EMP", new Dictionary<string, object?>(), ReportFormat.Html)).Value.Content);
            html.Should().Contain("HR-1").And.Contain("Alice Admin").And.Contain("Operations").And.Contain("2,500.00");
        }
    }
}
