using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Payroll;
using One4All.Core.Payroll;
using One4All.Core.Security;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers employee self-service: the signed-in user sees only their own payslips, tax certificate and leave; leave applications and
/// detail changes wait for HR, nobody decides their own, and approval takes the leave or applies the change.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class EmployeeSelfServiceTests
{
    private readonly AccountingFixture _fixture;

    public EmployeeSelfServiceTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Employees_serve_themselves_and_HR_decides()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        User login = new($"ess-zoe-{companyId}", "Zoe Staff"), hr = new($"ess-hr-{companyId}", "HR Officer"), stranger = new($"ess-x-{companyId}", "Contractor");
        db.Users.AddRange(login, hr, stranger);
        PayElement basic = new(companyId, "BASIC", "Basic Salary", PayElementType.Earning, CalculationBasis.Formula, 1, "6100") { Formula = "BASIC", Taxable = true };
        PayElement paye = new(companyId, "PAYE", "PAYE", PayElementType.Deduction, CalculationBasis.Formula, 2, "2400") { Formula = "TAXABLE * 10 / 100" };
        db.PayElements.AddRange(basic, paye);
        await db.SaveChangesAsync();
        Employee zoe = new(companyId, "ES-1", "Zoe Staff", 1000m, new DateOnly(2025, 1, 1)) { UserId = login.Id, BankName = "First Bank", BankAccountNumber = "123456789" };
        zoe.AssignPayElement(basic.Id);
        zoe.AssignPayElement(paye.Id);
        db.Employees.Add(zoe);
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<ILeaveService>().AccrueAsync(companyId, zoe.Id, "ANNUAL", 10m)).IsSuccess.Should().BeTrue();
        IPayrollService payroll = scope.ServiceProvider.GetRequiredService<IPayrollService>();
        (await payroll.RunAsync(companyId, "2026-01", new DateOnly(2026, 1, 25), "2420", "ES-01")).IsSuccess.Should().BeTrue();
        (await payroll.RunAsync(companyId, "2026-02", new DateOnly(2026, 2, 25), "2420", "ES-02")).IsSuccess.Should().BeTrue();

        IEmployeeSelfService ess = scope.ServiceProvider.GetRequiredService<IEmployeeSelfService>();
        (await ess.ProfileAsync(stranger.Id)).Error.Code.Should().Be("Ess.NotAnEmployee");
        (await ess.ProfileAsync(login.Id)).Value.Should().Match<SelfServiceProfile>(p => p.EmployeeNumber == "ES-1" && p.BankAccountMasked == "*****6789");
        (await ess.PayslipsAsync(login.Id)).Value.Select(p => (p.PeriodLabel, p.Net)).Should().Equal(("2026-02", 900m), ("2026-01", 900m));
        (await ess.PayslipTextAsync(login.Id, "2026-01")).Value.Should().Contain("Basic Salary").And.Contain("900.00");
        (await ess.PayslipTextAsync(login.Id, "2025-12")).Error.Code.Should().Be("Ess.NoPayslip");
        (await ess.TaxCertificateAsync(login.Id, 2026)).Value.Should().Match<TaxCertificate>(t => t.Gross == 2000m && t.Taxable == 2000m && t.TaxDeducted == 200m);

        // Leave: applications count against the balance before HR decides; nobody decides their own.
        long application = (await ess.ApplyForLeaveAsync(login.Id, "annual", new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 9), 6m)).Value;
        (await ess.ApplyForLeaveAsync(login.Id, "ANNUAL", new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 7), 5m)).Error.Code.Should().Be("Ess.LeaveBalance");
        (await ess.LeaveBalancesAsync(login.Id)).Value.Should().ContainSingle(b => b.LeaveType == "ANNUAL" && b.Balance == 10m && b.Pending == 6m);
        (await ess.DecideLeaveAsync(companyId, application, true, login.Id, null)).Error.Code.Should().Be("Ess.OwnRequest");
        (await ess.DecideLeaveAsync(companyId, application, true, hr.Id, "Enjoy")).Value.Should().Be(SelfServiceStatus.Approved);
        (await ess.LeaveBalancesAsync(login.Id)).Value.Should().ContainSingle(b => b.Balance == 4m && b.Pending == 0m);

        // Detail changes: only payment details, applied by HR.
        (await ess.RequestDetailChangeAsync(login.Id, "BasicSalary", "5000")).Error.Code.Should().Be("Ess.Field");
        long change = (await ess.RequestDetailChangeAsync(login.Id, "bankaccountnumber", "999888777")).Value;
        (await ess.PendingAsync(companyId)).Should().ContainSingle(r => r.Kind == "Detail" && r.Summary == "BankAccountNumber: 123456789 -> 999888777");
        (await ess.DecideDetailChangeAsync(companyId, change, true, login.Id, null)).Error.Code.Should().Be("Ess.OwnRequest");
        (await ess.DecideDetailChangeAsync(companyId, change, true, hr.Id, "Letter from bank seen")).Value.Should().Be(SelfServiceStatus.Approved);
        (await db.Employees.AsNoTracking().SingleAsync(e => e.Id == zoe.Id)).BankAccountNumber.Should().Be("999888777");
        (await ess.PendingAsync(companyId)).Should().BeEmpty();
    }
}
