using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Payroll;
using One4All.Core.Payroll;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the Pay and Close stages: a committed run with one employee paid by bank transfer and one in cash is paid from the bank and the cash account,
/// clearing the net pay control, then closed; a run cannot close before it is paid, be paid twice, or close twice.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PayrollSettlementTests
{
    private readonly AccountingFixture _fixture;

    public PayrollSettlementTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_committed_run_is_paid_then_closed()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        PayElement basic = new(companyId, "BASIC", "Basic Salary", PayElementType.Earning, CalculationBasis.Formula, 1, "6100") { Formula = "BASIC" };
        db.PayElements.Add(basic);
        await db.SaveChangesAsync();
        Employee ann = new(companyId, "PS-1", "Ann Bank", 1000m, new DateOnly(2025, 1, 1)) { PaymentMethod = PayMethod.BankTransfer, BankAccountNumber = "111" };
        Employee ben = new(companyId, "PS-2", "Ben Cash", 600m, new DateOnly(2025, 1, 1)) { PaymentMethod = PayMethod.Cash };
        ann.AssignPayElement(basic.Id);
        ben.AssignPayElement(basic.Id);
        db.Employees.AddRange(ann, ben);
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<IPayrollService>().RunAsync(companyId, "2026-03", new DateOnly(2026, 3, 25), "2420", "PS-M03")).IsSuccess.Should().BeTrue();

        IPayrollSettlementService settlement = scope.ServiceProvider.GetRequiredService<IPayrollSettlementService>();
        (await settlement.StageAsync(companyId, "PS-M03")).Value.Should().Be(new PayrollRunStage("PS-M03", 1600m, null, null, 1600m));
        (await settlement.CloseAsync(companyId, "PS-M03", new DateOnly(2026, 3, 26))).Error.Code.Should().Be("Payroll.RunNotPaid");
        (await settlement.PayAsync(companyId, "PS-M03", new DateOnly(2026, 3, 25), new Dictionary<PayMethod, string> { [PayMethod.BankTransfer] = "1100" }))
            .Error.Code.Should().Be("Payroll.FundingAccount", "the cash pay needs an account too");

        PayrollPayment paid = (await settlement.PayAsync(companyId, "PS-M03", new DateOnly(2026, 3, 25),
            new Dictionary<PayMethod, string> { [PayMethod.BankTransfer] = "1100", [PayMethod.Cash] = "1000" })).Value;
        paid.ByMethod.Should().Equal((PayMethod.BankTransfer, "1100", 1000m), (PayMethod.Cash, "1000", 600m));
        (await settlement.PayAsync(companyId, "PS-M03", new DateOnly(2026, 3, 25), new Dictionary<PayMethod, string> { [PayMethod.BankTransfer] = "1100", [PayMethod.Cash] = "1000" }))
            .Error.Code.Should().Be("Payroll.RunPaid");

        PayrollRunStage closed = (await settlement.CloseAsync(companyId, "PS-M03", new DateOnly(2026, 3, 26))).Value;
        (closed.PaidOn, closed.ClosedOn, closed.NetPayOutstanding).Should().Be(((DateOnly?)new DateOnly(2026, 3, 25), (DateOnly?)new DateOnly(2026, 3, 26), 0m));
        (await settlement.CloseAsync(companyId, "PS-M03", new DateOnly(2026, 3, 27))).Error.Code.Should().Be("Payroll.RunClosed");
        (await db.Set<PayrollRunRecord>().AsNoTracking().SingleAsync(r => r.CompanyId == companyId && r.DocumentNumber == "PS-M03")).PaymentJournalNumber.Should().Be(paid.JournalNumber);

        // The designable payslip: one employee's lines with the slip's totals, ready for a company's own layout.
        await scope.ServiceProvider.GetRequiredService<One4All.Infrastructure.Persistence.Seeding.ReportCatalogueSeeder>().SeedAsync();
        var payslip = await scope.ServiceProvider.GetRequiredService<One4All.Application.Abstractions.Reporting.IReportRunner>().RenderAsync(companyId, "PR74",
            new Dictionary<string, object?> { ["FromDate"] = new DateOnly(2026, 3, 1), ["ToDate"] = new DateOnly(2026, 3, 31), ["EmployeeNumber"] = "ps-1" },
            One4All.Application.Abstractions.Reporting.ReportFormat.Csv);
        payslip.IsSuccess.Should().BeTrue(payslip.IsFailure ? payslip.Error.Message : null);
        string csv = System.Text.Encoding.UTF8.GetString(payslip.Value.Content);
        csv.Should().Contain("Ann Bank,PS-1").And.Contain("Earnings,BASIC").And.NotContain("Ben Cash");
    }

    [SkippableFact]
    public async Task Earnings_paid_in_another_currency_are_paid_from_its_account()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.Add(new Core.Accounting.LedgerAccount(companyId, "1120", "Bank EUR", Core.Accounting.AccountType.Asset));
        PayElement basic = new(companyId, "BASIC", "Basic Salary", PayElementType.Earning, CalculationBasis.Formula, 1, "6100") { Formula = "BASIC" };
        PayElement euro = new(companyId, "EURALLOW", "Allowance paid in EUR", PayElementType.Earning, CalculationBasis.FixedAmount, 2, "6100") { FixedAmount = 220m, PaidInCurrency = Scenario.Eur };
        db.PayElements.AddRange(basic, euro);
        await db.SaveChangesAsync();
        Employee cy = new(companyId, "MC-1", "Cy Expat", 1000m, new DateOnly(2025, 1, 1)) { PaymentMethod = PayMethod.BankTransfer, BankAccountNumber = "222" };
        cy.AssignPayElement(basic.Id);
        cy.AssignPayElement(euro.Id);
        db.Employees.Add(cy);
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<IPayrollService>().RunAsync(companyId, "2026-03", new DateOnly(2026, 3, 25), "2420", "MC-M03")).IsSuccess.Should().BeTrue();

        IPayrollSettlementService settlement = scope.ServiceProvider.GetRequiredService<IPayrollSettlementService>();
        Dictionary<PayMethod, string> bank = new() { [PayMethod.BankTransfer] = "1100" };
        (await settlement.PayAsync(companyId, "MC-M03", new DateOnly(2026, 3, 25), bank)).Error.Code.Should().Be("Payroll.CurrencyAccount");

        // 220 base of EUR earnings at 1.10 = EUR 200 from the EUR account; the other 1,000 by bank transfer.
        PayrollPayment paid = (await settlement.PayAsync(companyId, "MC-M03", new DateOnly(2026, 3, 25), bank,
            new Dictionary<Core.Accounting.CurrencyCode, string> { [Scenario.Eur] = "1120" })).Value;
        paid.ByMethod.Should().Equal((PayMethod.BankTransfer, "1100", 1000m));
        paid.ByCurrency.Should().Equal((Scenario.Eur, "1120", 200m, 220m));
        (await settlement.StageAsync(companyId, "MC-M03")).Value.NetPayOutstanding.Should().Be(0m);
    }
}
