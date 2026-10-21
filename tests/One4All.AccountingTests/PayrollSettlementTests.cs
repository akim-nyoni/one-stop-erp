using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Banking;
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

    [SkippableFact]
    public async Task Net_pay_owed_in_a_currency_is_fixed_at_the_pay_dates_rate_and_paying_later_books_the_exchange_difference()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Core.Accounting.CurrencyCode sek = Core.Accounting.CurrencyCode.Parse("SEK");
        if (!await db.Currencies.AnyAsync(c => c.Code == sek))
        {
            db.Currencies.Add(new Core.System.Currency(sek, "Swedish Krona", "kr"));
        }

        if (!await db.ExchangeRates.AnyAsync(r => r.FromCurrency == sek && r.ToCurrency == Scenario.Usd))
        {
            db.ExchangeRates.Add(new Core.System.ExchangeRate(sek, Scenario.Usd, Core.System.ExchangeRateType.Daily, new DateOnly(2026, 1, 1), 1.25m));
            db.ExchangeRates.Add(new Core.System.ExchangeRate(sek, Scenario.Usd, Core.System.ExchangeRateType.Daily, new DateOnly(2026, 7, 1), 1.30m));
        }

        db.Accounts.Add(new Core.Accounting.LedgerAccount(companyId, "1130", "Bank SEK", Core.Accounting.AccountType.Asset));
        PayElement basic = new(companyId, "BASIC", "Basic Salary", PayElementType.Earning, CalculationBasis.Formula, 1, "6100") { Formula = "BASIC" };
        PayElement krona = new(companyId, "SEKALLOW", "Allowance paid in SEK", PayElementType.Earning, CalculationBasis.FixedAmount, 2, "6100") { FixedAmount = 250m, PaidInCurrency = sek };
        db.PayElements.AddRange(basic, krona);
        await db.SaveChangesAsync();
        Employee dee = new(companyId, "FX-1", "Dee Abroad", 1000m, new DateOnly(2025, 1, 1)) { PaymentMethod = PayMethod.BankTransfer, BankAccountNumber = "333" };
        dee.AssignPayElement(basic.Id);
        dee.AssignPayElement(krona.Id);
        db.Employees.Add(dee);
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<IPayrollService>().RunAsync(companyId, "2026-06", new DateOnly(2026, 6, 25), "2420", "FX-M06")).IsSuccess.Should().BeTrue();

        // 250 base at the pay date's 1.25 is SEK 200 owed, as the payslip shows. Paid on 2 July at 1.30 the SEK 200 cost 260 base: a 10 exchange loss.
        PayrollPayment paid = (await scope.ServiceProvider.GetRequiredService<IPayrollSettlementService>().PayAsync(companyId, "FX-M06", new DateOnly(2026, 7, 2),
            new Dictionary<PayMethod, string> { [PayMethod.BankTransfer] = "1100" }, new Dictionary<Core.Accounting.CurrencyCode, string> { [sek] = "1130" })).Value;
        paid.ByCurrency.Should().Equal((sek, "1130", 200m, 260m));
        string fxLoss = await db.Accounts.Where(a => a.CompanyId == companyId && a.ControlAccountType == Core.Accounting.ControlAccountType.FxLoss).Select(a => a.AccountCode).FirstAsync();
        Core.Accounting.JournalEntry journal = await db.Journals.AsNoTracking().Include(j => j.Lines).SingleAsync(j => j.CompanyId == companyId && j.JournalNumber == paid.JournalNumber);
        journal.Lines.Single(l => l.AccountCode == fxLoss).BaseDebit.Should().Be(10m);
        journal.Lines.Where(l => l.AccountCode == "2420").Sum(l => l.BaseDebit).Should().Be(1_250m, "the net pay accrued is cleared in full");
    }
    [SkippableFact]
    public async Task A_split_paid_in_another_currency_moves_its_share_of_net_pay_into_it()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Core.Accounting.CurrencyCode nok = Core.Accounting.CurrencyCode.Parse("NOK");
        if (!await db.Currencies.AnyAsync(c => c.Code == nok))
        {
            db.Currencies.Add(new Core.System.Currency(nok, "Norwegian Krone", "kr"));
        }

        if (!await db.ExchangeRates.AnyAsync(r => r.FromCurrency == nok && r.ToCurrency == Scenario.Usd))
        {
            db.ExchangeRates.Add(new Core.System.ExchangeRate(nok, Scenario.Usd, Core.System.ExchangeRateType.Daily, new DateOnly(2026, 1, 1), 0.10m));
        }

        db.Accounts.Add(new Core.Accounting.LedgerAccount(companyId, "1140", "Bank NOK", Core.Accounting.AccountType.Asset));
        Core.Security.User login = new($"nk-ola-{companyId}", "Ola Nordmann");
        db.Users.Add(login);
        PayElement basic = new(companyId, "BASIC", "Basic Salary", PayElementType.Earning, CalculationBasis.Formula, 1, "6100") { Formula = "BASIC" };
        db.PayElements.Add(basic);
        await db.SaveChangesAsync();
        Employee ola = new(companyId, "NK-1", "Ola Nordmann", 1000m, new DateOnly(2025, 1, 1))
        {
            UserId = login.Id, PaymentMethod = PayMethod.BankTransfer, BankName = "Home Bank", BankAccountNumber = "777",
        };
        ola.AssignPayElement(basic.Id);
        db.Employees.Add(ola);
        await db.SaveChangesAsync();

        // The split is asked for and approved like any bank-detail change; an unknown currency is refused.
        IPayrollSetupService setup = scope.ServiceProvider.GetRequiredService<IPayrollSetupService>();
        PaymentSplitChange toNordic = new(PayMethod.BankTransfer, "NO-1", null, 40m, "Nordic Bank", "N1", Currency: "nok");
        (await setup.RequestPaymentSplitAsync(companyId, "NK-1", toNordic with { Currency = "QQQ" }, 1)).Error.Code.Should().Be("Payroll.SplitCurrency");
        long request = (await setup.RequestPaymentSplitAsync(companyId, "NK-1", toNordic, 1)).Value;
        (await setup.DecideBankChangeAsync(companyId, request, true, 2, null)).IsSuccess.Should().BeTrue();
        (await db.PaymentSplits.AsNoTracking().SingleAsync(s => s.EmployeeId == ola.Id)).Currency.Should().Be(nok);
        (await scope.ServiceProvider.GetRequiredService<IPayrollService>().RunAsync(companyId, "2026-06", new DateOnly(2026, 6, 25), "2420", "NK-M06")).IsSuccess.Should().BeTrue();

        // 40% of the 1,000 net is 400 base, NOK 4,000 at 0.10, from the NOK account; the other 600 by bank transfer.
        IPayrollSettlementService settlement = scope.ServiceProvider.GetRequiredService<IPayrollSettlementService>();
        Dictionary<PayMethod, string> bank = new() { [PayMethod.BankTransfer] = "1100" };
        (await settlement.PayAsync(companyId, "NK-M06", new DateOnly(2026, 6, 25), bank)).Error.Code.Should().Be("Payroll.CurrencyAccount");
        PayrollPayment paid = (await settlement.PayAsync(companyId, "NK-M06", new DateOnly(2026, 6, 25), bank, new Dictionary<Core.Accounting.CurrencyCode, string> { [nok] = "1140" })).Value;
        paid.ByMethod.Should().Equal((PayMethod.BankTransfer, "1100", 600m));
        paid.ByCurrency.Should().Equal((nok, "1140", 4000m, 400m));
        (await settlement.StageAsync(companyId, "NK-M06")).Value.NetPayOutstanding.Should().Be(0m);

        // The payslip shows the net pay by currency, and the bank file sends NOK 4,000 to Nordic Bank and 600 to the main account.
        string payslip = (await scope.ServiceProvider.GetRequiredService<IEmployeeSelfService>().PayslipTextAsync(login.Id, "2026-06")).Value;
        payslip.Should().Contain("in USD").And.Contain("600.00").And.Contain("in NOK at 0.1 (400.00)").And.Contain("4,000.00");
        IBankFileService files = scope.ServiceProvider.GetRequiredService<IBankFileService>();
        (await files.SaveLayoutAsync(new BankFileLayoutRequest(companyId, "NKB", "Plain EFT", null, "D|{Name}|{Branch}|{Account}|{Amount}", null))).IsSuccess.Should().BeTrue();
        IReadOnlyList<BankFileOutput> payroll = (await files.PayrollFilesAsync(companyId, "NK-M06", "NKB")).Value;
        payroll.Select(f => (f.Bank, f.Currency, f.Count, f.Total)).Should().Equal(("Home Bank", "USD", 1, 600m), ("Nordic Bank", "NOK", 1, 4000m));
    }
}
