using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Payroll;
using One4All.Core.Payroll;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Fourteenth audit #2: splitting net pay to another account is a bank-detail change - asked for by one user, applied only when another approves it.</summary>
[Collection(AccountingCollection.Name)]
public sealed class PaymentSplitApprovalTests
{
    private const long Clerk = 93;
    private const long Manager = 94;

    private readonly AccountingFixture _fixture;

    public PaymentSplitApprovalTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_split_waits_for_a_second_user()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        IPayrollSetupService setup = scope.ServiceProvider.GetRequiredService<IPayrollSetupService>();
        long employee = (await setup.CreateEmployeeAsync(companyId, new NewEmployee("PSA-1", "Tatenda Gumbo", 1_000m, new DateOnly(2024, 1, 1)))).Value;

        PaymentSplitChange toWallet = new(PayMethod.MobileMoney, "0771234567", null, 40m, null, null);
        (await setup.RequestPaymentSplitAsync(companyId, "PSA-1", toWallet with { Percent = 140m }, Clerk)).Error.Code.Should().Be("Payroll.Split");
        long request = (await setup.RequestPaymentSplitAsync(companyId, "PSA-1", toWallet, Clerk)).Value;
        (await setup.RequestPaymentSplitAsync(companyId, "PSA-1", toWallet, Clerk)).Error.Code.Should().Be("Payroll.SplitPending");
        (await db.PaymentSplits.AsNoTracking().AnyAsync(s => s.EmployeeId == employee)).Should().BeFalse("nothing changes before approval");
        (await setup.PendingBankChangesAsync(companyId)).Should().ContainSingle(r => r.RequestId == request && r.NewValue == "MobileMoney 0771234567 40%");

        (await setup.DecideBankChangeAsync(companyId, request, true, Clerk, null)).Error.Code.Should().Be("Payroll.SelfApproval");
        (await setup.DecideBankChangeAsync(companyId, request, true, Manager, "Letter on file")).IsSuccess.Should().BeTrue();
        EmployeePaymentSplit split = await db.PaymentSplits.AsNoTracking().SingleAsync(s => s.EmployeeId == employee);
        (split.Method, split.Destination, split.Percent).Should().Be((PayMethod.MobileMoney, "0771234567", (decimal?)40m));

        long removal = (await setup.RequestPaymentSplitAsync(companyId, "PSA-1", toWallet with { Remove = true }, Clerk)).Value;
        (await setup.DecideBankChangeAsync(companyId, removal, true, Manager, null)).IsSuccess.Should().BeTrue();
        (await db.PaymentSplits.AsNoTracking().AnyAsync(s => s.EmployeeId == employee)).Should().BeFalse();
        (await setup.RequestPaymentSplitAsync(companyId, "PSA-1", toWallet with { Remove = true }, Clerk)).Error.Code.Should().Be("Payroll.SplitNotFound");
    }
}
