using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Banking;
using One4All.Application.Abstractions.Payroll;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Payroll;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers bank payment files from stored layouts: payroll net pay written per beneficiary bank (split pay to its own account, held
/// salaries and non-bank employees left out, a missing account refused), and a supplier payment run written in a bank's layout.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class BankFileTests
{
    private readonly AccountingFixture _fixture;

    public BankFileTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Payroll_and_supplier_payments_are_written_in_the_bank_layout()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        string originator = (await db.Companies.SingleAsync(c => c.Id == companyId)).Code;
        IBankFileService files = scope.ServiceProvider.GetRequiredService<IBankFileService>();
        (await files.SaveLayoutAsync(new BankFileLayoutRequest(companyId, "BAD", "Bad", null, "{Name}{Iban}", null))).Error.Code.Should().Be("Bank.LayoutField");
        (await files.SaveLayoutAsync(new BankFileLayoutRequest(companyId, "fbk", "First Bank EFT", "H|{Date}|{Originator}|{Count}|{Total}", "D|{Seq}|{Reference}|{Name,-12}|{Branch}|{Account}|{Amount,010:cents}",
            "T|{Count}|{Hash}"))).IsSuccess.Should().BeTrue();

        PayElement basic = new(companyId, "BASIC", "Basic Salary", PayElementType.Earning, CalculationBasis.Formula, 1, "6100") { Formula = "BASIC", Taxable = true };
        db.PayElements.Add(basic);
        await db.SaveChangesAsync();
        Employee ann = new(companyId, "EF-1", "Ann Bank", 1000m, new DateOnly(2025, 1, 1)) { BankName = "First Bank", BankBranch = "B1", BankAccountNumber = "111" };
        Employee bob = new(companyId, "EF-2", "Bob Second", 2000m, new DateOnly(2025, 1, 1)) { BankName = "Second Bank", BankAccountNumber = "222" };
        Employee held = new(companyId, "EF-3", "Hal Held", 900m, new DateOnly(2025, 1, 1)) { BankName = "First Bank", BankAccountNumber = "444" };
        Employee wallet = new(companyId, "EF-4", "Mo Wallet", 800m, new DateOnly(2025, 1, 1)) { PaymentMethod = PayMethod.MobileMoney, MobileMoneyNumber = "0771" };
        Employee eve = new(companyId, "EF-5", "Eve Late", 700m, new DateOnly(2025, 1, 1)) { BankName = "First Bank" };
        held.HoldSalary("Query on attendance");
        foreach (Employee e in new[] { ann, bob, held, wallet, eve })
        {
            e.AssignPayElement(basic.Id);
        }

        db.Employees.AddRange(ann, bob, held, wallet, eve);
        await db.SaveChangesAsync();
        db.PaymentSplits.Add(new EmployeePaymentSplit(companyId, bob.Id, PayMethod.BankTransfer, "333", 500m));
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<IPayrollService>().RunAsync(companyId, "2026-09", new DateOnly(2026, 9, 25), "2420", "EF-09")).IsSuccess.Should().BeTrue();

        // Eve is paid by bank but has no account yet.
        (await files.PayrollFilesAsync(companyId, "EF-09", "FBK")).Error.Code.Should().Be("Bank.MissingAccount");
        eve.BankAccountNumber = "555";
        await db.SaveChangesAsync();
        IReadOnlyList<BankFileOutput> payroll = (await files.PayrollFilesAsync(companyId, "EF-09", "FBK")).Value;
        payroll.Select(f => (f.Bank, f.Count, f.Total, f.Hash)).Should().Equal(("First Bank", 2, 1700m, 666L), ("Second Bank", 2, 2000m, 555L));
        payroll[0].Content.Should().Be($"H|20260925|{originator}|2|1700.00\r\nD|1|2026-09 EF-1|Ann Bank    |B1|111|0000100000\r\nD|2|2026-09 EF-5|Eve Late    ||555|0000070000\r\nT|2|666\r\n");
        payroll[1].Content.Should().Contain("D|1|2026-09 EF-2|Bob Second  ||333|0000050000\r\nD|2|2026-09 EF-2|Bob Second  ||222|0000150000\r\n");
        payroll[0].FileName.Should().Be("EF-09-First_Bank-USD.txt");

        // A supplier payment run in the same layout; an unknown layout is refused before anything is paid.
        Supplier supplier = new(companyId, "EF-SUP", "Paper Mill", Scenario.Usd) { BankName = "First Bank", BankAccountNumber = "9876543" };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>().PostInvoiceAsync(new PurchaseInvoiceRequest
        {
            CompanyId = companyId, SupplierId = supplier.Id, DocumentNumber = "EF-P1", DocumentDate = new DateOnly(2026, 9, 1), Currency = Scenario.Usd, NetAmount = 100m, ExpenseAccountCode = "5000",
        })).IsSuccess.Should().BeTrue();
        IPaymentRunService runs = scope.ServiceProvider.GetRequiredService<IPaymentRunService>();
        PaymentRunRequest Run(string layout) => new() { CompanyId = companyId, PaymentDate = new DateOnly(2026, 10, 1), DueOnOrBefore = new DateOnly(2026, 10, 1), BankAccountCode = "1100", EftLayoutCode = layout };
        (await runs.RunAsync(Run("NOPE"))).Error.Code.Should().Be("Bank.LayoutNotFound");
        (await db.OpenItems.Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsPayable).SumAsync(i => i.OutstandingBase)).Should().Be(100m);
        string eft = Encoding.UTF8.GetString((await runs.RunAsync(Run("FBK"))).Value.EftFile);
        eft.Should().StartWith("H|20261001|1100|1|100.00\r\nD|1|").And.Contain("|Paper Mill  ||9876543|0000010000\r\n").And.EndWith("T|1|9876543\r\n");
    }
}
