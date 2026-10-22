using System.Globalization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Payroll;
using One4All.Core.Payroll;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Audit finding 9: where the company's payroll needs approval, a salary or payment-method change - and the bank details given at engagement - wait for
/// a second user, who is not the one who asked; approving applies them, and the decided requests are the dated salary history. Without that setting the
/// changes apply at once, as before, and are still kept in the history.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PayChangeApprovalTests
{
    private const long Clerk = 95;
    private const long Manager = 96;

    private readonly AccountingFixture _fixture;

    public PayChangeApprovalTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Pay_changes_wait_for_a_second_user_where_payroll_needs_approval()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        (await db.Companies.SingleAsync(c => c.Id == companyId)).PayrollApprovalRequired = true;
        await db.SaveChangesAsync();
        IPayrollSetupService setup = scope.ServiceProvider.GetRequiredService<IPayrollSetupService>();
        async Task<Employee> Reload() => await db.Employees.AsNoTracking().SingleAsync(e => e.CompanyId == companyId && e.EmployeeNumber == "PCA-1");

        // Engaged with bank details: the record is created without them; they wait like any bank-detail change.
        (await setup.CreateEmployeeAsync(companyId, new NewEmployee("PCA-1", "Rudo Moyo", 1_000m, new DateOnly(2025, 1, 1))
        {
            BankName = "First Bank", BankAccountNumber = "4455667788", RequestedByUserId = Clerk,
        })).IsSuccess.Should().BeTrue();
        Employee engaged = await Reload();
        (engaged.BankName, engaged.BankAccountNumber).Should().Be((null, null));
        List<BankChangeRequest> held = [.. (await setup.PendingBankChangesAsync(companyId)).Where(r => r.EmployeeNumber == "PCA-1")];
        held.Select(r => (r.Field, r.NewValue)).Should().BeEquivalentTo(new[] { ("BankName", "First Bank"), ("BankAccountNumber", "4455667788") });
        foreach (BankChangeRequest request in held)
        {
            (await setup.DecideBankChangeAsync(companyId, request.RequestId, true, Clerk, null)).Error.Code.Should().Be("Payroll.SelfApproval");
            (await setup.DecideBankChangeAsync(companyId, request.RequestId, true, Manager, "Bank letter seen")).IsSuccess.Should().BeTrue();
        }

        (await Reload()).BankAccountNumber.Should().Be("4455667788");

        // A raise waits for approval; a second one cannot queue behind it.
        (await setup.UpdateEmployeeAsync(companyId, "PCA-1", new EmployeeUpdate { BasicSalary = 1_250m, Grade = "B2", RequestedByUserId = Clerk })).IsSuccess.Should().BeTrue();
        Employee waiting = await Reload();
        (waiting.BasicSalary, waiting.Grade).Should().Be((1_000m, "B2"), "the rest of the record changes; the salary waits");
        BankChangeRequest raise = (await setup.PendingBankChangesAsync(companyId)).Single(r => r.EmployeeNumber == "PCA-1");
        raise.Field.Should().Be("BasicSalary");
        decimal.Parse(raise.OldValue!, CultureInfo.InvariantCulture).Should().Be(1_000m);
        decimal.Parse(raise.NewValue, CultureInfo.InvariantCulture).Should().Be(1_250m);
        raise.EffectiveFrom.Should().NotBeNull();
        (await setup.UpdateEmployeeAsync(companyId, "PCA-1", new EmployeeUpdate { BasicSalary = 1_300m, RequestedByUserId = Clerk })).Error.Code.Should().Be("Payroll.PayChangePending");

        (await setup.DecideBankChangeAsync(companyId, raise.RequestId, true, Clerk, null)).Error.Code.Should().Be("Payroll.SelfApproval");
        (await Reload()).BasicSalary.Should().Be(1_000m);
        (await setup.DecideBankChangeAsync(companyId, raise.RequestId, true, Manager, "Promotion letter")).IsSuccess.Should().BeTrue();
        (await Reload()).BasicSalary.Should().Be(1_250m);

        // A switch to cash likewise; a rejected one leaves the method alone.
        (await setup.UpdateEmployeeAsync(companyId, "PCA-1", new EmployeeUpdate { PaymentMethod = PayMethod.Cash, RequestedByUserId = Clerk })).IsSuccess.Should().BeTrue();
        (await Reload()).PaymentMethod.Should().Be(PayMethod.BankTransfer);
        BankChangeRequest toCash = (await setup.PendingBankChangesAsync(companyId)).Single(r => r.EmployeeNumber == "PCA-1");
        (toCash.Field, toCash.OldValue, toCash.NewValue).Should().Be(("PaymentMethod", "BankTransfer", "Cash"));
        (await setup.DecideBankChangeAsync(companyId, toCash.RequestId, false, Manager, "No reason given")).IsSuccess.Should().BeTrue();
        (await Reload()).PaymentMethod.Should().Be(PayMethod.BankTransfer);
        (await setup.UpdateEmployeeAsync(companyId, "PCA-1", new EmployeeUpdate { PaymentMethod = PayMethod.MobileMoney, RequestedByUserId = Clerk })).IsSuccess.Should().BeTrue();
        long toWallet = (await setup.PendingBankChangesAsync(companyId)).Single(r => r.EmployeeNumber == "PCA-1").RequestId;
        (await setup.DecideBankChangeAsync(companyId, toWallet, true, Clerk, null)).Error.Code.Should().Be("Payroll.SelfApproval");
        (await setup.DecideBankChangeAsync(companyId, toWallet, true, Manager, null)).IsSuccess.Should().BeTrue();
        (await Reload()).PaymentMethod.Should().Be(PayMethod.MobileMoney);

        // A raise dated later is approved now and applied by the first run paid on or after its date.
        (await setup.UpdateEmployeeAsync(companyId, "PCA-1", new EmployeeUpdate { BasicSalary = 1_400m, EffectiveFrom = new DateOnly(2099, 1, 1), RequestedByUserId = Clerk }))
            .IsSuccess.Should().BeTrue();
        long later = (await setup.PendingBankChangesAsync(companyId)).Single(r => r.EmployeeNumber == "PCA-1").RequestId;
        (await setup.DecideBankChangeAsync(companyId, later, true, Manager, null)).IsSuccess.Should().BeTrue();
        (await Reload()).BasicSalary.Should().Be(1_250m);

        // The history: who asked, from what to what, from when, who decided and when it was applied.
        IReadOnlyList<PayChangeRecord> history = await setup.PayChangeHistoryAsync(companyId, "pca-1");
        history.Should().HaveCount(4);
        history.Should().OnlyContain(r => r.RequestedByUserId == Clerk && r.DecidedByUserId == Manager && r.EffectiveFrom != null);
        history.Should().ContainSingle(r => r.Field == "BasicSalary" && r.Status == SelfServiceStatus.Approved && r.AppliedOn != null && r.Note == "Promotion letter");
        history.Should().ContainSingle(r => r.Field == "BasicSalary" && r.EffectiveFrom == new DateOnly(2099, 1, 1) && r.AppliedOn == null);
        history.Should().ContainSingle(r => r.Field == "PaymentMethod" && r.Status == SelfServiceStatus.Rejected && r.NewValue == "Cash");
        history.Should().ContainSingle(r => r.Field == "PaymentMethod" && r.Status == SelfServiceStatus.Approved && r.NewValue == "MobileMoney");
    }

    [SkippableFact]
    public async Task Without_payroll_approval_pay_changes_apply_at_once_and_are_kept()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        IPayrollSetupService setup = scope.ServiceProvider.GetRequiredService<IPayrollSetupService>();
        (await setup.CreateEmployeeAsync(companyId, new NewEmployee("PCN-1", "Farai Dube", 800m, new DateOnly(2025, 1, 1)) { BankAccountNumber = "1234", RequestedByUserId = Clerk }))
            .IsSuccess.Should().BeTrue();
        (await setup.UpdateEmployeeAsync(companyId, "PCN-1", new EmployeeUpdate { BasicSalary = 900m, PaymentMethod = PayMethod.Cash, RequestedByUserId = Clerk })).IsSuccess.Should().BeTrue();

        Employee employee = await db.Employees.AsNoTracking().SingleAsync(e => e.CompanyId == companyId && e.EmployeeNumber == "PCN-1");
        (employee.BasicSalary, employee.PaymentMethod, employee.BankAccountNumber).Should().Be((900m, PayMethod.Cash, "1234"));
        (await setup.PendingBankChangesAsync(companyId)).Should().BeEmpty();
        IReadOnlyList<PayChangeRecord> history = await setup.PayChangeHistoryAsync(companyId, "PCN-1");
        history.Should().HaveCount(2).And.OnlyContain(r => r.Status == SelfServiceStatus.Approved && r.DecidedByUserId == Clerk && r.AppliedOn != null);
    }
}
