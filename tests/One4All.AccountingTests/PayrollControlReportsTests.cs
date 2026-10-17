using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.JobCosting;
using One4All.Application.Abstractions.Payroll;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.FixedAssets;
using One4All.Core.Payroll;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using One4All.Infrastructure.Security;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the payroll control listings (brief §14 O PR22, PR42, PR53, PR63, PR66): a clerk changes their own bank details and
/// then runs the payroll that pays them (two SoD violations); a leaver still holds a laptop, owes a loan and has leave; job
/// timesheets cover part of an employee's pay; the next month is accrued.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PayrollControlReportsTests
{
    private readonly AccountingFixture _fixture;

    public PayrollControlReportsTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Payroll_control_listings_flag_the_exceptions()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        PayElement basic = new(companyId, "BASIC", "Basic Salary", PayElementType.Earning, CalculationBasis.Formula, 1, "6100") { Formula = "BASIC", Taxable = true };
        db.PayElements.Add(basic);
        await db.SaveChangesAsync();
        Employee clerk = new(companyId, "PC-1", "Alice Clerk", 2_000m, new DateOnly(2025, 1, 1)) { UserId = 7, Department = "Finance" };
        Employee leaver = new(companyId, "PC-2", "Bob Leaver", 1_500m, new DateOnly(2025, 1, 1)) { Department = "Stores" };
        clerk.AssignPayElement(basic.Id);
        leaver.AssignPayElement(basic.Id);
        db.Employees.AddRange(clerk, leaver);
        Customer client = new(companyId, "PCCLI", "Client", Scenario.Usd);
        db.Customers.Add(client);
        await db.SaveChangesAsync();
        db.Add(new EmployeeLoan(companyId, leaver.Id, "LN-9", 600m, 100m, new DateOnly(2025, 6, 1), "1400") { DeductFromPay = false });
        db.Add(new EmployeeLeaveBalance(companyId, leaver.Id, "Annual"));
        await db.SaveChangesAsync();
        (await db.Set<EmployeeLeaveBalance>().SingleAsync(l => l.EmployeeId == leaver.Id)).Accrue(10m);
        await db.SaveChangesAsync();

        CurrentUserContext user = scope.ServiceProvider.GetRequiredService<CurrentUserContext>();
        user.SignIn(7, "payclerk", One4All.Core.Security.EffectivePermissions.None, companyId);
        clerk.BankAccountNumber = "999-NEW";
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<IPayrollService>().RunAsync(companyId, "2026-01", new DateOnly(2026, 1, 25), "2420", "PRR-PC-01")).IsSuccess.Should().BeTrue();
        user.SignOut();

        leaver.Terminate(new DateOnly(2026, 1, 31), "Resigned");
        await db.SaveChangesAsync();
        long contract = (await scope.ServiceProvider.GetRequiredService<IJobCostingService>().CreateContractAsync(companyId, "PC-JOB", "Depot", client.Id, 10_000m, 8_000m, 0m, "4300")).Value;
        (await scope.ServiceProvider.GetRequiredService<IContractControlService>().RecordTimeAsync(companyId, contract, clerk.Id, new DateOnly(2026, 1, 10), 40m / 5m, 12.5m)).IsSuccess.Should().BeTrue();
        FixedAsset laptop = new(companyId, "PC-LAP", "Laptop", "IT", 1_000m, 0m, new DateOnly(2025, 6, 1), DepreciationMethod.StraightLine, 36, 0m, "1600", "1650", "6000") { Custodian = "Bob Leaver" };
        db.Add(laptop);
        await db.SaveChangesAsync();

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        async Task<string> Render(string code, DateOnly asAt)
        {
            Dictionary<string, object?> parameters = new() { ["FromDate"] = new DateOnly(2026, 1, 1), ["ToDate"] = new DateOnly(2026, 1, 31), ["AsAtDate"] = asAt };
            var rendered = await runner.RenderAsync(companyId, code, parameters, ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? $"{code}: {rendered.Error.Message}" : null);
            return Encoding.UTF8.GetString(rendered.Value.Content);
        }

        (await Render("PR22", new DateOnly(2026, 1, 31))).Should().Contain("PC-2 Bob Leaver,LN-9,2025-06-01,600.00,100.00,0.00,6,Active,600");
        (await Render("PR42", new DateOnly(2026, 1, 31))).Should().Contain("PC-1 Alice Clerk,PC-JOB,8,100.00,\"2,000.00\",5.0%,1900").And.Contain("PC-2 Bob Leaver,-,0,0.00,\"1,500.00\",0.0%,1500");
        (await Render("PR53", new DateOnly(2026, 2, 28))).Should().Contain("PC-2 Bob Leaver,2026-01-31,Resigned,PC-LAP,10,3,600");
        string accrual = await Render("PR63", new DateOnly(2026, 2, 28));
        accrual.Should().Contain("PC-1 Alice Clerk,Finance,2026-01-25,Not yet paid,2000").And.NotContain("Bob Leaver");
        string sod = await Render("PR66", new DateOnly(2026, 1, 31));
        sod.Should().Contain("Changed pay or payment details then processed the run,PC-1 Alice Clerk: BankAccountNumber").And.Contain("Processed a run containing their own pay,\"PC-1 Alice Clerk net 2,000.00\"");
    }
}
