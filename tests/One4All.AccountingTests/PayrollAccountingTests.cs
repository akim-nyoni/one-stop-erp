using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.JobCosting;
using One4All.Application.Abstractions.Payroll;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Core.Payroll;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers period-end payroll accounting: salaries of employees not yet paid for the month are accrued and reversed on the first of the
/// next, and contract timesheet labour is capitalised into WIP once per timesheet.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PayrollAccountingTests
{
    private readonly AccountingFixture _fixture;

    public PayrollAccountingTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Salaries_are_accrued_and_timesheet_labour_capitalised()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.AddRange(new LedgerAccount(companyId, "2492", "Accrued Salaries", AccountType.Liability), new LedgerAccount(companyId, "1491", "Contract WIP", AccountType.Asset));
        Customer client = new(companyId, "PX-CLIENT", "Roads Authority", Scenario.Usd);
        db.Customers.Add(client);
        PayElement basic = new(companyId, "BASIC", "Basic Salary", PayElementType.Earning, CalculationBasis.Formula, 1, "6100") { Formula = "BASIC", Taxable = true };
        db.PayElements.Add(basic);
        await db.SaveChangesAsync();
        Employee monthly = new(companyId, "PX-1", "Mona Monthly", 2000m, new DateOnly(2025, 1, 1));
        Employee weekly = new(companyId, "PX-2", "Wes Weekly", 800m, new DateOnly(2025, 1, 1)) { PayGroup = "WEEKLY" };
        monthly.AssignPayElement(basic.Id);
        weekly.AssignPayElement(basic.Id);
        db.Employees.AddRange(monthly, weekly);
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<IPayrollService>().RunAsync(companyId, "2026-01", new DateOnly(2026, 1, 25), "2420", "PX-01")).IsSuccess.Should().BeTrue();

        // Wes's weekly payroll has not run for January: his salary is accrued and reversed on 1 February.
        IPayrollAccountingService accounting = scope.ServiceProvider.GetRequiredService<IPayrollAccountingService>();
        SalaryAccrual accrual = (await accounting.AccrueSalariesAsync(companyId, new DateOnly(2026, 1, 31), "6100", "2492", "ACC-SAL-01")).Value;
        accrual.Should().Match<SalaryAccrual>(a => a.Employees == 1 && a.Amount == 800m);
        IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
        decimal Balance(TrialBalanceReport tb, string account) => tb.Rows.Where(r => r.AccountCode == account).Sum(r => r.Credit - r.Debit);
        Balance(await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 1, 31)), "2492").Should().Be(800m);
        Balance(await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 2, 28)), "2492").Should().Be(0m);

        // Timesheet labour goes to WIP once, up to the date asked.
        IJobCostingService jobs = scope.ServiceProvider.GetRequiredService<IJobCostingService>();
        long contract = (await jobs.CreateContractAsync(companyId, "PX-JOB", "Bridge", client.Id, 10_000m, 8_000m, 0m, "4300")).Value;
        IContractControlService control = scope.ServiceProvider.GetRequiredService<IContractControlService>();
        (await control.RecordTimeAsync(companyId, contract, monthly.Id, new DateOnly(2026, 1, 20), 8m, 12.5m)).IsSuccess.Should().BeTrue();
        (await control.RecordTimeAsync(companyId, contract, weekly.Id, new DateOnly(2026, 1, 21), 4m, 10m)).IsSuccess.Should().BeTrue();
        (await control.RecordTimeAsync(companyId, contract, weekly.Id, new DateOnly(2026, 2, 3), 6m, 10m)).IsSuccess.Should().BeTrue();
        LabourCapitalised january = (await accounting.CapitaliseTimesheetsAsync(companyId, contract, new DateOnly(2026, 1, 31), "1491", "6100", "LAB-01")).Value;
        january.Should().Match<LabourCapitalised>(c => c.Timesheets == 2 && c.Hours == 12m && c.Amount == 140m);
        (await accounting.CapitaliseTimesheetsAsync(companyId, contract, new DateOnly(2026, 1, 31), "1491", "6100", "LAB-01B")).Error.Code.Should().Be("Payroll.NothingToCapitalise");
        (await accounting.CapitaliseTimesheetsAsync(companyId, contract, new DateOnly(2026, 2, 28), "1491", "6100", "LAB-02")).Value.Amount.Should().Be(60m);
        Balance(await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 2, 28)), "1491").Should().Be(-200m);
    }
}
