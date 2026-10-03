using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Banking;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Banking;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers posting cash-book entries from statement lines: a receipt to a customer, a payment to a supplier and
/// bank charges to an expense account, each matched in the same step so the reconciliation difference is nil;
/// wrong-direction, already-reconciled and control-account postings are refused with nothing posted.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class BankStatementPostingTests
{
    private readonly AccountingFixture _fixture;

    public BankStatementPostingTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Statement_lines_post_to_customers_suppliers_and_accounts_and_reconcile()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long bankAccountId;
        long customerId;
        long supplierId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            BankAccount bank = new(companyId, "BSP-BANK", "Current", Scenario.Usd, "1100");
            db.Set<BankAccount>().Add(bank);
            Customer customer = new(companyId, "BSP-C", "Paying Customer", Scenario.Usd);
            db.Customers.Add(customer);
            Supplier supplier = new(companyId, "BSP-S", "Debit-order Supplier", Scenario.Usd);
            db.Suppliers.Add(supplier);
            await db.SaveChangesAsync();
            bankAccountId = bank.Id;
            customerId = customer.Id;
            supplierId = supplier.Id;

            IBankReconciliationService recon = scope.ServiceProvider.GetRequiredService<IBankReconciliationService>();
            (await recon.ImportStatementLinesAsync(companyId, bankAccountId,
            [
                new StatementLineImport(new DateOnly(2026, 3, 5), "EFT from customer", 640m, "RCPT-77"),
                new StatementLineImport(new DateOnly(2026, 3, 8), "Debit order", -215m, null),
                new StatementLineImport(new DateOnly(2026, 3, 31), "Monthly service fee", -12.50m, null),
            ])).IsSuccess.Should().BeTrue();
        }

        long receiptLine;
        long debitOrderLine;
        long feeLine;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            List<BankStatementLine> lines = await db.Set<BankStatementLine>().Where(l => l.BankAccountId == bankAccountId).OrderBy(l => l.StatementDate).ToListAsync();
            receiptLine = lines[0].Id;
            debitOrderLine = lines[1].Id;
            feeLine = lines[2].Id;

            IBankStatementPostingService posting = scope.ServiceProvider.GetRequiredService<IBankStatementPostingService>();

            // Wrong direction and control-account postings are refused.
            (await posting.PostCustomerReceiptAsync(companyId, bankAccountId, debitOrderLine, customerId)).Error.Code.Should().Be("Bank.NotMoneyIn");
            (await posting.PostSupplierPaymentAsync(companyId, bankAccountId, receiptLine, supplierId)).Error.Code.Should().Be("Bank.NotMoneyOut");
            (await posting.PostToAccountAsync(companyId, bankAccountId, feeLine, "1200")).Error.Code.Should().Be("Bank.ControlAccount");

            var receipt = await posting.PostCustomerReceiptAsync(companyId, bankAccountId, receiptLine, customerId);
            receipt.IsSuccess.Should().BeTrue(receipt.IsFailure ? receipt.Error.ToString() : null);
            receipt.Value.OpenItemId.Should().NotBeNull();

            var payment = await posting.PostSupplierPaymentAsync(companyId, bankAccountId, debitOrderLine, supplierId, "DO-MAR");
            payment.IsSuccess.Should().BeTrue(payment.IsFailure ? payment.Error.ToString() : null);

            var fee = await posting.PostToAccountAsync(companyId, bankAccountId, feeLine, "6800", "Bank charges");
            fee.IsSuccess.Should().BeTrue(fee.IsFailure ? fee.Error.ToString() : null);
            fee.Value.OpenItemId.Should().BeNull();

            // A reconciled line cannot be posted again.
            (await posting.PostToAccountAsync(companyId, bankAccountId, feeLine, "6800")).Error.Code.Should().Be("Bank.AlreadyReconciled");
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();

            // The receipt used the bank reference as its number and sits unallocated on the customer's account.
            OpenItem receipt = await db.OpenItems.SingleAsync(o => o.CompanyId == companyId && o.TraderId == customerId && o.Ledger == TradingLedger.AccountsReceivable);
            receipt.DocumentNumber.Should().Be("RCPT-77");
            receipt.OutstandingTransaction.Should().Be(640m);
            receipt.Sign.Should().Be(LedgerSign.Credit);

            OpenItem payment = await db.OpenItems.SingleAsync(o => o.CompanyId == companyId && o.TraderId == supplierId && o.Ledger == TradingLedger.AccountsPayable);
            payment.DocumentNumber.Should().Be("DO-MAR");
            payment.OutstandingTransaction.Should().Be(215m);

            // Every line is matched and the reconciliation agrees to the cent.
            IBankReconciliationService recon = scope.ServiceProvider.GetRequiredService<IBankReconciliationService>();
            BankReconciliationReport report = (await recon.GetReconciliationAsync(companyId, bankAccountId, new DateOnly(2026, 3, 31))).Value;
            report.StatementBalance.Should().Be(412.50m);
            report.GlBalance.Should().Be(412.50m);
            report.Difference.Should().Be(0m);
            report.UnmatchedStatementItems.Should().BeEmpty();
            report.UnpresentedGlItems.Should().BeEmpty();

            ConsolidatedTrialBalance tb = (await scope.ServiceProvider.GetRequiredService<IConsolidationService>().ConsolidatedTrialBalanceAsync([companyId], new DateOnly(2026, 3, 31))).Value;
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Single(r => r.AccountCode == "6800").Debit.Should().Be(12.50m);
        }
    }
}
